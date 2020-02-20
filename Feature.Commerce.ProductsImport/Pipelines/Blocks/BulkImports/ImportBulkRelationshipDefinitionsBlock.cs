using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Pipelines;
using System.Collections.Generic;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    [PipelineDisplayName("Catalog.block.BulkImports.ImportBulkRelationshipDefinitions")]
    public class ImportBulkRelationshipDefinitionsBlock : ImportBulkCatalogItemsBaseBlock<RelationshipDefinition, ImportCatalogsArgument, CommercePipelineExecutionContext>
    {
        public ImportBulkRelationshipDefinitionsBlock(CommerceCommander commander)
          : base(commander)
        {
        }

        protected override IEnumerable<string> GetListMemberships(
          CommercePipelineExecutionContext context)
        {
            return (IEnumerable<string>)new string[1]
            {
                context.GetPolicy<KnownRelationshipListsPolicy>().CustomRelationshipDefinitions
            };
        }        
    }
}
