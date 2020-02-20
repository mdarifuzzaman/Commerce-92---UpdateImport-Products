using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Conditions;
using Sitecore.Framework.Pipelines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    [PipelineDisplayName("Catalog.block.BulkImports.ImportBulkRelationshipsBlock")]
    public class ImportBulkRelationshipsBlock : PipelineBlock<IImportArgument, IImportArgument, CommercePipelineExecutionContext>
    {
        public ImportBulkRelationshipsBlock(CommerceCommander commander)
          : base((string)null)
        {
            Condition.Requires<CommerceCommander>(commander, nameof(commander)).IsNotNull<CommerceCommander>();
            this.Commander = commander;
        }

        protected CommerceCommander Commander { get; }

        public override async Task<IImportArgument> Run(
          IImportArgument arg,
          CommercePipelineExecutionContext context)
        {
            ImportBulkRelationshipsBlock relationshipsBlock = this;
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires(arg.RelationshipArchiveEntries, "arg.RelationshipArchiveEntries").IsNotNull();
            Condition.Requires(context, nameof(context)).IsNotNull();
            if (relationshipsBlock.ShouldAbort(arg, context))
            {
                context.Logger.LogWarning("{0} aborted due to excessive errors.", (object)relationshipsBlock.GetType().Name);
                return arg;
            }
            context.Logger.LogInformation("{0} starting import from '{1}'.", (object)relationshipsBlock.GetType().Name, (object)arg.ImportFile.FileName);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            int relationshipCount = 0;
            List<Task> tasks = new List<Task>();
            int processorCount = Environment.ProcessorCount;
            int maxCount = (int)Math.Pow((double)processorCount, 2.0);
            SemaphoreSlim semaphore = new SemaphoreSlim(processorCount, maxCount);
            try
            {
                JsonSerializer serializer = new JsonSerializer();
                int batchSize = arg.BatchSize;
                List<ImportRelationshipsArgument> batchList = new List<ImportRelationshipsArgument>(batchSize + 100);
                foreach (ZipArchiveEntry relationshipArchiveEntry in arg.RelationshipArchiveEntries)
                {
                    using (Stream entryStream = relationshipArchiveEntry.Open())
                    {
                        using (StreamReader entryReader = new StreamReader(entryStream))
                        {
                            using (JsonTextReader jsonReader = new JsonTextReader((TextReader)entryReader))
                            {
                                IList<ImportRelationshipsArgument> relationshipList = serializer.Deserialize<IList<ImportRelationshipsArgument>>(jsonReader);
                                if (batchSize > 0)
                                {
                                    batchList.AddRange(relationshipList);
                                    if (batchList.Count >= batchSize)
                                    {
                                        await semaphore.WaitAsync().ConfigureAwait(false);
                                        tasks.Add(relationshipsBlock.ImportRelationships(arg, context, (IList<ImportRelationshipsArgument>)batchList).ContinueWith(_ =>
                                        {
                                            semaphore.Release();
                                            tasks.Remove(_);
                                        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default));
                                        batchList.Clear();
                                    }
                                }
                                else
                                {
                                    await semaphore.WaitAsync().ConfigureAwait(false);
                                    tasks.Add(relationshipsBlock.ImportRelationships(arg, context, relationshipList).ContinueWith((Action<Task>)(_ =>
                                    {
                                        semaphore.Release();
                                        tasks.Remove(_);
                                    }), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default));
                                }
                                relationshipCount += relationshipList.Count;
                                relationshipList = null;
                            }
                        }
                    }
                }
                if (batchList.Any())
                    tasks.Add((Task)relationshipsBlock.ImportRelationships(arg, context, batchList).ContinueWith<bool>(new Func<Task, bool>(tasks.Remove), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default));
                await Task.WhenAll(tasks).ConfigureAwait(false);
                serializer = null;
                batchList = null;
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(string.Format("{0} encountered an unhandled exception: {1}", (object)relationshipsBlock.GetType().Name, (object)ex));
                throw;
            }
            finally
            {
                sw.Stop();
                context.Logger.LogInformation("{0} completed. Imported {1} relationships from '{2}' in {3} ({4} ms, {5} ms/relationship).", (object)relationshipsBlock.GetType().Name, (object)relationshipCount, (object)arg.ImportFile.FileName, (object)sw.Elapsed, (object)sw.ElapsedMilliseconds, (object)(relationshipCount > 0 ? sw.ElapsedMilliseconds / (long)relationshipCount : 0L));
            }
            return arg;
        }

        protected virtual async Task ImportRelationships(
          IImportArgument arg,
          CommercePipelineExecutionContext context,
          IList<ImportRelationshipsArgument> relationshipList)
        {
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires(context, nameof(context)).IsNotNull();
            Condition.Requires(relationshipList, nameof(relationshipList)).IsNotNull();
            ListsEntitiesArgument listsEntitiesArgument = new ListsEntitiesArgument();
            foreach (ImportRelationshipsArgument relationship in relationshipList)
            {             
                string key = relationship.RelationshipType + "-" + relationship.SourceId.SimplifyEntityName();
                if (relationship.SourceEntityVersion > 1)
                    key += string.Format("-{0}", (object)relationship.SourceEntityVersion);
                listsEntitiesArgument.ListNamesAndEntityIds.TryAdd(key, relationship.TargetIds);
            }
            await this.ErrorHandleImportRelationships(arg, context, listsEntitiesArgument).ConfigureAwait(false);
        }

        protected virtual Task BulkImportRelationships(
          IImportArgument arg,
          CommercePipelineExecutionContext context,
          ListsEntitiesArgument listsEntitiesArgument)
        {
            return this.Commander.Pipeline<IAddListsEntitiesPipeline>().Run(listsEntitiesArgument, context);
        }

        protected virtual bool ShouldAbort(
          IImportArgument arg,
          CommercePipelineExecutionContext context)
        {
            return CatalogExportHelper.ShouldAbortImport(arg, context);
        }

        private async Task ErrorHandleImportRelationships(
          IImportArgument arg,
          CommercePipelineExecutionContext context,
          ListsEntitiesArgument listsEntitiesArgument)
        {
            ImportBulkRelationshipsBlock relationshipsBlock = this;
            if (relationshipsBlock.ShouldAbort(arg, context))
                return;
            try
            {
                await relationshipsBlock.BulkImportRelationships(arg, context, listsEntitiesArgument).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ISSUE: explicit non-virtual call
                await context.CommerceContext.AddMessage(context.GetPolicy<KnownResultCodes>().Error, (relationshipsBlock.Name), new object[1]
                {
                    ex
                }, null).ConfigureAwait(false);
            }
        }
    }
}
