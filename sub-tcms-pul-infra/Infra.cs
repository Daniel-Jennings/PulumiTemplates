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
        // Create an Azure Resource Group
        var resourceGroup = factory.GetResourceGroup();

        var storageAccount = factory.GetStorageAccount("Standard", "LRS", resourceGroup.Name);
        // Export the connection string for the storage account
        this.ConnectionString = storageAccount.PrimaryConnectionString;
    }

    [Output]
    public Output<string> ConnectionString { get; set; }
}
