using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;
using PulumiFactory;

class Infra : Stack
{
    public Infra()
    {
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
        var storageAccount = factory.GetStorageAccount(resourceGroup.Name, tags: tags);
		
        // Export the connection string for the storage account
        this.ConnectionString = storageAccount.PrimaryConnectionString;
    }

    [Output]
    public Output<string> ConnectionString { get; set; }
}
