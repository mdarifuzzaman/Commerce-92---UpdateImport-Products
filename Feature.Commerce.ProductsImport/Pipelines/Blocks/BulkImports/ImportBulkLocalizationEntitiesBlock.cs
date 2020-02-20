using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Pipelines;
using System.Collections.Generic;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    [PipelineDisplayName("Catalog.block.BulkImports.ImportBulkLocalizationEntitiesBlock")]
    public class ImportBulkLocalizationEntitiesBlock : ImportBulkCatalogItemsBaseBlock<LocalizationEntity, IImportArgument, CommercePipelineExecutionContext>
    {
        public ImportBulkLocalizationEntitiesBlock(CommerceCommander commander)
          : base(commander)
        {
        }

        protected override IEnumerable<string> GetListMemberships(
          CommercePipelineExecutionContext context)
        {
            return (IEnumerable<string>)null;
        }

        protected override LocalizationEntity PrepareEntityForImport(
          IImportArgument arg,
          CommercePipelineExecutionContext context,
          LocalizationEntity entity)
        {
            LocalizationEntity localizationEntity = base.PrepareEntityForImport(arg, context, entity);
            localizationEntity.RelatedEntity.EntityTargetUniqueId = GuidUtility.GenerateEntityUniqueId(context.CommerceContext.Environment.ArtifactStoreId, localizationEntity.RelatedEntity.EntityTarget, new int?(localizationEntity.EntityVersion));
            return localizationEntity;
        }
    }
}
