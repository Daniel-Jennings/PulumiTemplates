// Copyright 2016-2019, Pulumi Corporation.  All rights reserved.
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Pulumi;
using PulumiFactory;

class Program
{
    static Task<int> Main()
    {
        return Deployment.RunAsync(async () => {
            var config = new Pulumi.Config();
            var companyCode = config.Require("company_code");
            var location = config.Require("location");
            var environment = config.Require("environment");
            var webAppPath = config.Require("webAppPath");
            ResourceFactory factory = new ResourceFactory(companyCode, location, environment);

            // Create a resource group
            var resourceGroup = factory.GetResourceGroup("00");

            // Create a storage account for Blobs
            var storageAccount = factory.GetStorageAccount("00", resourceGroup.Name);

            // The container to put our files into
            var storageContainer = factory.GetContainer("00", storageAccount.Name);

            // Azure SQL Server that we want to access from the application
            var administratorLoginPassword = factory.GetRandomPassword(16).Result;
            var sqlServer = factory.GetSqlServer("00", resourceGroup.Name, "manualadmin", administratorLoginPassword, "12.0");

            // Azure SQL Database that we want to access from the application
            var database = factory.GetDatabase("00", resourceGroup.Name, sqlServer.Name, "S0");

            // The connection string that has no credentials in it: authertication will come through MSI
            var connectionString = Output.Format($"Server=tcp:{sqlServer.Name}.database.windows.net;Database={database.Name};");

            // A file in Blob Storage that we want to access from the application
            var textBlob = factory.GetBlob("00", storageAccount.Name, storageContainer.Name, "block", "./README.md");

            // A plan to host the App Service
            var appServicePlanSku = factory.GetPlanSku("Basic", "B1");
            var appServicePlan = factory.GetPlan("00", resourceGroup.Name, appServicePlanSku, "App");

            // ASP.NET deployment package
            var content = new FileArchive(webAppPath);
            var blob = factory.GetZipBlob("00", storageAccount.Name, storageContainer.Name, "block", content);

            var clientConfig = await Pulumi.Azure.Core.Invokes.GetClientConfig();
            var tenantId = clientConfig.TenantId;
            var currentPrincipal = clientConfig.ObjectId;

            // Key Vault to store secrets (e.g. Blob URL with SAS)
            var vaultAccessPolicies = factory.GetKeyVaultAccessPolicy(Output.Create(tenantId), Output.Create(currentPrincipal), null, 
                new List<string> { "delete", "get", "list", "set" }, null);
            var vault = factory.GetKeyVault("00", resourceGroup.Name, Output.Create(tenantId), vaultAccessPolicies);

            // Put the URL of the zip Blob to KV
            var secret = factory.GetSecret("00", vault.Id, blob, storageAccount);
            var secretUri = Output.Format($"{secret.VaultUri}secrets/{secret.Name}/{secret.Version}");

            // The application hosted in App Service
            var app = factory.GetAppService("00", resourceGroup.Name, appServicePlan.Id, textBlob.Url, secretUri, connectionString,
                "db", "SQLAzure");

            // Work around a preview issue https://github.com/pulumi/pulumi-azure/issues/192
            var principalId = app.Identity.Apply(id => id.PrincipalId ?? "11111111-1111-1111-1111-111111111111");

            // Grant App Service access to KV secrets
            var policy = factory.GetAccessPolicy("00", vault.Id, Output.Create(tenantId), principalId, 
                secretPermissions: new List<string> { "get" });

            // Make the App Service the admin of the SQL Server (double check if you want a more fine-grained security model in your real app)
            var sqlAdmin = factory.GetActiveDirectoryAdministrator(resourceGroup.Name, Output.Create(tenantId), principalId,
                "adadmin", sqlServer.Name);

            // Grant access from App Service to the container in the storage
            var blobPermission = factory.GetAssignment("00", principalId, storageAccount.Id, storageContainer.Name,
                "Storage Blob Data Reader");

            // Add SQL firewall exceptions
            var firewallRules = app.OutboundIpAddresses.Apply(
                ips => ips.Split(",").Select(ip => factory.GetFirewallRule("00", resourceGroup.Name, ip, ip, sqlServer.Name)));

            return new Dictionary<string, object?>
            {
                { "endpoint", Output.Format($"https://{app.DefaultSiteHostname}") },
            };
        });
    }
}
