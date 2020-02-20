using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Sitecore.Commerce.Core;
using Sitecore.Framework.Configuration;
using Sitecore.Framework.Pipelines.Definitions.Extensions;
using Sitecore.Commerce.Plugin.Catalog;

namespace Feature.Commerce.ProductsImporter.Engine
{
    /// <summary>
    /// Configure the services
    /// </summary>
    public class ConfigureSitecore : IConfigureSitecore
    {
        /// <summary>
        /// Register the blocks and pipelines
        /// </summary>
        /// <param name="services"></param>
        public void ConfigureServices(IServiceCollection services)
        {
            var assembly = Assembly.GetExecutingAssembly();
            services.RegisterAllPipelineBlocks(assembly);

            services.Sitecore().Pipelines(pc =>
            {                               
                pc.ConfigurePipeline<IImportCatalogsPipeline>(c =>
                {
                    c.Replace<ImportCatalogsPrepareBlock, Pipelines.Blocks.BulkImports.ImportCatalogsPrepareBlock>();
                    c.Replace<ImportBulkRelationshipDefinitionsBlock, Pipelines.Blocks.BulkImports.ImportBulkRelationshipDefinitionsBlock>();
                    c.Replace<ImportBulkLocalizationEntitiesBlock, Pipelines.Blocks.BulkImports.ImportBulkLocalizationEntitiesBlock>();
                    c.Replace<ImportBulkCatalogsBlock, Pipelines.Blocks.BulkImports.ImportBulkCatalogsBlock>();
                    c.Replace<ImportBulkSellableItemsBlock, Pipelines.Blocks.BulkImports.ImportBulkSellableItemsBlock>();
                    c.Replace<ImportBulkRelationshipsBlock, Pipelines.Blocks.BulkImports.ImportBulkRelationshipsBlock>();
                    c.Add<Pipelines.Blocks.BulkImports.RunRebuildIndexBlock>();
                });
            });            

            services.RegisterAllCommands(assembly);
        }
    }
}
