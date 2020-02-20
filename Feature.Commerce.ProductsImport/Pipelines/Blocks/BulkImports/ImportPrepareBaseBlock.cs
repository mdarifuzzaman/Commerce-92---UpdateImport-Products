using Microsoft.AspNetCore.Http;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Conditions;
using Sitecore.Framework.Pipelines;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    public abstract class ImportPrepareBaseBlockV2<TInput, TContext> : PipelineBlock<TInput, TInput, TContext>
     where TInput : IImportArgument
     where TContext : CommercePipelineExecutionContext
    {
        protected CommerceCommander Commander { get; }

        protected ImportPrepareBaseBlockV2(CommerceCommander commander)
          : base((string)null)
        {
            Condition.Requires(commander, nameof(commander)).IsNotNull();
            this.Commander = commander;
        }

        public override async Task<TInput> Run(TInput arg, TContext context)
        {
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires(arg.Mode, "arg.Mode").IsNotNullOrWhiteSpace();
            string upperInvariant = arg.Mode.ToUpperInvariant();
            if (!(upperInvariant == "REPLACE"))
            {
                if (!(upperInvariant == "ADD"))
                    throw new NotImplementedException("The import mode '" + arg.Mode + "' is not implemented.");
                this.PrepareImportArguments(arg, context);
                await this.ImportAdd(arg, context).ConfigureAwait(false);
            }
            else
            {
                this.PrepareImportArguments(arg, context);
                await this.ImportReplace(arg, context).ConfigureAwait(false);
            }
            return arg;
        }

        protected virtual void PrepareImportArguments(TInput arg, TContext context)
        {
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires(arg.ImportFile, "arg.ImportFile").IsNotNull();
            Condition.Requires(context, nameof(context)).IsNotNull();
            List<ZipArchiveEntry> zipArchiveEntryList1 = new List<ZipArchiveEntry>();
            List<ZipArchiveEntry> zipArchiveEntryList2 = new List<ZipArchiveEntry>();
            ZipArchive zipArchive = new ZipArchive(arg.ImportFile.OpenReadStream(), ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in zipArchive.Entries)
            {
                if (entry.FullName.StartsWith("Relationships\\", StringComparison.OrdinalIgnoreCase) || entry.FullName.StartsWith("Relationships/", StringComparison.OrdinalIgnoreCase))
                    zipArchiveEntryList2.Add(entry);
                else
                    zipArchiveEntryList1.Add(entry);
            }
            arg.ImportArchive = zipArchive;
            arg.EntityArchiveEntries = (IEnumerable<ZipArchiveEntry>)zipArchiveEntryList1;
            arg.RelationshipArchiveEntries = (IEnumerable<ZipArchiveEntry>)zipArchiveEntryList2;
        }

        protected abstract Task ImportReplace(TInput arg, TContext context);

        protected abstract Task ImportAdd(TInput arg, TContext context);

        protected async Task AbortIfImportEntitiesAlreadyExist<TEntity>(
          TInput arg,
          TContext context)
          where TEntity : CommerceEntity
        {
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires(arg.EntityArchiveEntries, "arg.EntityArchiveEntries").IsNotNull();
            foreach (ZipArchiveEntry entityArchiveEntry in arg.EntityArchiveEntries)
            {
                if (entityArchiveEntry.FullName.StartsWith(typeof(TEntity).Name + ".", StringComparison.InvariantCulture))
                {
                    using (Stream entryStream = entityArchiveEntry.Open())
                    {
                        using (StreamReader entryReader = new StreamReader(entryStream))
                        {
                            string end = entryReader.ReadToEnd();
                            foreach (TEntity entity1 in this.Commander.Deserialize<IEnumerable<TEntity>>(context.CommerceContext, end))
                            {
                                TEntity entity = entity1;
                                var result = await this.Commander.Pipeline<IDoesEntityExistPipeline>().Run(new FindEntityArgument(typeof(TEntity), entity.Id, false), (CommercePipelineExecutionContext)context);
                                
                                if (result)
                                {
                                    CommerceContext commerceContext = context.CommerceContext;
                                    string error = context.GetPolicy<KnownResultCodes>().Error;
                                    object[] args = new object[3]
                                    {
                                        (object) typeof (TEntity).Name,
                                            (object) entity.Id,
                                            (object) context.CommerceContext.Environment.Name
                                    };
                                    string defaultMessage = "The 'add' mode import failed because a " + typeof(TEntity).Name + " entity with ID '" + entity.Id + "' already exists in the environment '" + context.CommerceContext.Environment.Name + "'.";
                                    context.Abort(await commerceContext.AddMessage(error, "EntityAlreadyExists", args, defaultMessage).ConfigureAwait(false), (object)context);
                                }
                                entity = default(TEntity);
                            }
                        }
                    }
                }
            }
        }
    }
}
