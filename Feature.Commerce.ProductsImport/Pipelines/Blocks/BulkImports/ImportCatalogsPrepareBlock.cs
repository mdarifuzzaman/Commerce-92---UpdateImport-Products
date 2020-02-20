using Sitecore.Commerce.Core;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Conditions;
using Sitecore.Framework.Pipelines;
using System;
using System.Threading.Tasks;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{
    [PipelineDisplayName("Catalog.block.BulkImports.ImportCatalogsPrepare")]
    public class ImportCatalogsPrepareBlock : ImportPrepareBaseBlockV2<ImportCatalogsArgument, CommercePipelineExecutionContext>
    {
        public ImportCatalogsPrepareBlock(CommerceCommander commander)
          : base(commander)
        {
        }

        protected override async Task ImportReplace(
          ImportCatalogsArgument arg,
          CommercePipelineExecutionContext context)
        {
            if(arg.Mode == "replace" || arg.Mode == "add")
            {
                await this.Commander.Pipeline<IRemoveAllCatalogItemsPipeline>().Run(new RemoveAllCatalogItemsArgument(), context).ConfigureAwait(false);
            }           
        }

        public override async Task<ImportCatalogsArgument> Run(ImportCatalogsArgument arg, CommercePipelineExecutionContext context)
        {
            Condition.Requires(arg, nameof(arg)).IsNotNull();
            Condition.Requires<string>(arg.Mode, "arg.Mode").IsNotNullOrWhiteSpace();
            string upperInvariant = arg.Mode.ToUpperInvariant();

            this.PrepareImportArguments(arg, context);

            if (upperInvariant == "REPLACE")
            {
                await this.ImportReplace(arg, context).ConfigureAwait(false);
            }
            else if(upperInvariant == "ADD")
            {
                await this.ImportAdd(arg, context).ConfigureAwait(false);
            }
            else if(upperInvariant == "UPDATE")
            {
                await this.ImportReplace(arg, context).ConfigureAwait(false);
            }
            else
            {
                throw new NotImplementedException("The import mode '" + arg.Mode + "' is not implemented.");
            }

            return arg;
        }

        protected override async Task ImportAdd(
          ImportCatalogsArgument arg,
          CommercePipelineExecutionContext context)
        {
            await this.AbortIfImportEntitiesAlreadyExist<Sitecore.Commerce.Plugin.Catalog.Catalog>(arg, context).ConfigureAwait(false);
        }
    }
}
