using System.Collections.Generic;
using System.Threading.Tasks;

using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;

using PulumiFactory;
class Program
{
    static Task<int> Main()
    {
        return Deployment.RunAsync(() => {

            var config = new Pulumi.Config();
            var companyCode = config.Require("company_code");
            var location = config.Require("location");
            var environment = config.Require("environment");
            ResourceFactory factory = new ResourceFactory(companyCode, location, environment);

            // Create an Azure Resource Group
            var resourceGroup = factory.GetResourceGroup("00");

            // Create an Azure Storage Account
            var storageAccount = factory.GetStorageAccount("00", resourceGroup.Name);

            // Export the connection string for the storage account
            return new Dictionary<string, object?>
            {
                { "connectionString", storageAccount.PrimaryConnectionString },
            };
        });
    }
}
