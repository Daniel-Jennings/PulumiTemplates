using System.Collections.Generic;
using System.Threading.Tasks;
using Pulumi;
using PulumiFactory;

class Program
{
    static Task<int> Main()
    {
        return Deployment.RunAsync(async () => {
            var config = new Pulumi.Config();
            var location = config.Require("location");
            var companyCode = config.Require("company_code");
            var environment = config.Require("environment");
            var scope = config.Require("default_scope");

            ResourceFactory factory = new ResourceFactory(companyCode, location, environment, scope);

            // Create the tags to be applied to these resources: scope
            Dictionary<string, string> tags = new Dictionary<string, string>();
            tags.Add("scope", scope);

            // Create an Azure Resource Group and storage account
            var resourceGroup = factory.GetResourceGroup(tags: tags);
            var workspace = factory.GetAnalyticsWorkspace(resourceGroup.Name, tags: tags);

            return new Dictionary<string, object?>
            {
                { "resourceGroupId", resourceGroup.Id },
                { "resourceGroupName", resourceGroup.Name },
                { "workspaceId", workspace.WorkspaceId }
            };
        });
    }
}