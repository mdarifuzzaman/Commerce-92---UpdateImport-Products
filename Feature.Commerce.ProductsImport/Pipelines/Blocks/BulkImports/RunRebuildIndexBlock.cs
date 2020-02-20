using Microsoft.Extensions.Logging;
using Sitecore.Commerce.Core;
using Sitecore.Commerce.Core.Commands;
using Sitecore.Commerce.Plugin.Catalog;
using Sitecore.Framework.Conditions;
using Sitecore.Framework.Pipelines;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Feature.Commerce.ProductsImporter.Engine.Pipelines.Blocks.BulkImports
{

    [PipelineDisplayName("Catalog.block.BulkImports.RunRebuildIndexBlock")]
    public class RunRebuildIndexBlock : PipelineBlock<ImportResult, ImportResult, CommercePipelineExecutionContext>
    {
        public RunRebuildIndexBlock(CommerceCommander commander)
          : base((string)null)
        {
            Condition.Requires(commander, nameof(commander)).IsNotNull();
            this.Commander = commander;
        }

        public string MinionFullName => "Sitecore.Commerce.Plugin.Search.FullIndexMinion, Sitecore.Commerce.Plugin.Search";
        public string EnvironmentName => "[Your Shop Environment]";

        public List<Policy> Policies => new List<Policy> { new RunMinionPolicy { WithListToWatch = "CatalogItems" } };

        protected CommerceCommander Commander { get; }

        public override async Task<ImportResult> Run(ImportResult arg, CommercePipelineExecutionContext context)
        {            
            var minionCommand = this.Commander.Command<RunMinionCommand>();
            await minionCommand.Process(context.CommerceContext, MinionFullName, EnvironmentName, Policies);
            context.CommerceContext.Logger.LogWarning($"Minions triggered with {EnvironmentName} - {MinionFullName} - CatalogItems ...");
            return arg;
        }
    }
}
