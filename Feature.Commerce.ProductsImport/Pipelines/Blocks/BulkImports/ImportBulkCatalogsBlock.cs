using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Conditions;
using Sitecore.Framework.Pipelines;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    [PipelineDisplayName("Catalog.block.BulkImports.ImportBulkCatalogs")]
    public class ImportBulkCatalogsBlock : ImportBulkCatalogItemsBaseBlock<Catalog, ImportCatalogsArgument, CommercePipelineExecutionContext>
    {
        public ImportBulkCatalogsBlock(CommerceCommander commander)
          : base(commander)
        {
        }

        protected override Catalog PrepareEntityForImport(
          ImportCatalogsArgument arg,
          CommercePipelineExecutionContext context,
          Catalog entity)
        {
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires(context, nameof(context)).IsNotNull();
            Condition.Requires(entity, nameof(entity)).IsNotNull();
            Catalog catalog = base.PrepareEntityForImport(arg, context, entity);
            catalog.PriceBookName = string.Empty;
            catalog.PromotionBookName = string.Empty;
            catalog.DefaultInventorySetName = string.Empty;
            return catalog;
        }

        protected override async Task BulkImportEntities(
          ImportCatalogsArgument arg,
          CommercePipelineExecutionContext context,
          List<Catalog> preparedEntityList, bool updating = false)
        {
            await base.BulkImportEntities(arg, context, preparedEntityList, updating).ConfigureAwait(false);
            foreach (CommerceEntity preparedEntity in preparedEntityList)
                context.CommerceContext.AddModel(new ImportedCatalogModel(preparedEntity.Name));
        }

        protected override IEnumerable<string> GetListMemberships(
          CommercePipelineExecutionContext context)
        {
            return (IEnumerable<string>)new string[2]
            {
            CommerceEntity.ListName<Sitecore.Commerce.Plugin.Catalog.Catalog>(),
                context.GetPolicy<KnownCatalogListsPolicy>().CatalogItems
            };
        }
    }
}
