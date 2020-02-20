using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Pipelines;
using System.Collections.Generic;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    [PipelineDisplayName("Catalog.block.BulkImports.ImportSellableItems")]
    public class ImportBulkSellableItemsBlock : ImportBulkCatalogItemsBaseBlock<SellableItem, ImportCatalogsArgument, CommercePipelineExecutionContext>
    {
        public ImportBulkSellableItemsBlock(CommerceCommander commander)
          : base(commander)
        {
        }

        protected override SellableItem PrepareEntityForImport(
          ImportCatalogsArgument arg,
          CommercePipelineExecutionContext context,
          SellableItem entity)
        {
            SellableItem sellableItem = base.PrepareEntityForImport(arg, context, entity);
            sellableItem.ProductId = entity.ProductId.ProposeValidId();
            return sellableItem;
        }

        protected override IEnumerable<string> GetListMemberships(
          CommercePipelineExecutionContext context)
        {
            return (IEnumerable<string>)new string[2]
            {
                CommerceEntity.ListName<SellableItem>(),
                context.GetPolicy<KnownCatalogListsPolicy>().CatalogItems
            };
        }
    }
}
