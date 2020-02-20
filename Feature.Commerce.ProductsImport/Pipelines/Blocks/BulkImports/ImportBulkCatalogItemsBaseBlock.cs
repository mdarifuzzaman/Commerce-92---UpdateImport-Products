using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Commerce.Plugin.ManagedLists;
using Sitecore.Framework.Conditions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    public abstract class ImportBulkCatalogItemsBaseBlock<TEntity, TInput, TContext> : ImportExportBaseBlock<TEntity, TInput, TContext>
    where TEntity : CommerceEntity
    where TInput : IImportArgument
    where TContext : CommercePipelineExecutionContext
    {
        protected ImportBulkCatalogItemsBaseBlock(CommerceCommander commander)
        {
            Condition.Requires(commander, nameof(commander)).IsNotNull();
            this.Commander = commander;
        }
        protected CommerceCommander Commander { get; }

        public override async Task<TInput> Run(TInput arg, TContext context)
        {
            Condition.Requires<TInput>(arg, nameof(arg)).IsNotNull<TInput>();
            Condition.Requires<IEnumerable<ZipArchiveEntry>>(arg.EntityArchiveEntries, "arg.EntityArchiveEntries").IsNotNull<IEnumerable<ZipArchiveEntry>>();
            Condition.Requires<TContext>(context, nameof(context)).IsNotNull<TContext>();
            if (this.ShouldAbort(arg, context))
            {
                context.Logger.LogWarning("{0} aborted due to excessive errors.", (object)this.GetType().Name);
                return arg;
            }
            context.Logger.LogInformation("{0} starting import from '{1}'.", (object)this.GetType().Name, (object)arg.ImportFile.FileName);
            Stopwatch sw = new Stopwatch();
            sw.Start();
            int entityCount = 0;
            //List<Task> tasks = new List<Task>();
            int processorCount = Environment.ProcessorCount;
            int maxCount = (int)Math.Pow((double)processorCount, 2.0);
            SemaphoreSlim semaphore = new SemaphoreSlim(processorCount, maxCount);
            try
            {
                JsonSerializer serializer = new JsonSerializer()
                {
                    TypeNameHandling = TypeNameHandling.All,
                    NullValueHandling = NullValueHandling.Ignore,
                    DateParseHandling = DateParseHandling.DateTimeOffset,
                    FloatParseHandling = FloatParseHandling.Decimal
                };
                int batchSize = arg.BatchSize;
                List<TEntity> batchList = new List<TEntity>(batchSize + 100);
                foreach (ZipArchiveEntry zipArchiveEntry in arg.EntityArchiveEntries.Where(archiveEntry => archiveEntry.FullName.StartsWith(typeof(TEntity).Name + ".", StringComparison.InvariantCulture)))
                {
                    using (Stream entryStream = zipArchiveEntry.Open())
                    {
                        using (StreamReader entryReader = new StreamReader(entryStream))
                        {
                            using (JsonTextReader jsonReader = new JsonTextReader((TextReader)entryReader))
                            {
                                IList<TEntity> entityList = serializer.Deserialize<IList<TEntity>>(jsonReader);
                                if (batchSize > 0)
                                {
                                    batchList.AddRange(entityList);
                                    if (batchList.Count >= batchSize)
                                    {
                                        await semaphore.WaitAsync().ConfigureAwait(false);
                                        await this.ImportEntities(arg, context, batchList).ContinueWith(_ =>
                                        {
                                            semaphore.Release();
                                        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);

                                        batchList.Clear();
                                    }
                                }
                                else
                                {
                                    await semaphore.WaitAsync().ConfigureAwait(false);
                                    await ImportEntities(arg, context, entityList).ContinueWith((Action<Task>)(_ =>
                                     {
                                         semaphore.Release();
                                     }), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                                }
                                entityCount += entityList.Count;
                                entityList = (IList<TEntity>)null;
                            }
                        }
                    }
                }
                if (batchList.Any())
                    await this.ImportEntities(arg, context, batchList);
                context.CommerceContext.ClearEntities();
                context.CommerceContext.ClearObjects();
                serializer = null;
                batchList = null;
            }
            catch (Exception ex)
            {
                context.Logger.LogWarning(string.Format("{0} encountered an unhandled exception:  {1}", (object)this.GetType().Name, (object)ex));
                throw;
            }
            finally
            {
                sw.Stop();
                context.Logger.LogInformation("{0} completed. Imported {1} entities of type {2} from '{3}' in {4} ({5} ms, {6} ms/entity).", (object)this.GetType().Name, (object)entityCount, (object)typeof(TEntity).Name, (object)arg.ImportFile.FileName, (object)sw.Elapsed, (object)sw.ElapsedMilliseconds, (object)(entityCount > 0 ? sw.ElapsedMilliseconds / (long)entityCount : 0L));
            }
            return arg;
        }

        protected virtual bool ShouldAbort(TInput arg, TContext context)
        {
            return CatalogExportHelper.ShouldAbortImport(arg, context);
        }

        protected abstract IEnumerable<string> GetListMemberships(TContext context);

        protected virtual async Task BulkImportEntities(TInput arg, TContext context, List<TEntity> preparedEntityList, bool updating = false)
        {
            Condition.Requires<TInput>(arg, nameof(arg)).IsNotNull<TInput>();
            Condition.Requires<TContext>(context, nameof(context)).IsNotNull<TContext>();
            Condition.Requires<List<TEntity>>(preparedEntityList, nameof(preparedEntityList)).IsNotNull<List<TEntity>>();
            List<PersistEntityArgument> persistEntityArgumentList = new List<PersistEntityArgument>(preparedEntityList.Count);

            if (updating)
            {
                preparedEntityList.ForEach(e => persistEntityArgumentList.Add(new PersistEntityArgument(e)));
                await this.Commander.Pipeline<IUpdateEntitiesPipeline>().Run(new PersistEntitiesArgument(persistEntityArgumentList), context);
                          
                // Clear the cache else product will be found from the cache where version is not right
                persistEntityArgumentList.ForEach(async e => await this.CacheEntities(e, context).ConfigureAwait(false));                
            }
            else
            {
                foreach (TEntity preparedEntity in preparedEntityList)
                {
                    Type type = preparedEntity.GetType();
                    if ((object)preparedEntity is VersioningEntity || (object)preparedEntity is LocalizationEntity || !VersioningPolicy.HasVersioningPolicy(context.CommerceContext, type, preparedEntity.Id))
                    {
                        persistEntityArgumentList.Add(new PersistEntityArgument((CommerceEntity)preparedEntity));
                    }
                    else
                    {
                        string idBasedOnEntityId = VersioningEntity.GetIdBasedOnEntityId(preparedEntity.Id);
                        Guid entityUniqueId = GuidUtility.GenerateEntityUniqueId(context.CommerceContext.Environment.ArtifactStoreId, idBasedOnEntityId, new int?(1));
                        VersioningEntity versioningEntity1 = new VersioningEntity();
                        versioningEntity1.Id = idBasedOnEntityId;
                        versioningEntity1.UniqueId = entityUniqueId;
                        VersioningEntity versioningEntity2 = versioningEntity1;
                        versioningEntity2.AddUpdateVersion(preparedEntity.EntityVersion, preparedEntity.Published);
                        persistEntityArgumentList.Add(new PersistEntityArgument((CommerceEntity)preparedEntity)
                        {
                            VersioningEntity = versioningEntity2
                        });
                    }
                }
                await this.Commander.Pipeline<IAddEntitiesPipeline>().Run(new PersistEntitiesArgument(persistEntityArgumentList), context).ConfigureAwait(false);
            }                     
        }

        protected virtual async Task CacheEntities(
          PersistEntityArgument argument,
          CommercePipelineExecutionContext context)
        {
            EntityCachingPolicy cachePolicy1 = EntityCachingPolicy.GetCachePolicy(context.CommerceContext, argument.Entity.GetType(), argument.Entity.Id);
            await this.CacheEntity(argument.Entity, cachePolicy1, context);
            
            if (argument.LocalizationEntity != null)
            {
                EntityCachingPolicy cachePolicy2 = EntityCachingPolicy.GetCachePolicy(context.CommerceContext, typeof(LocalizationEntity), argument.LocalizationEntity.Id);
                await this.CacheEntity((CommerceEntity)argument.LocalizationEntity, cachePolicy2, context);               
            }
            if (argument.VersioningEntity == null)
                return;
            EntityCachingPolicy cachePolicy3 = EntityCachingPolicy.GetCachePolicy(context.CommerceContext, typeof(VersioningEntity), argument.VersioningEntity.Id);
            await this.CacheEntity((CommerceEntity)argument.VersioningEntity, cachePolicy3, context);
        }

        protected virtual async Task CacheEntity(
          CommerceEntity entity,
          EntityCachingPolicy cachingPolicy,
          CommercePipelineExecutionContext context)
        {
            if (!cachingPolicy.AllowCaching)
                return;
            await this.Commander.SetEntityCacheEntry<CommerceEntity>(context.CommerceContext, entity, cachingPolicy);
        }

        protected virtual async Task ImportEntities(TInput arg,TContext context,IList<TEntity> entityList)
        {
            Condition.Requires<TInput>(arg, nameof(arg)).IsNotNull<TInput>();
            Condition.Requires<TContext>(context, nameof(context)).IsNotNull<TContext>();
            Condition.Requires<IList<TEntity>>(entityList, nameof(entityList)).IsNotNull<IList<TEntity>>();
            List<TEntity> preparedEntityList = new List<TEntity>();
            List<TEntity> updateEntityList = new List<TEntity>();
            foreach (TEntity entity in entityList)
            {
                var args = new FindEntityArgument(entity.GetType(), entity.Id, false);      
                var findEntity = await this.Commander.Pipeline<IFindEntityPipeline>().Run(args, context);
                if(findEntity != null)
                {
                    entity.Version = findEntity.Version;                     
                    updateEntityList.Add(entity);    
                    
                }
                else if (this.PrepareEntityForImport(arg, context, entity) != null)
                    preparedEntityList.Add(entity);
            }

            await this.ErrorHandleBulkImportEntities(arg, context, preparedEntityList).ConfigureAwait(false);
            await this.ErrorHandleBulkImportUpdateEntities(arg, context, updateEntityList).ConfigureAwait(false);

        }

        protected virtual TEntity PrepareEntityForImport(TInput arg, TContext context, TEntity entity)
        {
            Condition.Requires<TInput>(arg, nameof(arg)).IsNotNull<TInput>();
            Condition.Requires<TContext>(context, nameof(context)).IsNotNull<TContext>();
            Condition.Requires<TEntity>(entity, nameof(entity)).IsNotNull<TEntity>();
            entity.Version = 0;
            entity.IsPersisted = false;
            entity.UniqueId = GuidUtility.GenerateEntityUniqueId(context.CommerceContext.Environment.ArtifactStoreId, entity.Id, new int?(entity.EntityVersion));
            IEnumerable<string> listMemberships = this.GetListMemberships(context);
            if (listMemberships != null && listMemberships.Any<string>())
            {
                ListMembershipsComponent membershipsComponent = entity.GetComponent<ListMembershipsComponent>();
                foreach (string str in listMemberships.Where(listMembership => !membershipsComponent.Memberships.Contains(listMembership)))
                    membershipsComponent.Memberships.Add(str);
            }
            return entity;
        }

        private async Task ErrorHandleBulkImportEntities(TInput arg,TContext context,List<TEntity> preparedEntityList)
        {            
            if (this.ShouldAbort(arg, context))
                return;
            try
            {
                context.CommerceContext = new CommerceContext(context.CommerceContext.Logger, context.CommerceContext.TelemetryClient, (IGetLocalizableMessagePipeline)null)
                {
                    GlobalEnvironment = context.CommerceContext.GlobalEnvironment,
                    Environment = context.CommerceContext.Environment,
                    Headers = context.CommerceContext.Headers
                };
                string[] policyKeys = new string[6]
                {
                  "IgnoreIndexDeletedSitecoreItem",
                  "IgnoreLocalizeEntity",
                  "IgnoreVersioningEntity",
                  "IgnoreIndexUpdatedSitecoreItem",
                  "IgnoreAddEntityToIndexList",
                  "IgnoreCaching"
                };
                context.CommerceContext.AddPolicyKeys(policyKeys);
                await this.BulkImportEntities(arg, context, preparedEntityList).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ISSUE: explicit non-virtual call
                await context.CommerceContext.AddMessage(context.GetPolicy<KnownResultCodes>().Error, (this.Name), new object[1]
                {
                    ex
                }, null).ConfigureAwait(false);
            }
        }

        private async Task ErrorHandleBulkImportUpdateEntities(TInput arg, TContext context, List<TEntity> updateEntityList)
        {
            if (this.ShouldAbort(arg, context))
                return;
            try
            {
                context.CommerceContext = new CommerceContext(context.CommerceContext.Logger, context.CommerceContext.TelemetryClient, null)
                {
                    GlobalEnvironment = context.CommerceContext.GlobalEnvironment,
                    Environment = context.CommerceContext.Environment,
                    Headers = context.CommerceContext.Headers
                };                

                await this.BulkImportEntities(arg, context, updateEntityList, true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ISSUE: explicit non-virtual call
                await context.CommerceContext.AddMessage(context.GetPolicy<KnownResultCodes>().Error, (this.Name), new object[1]
                {
                    ex
                }, null).ConfigureAwait(false);
            }
        }
    }
}
