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
            var environment = config.Require("environment");
            var companyCode = config.Require("company_code");
            var scope = config.Require("scope");
            var location = config.Require("location");
            var webAppPath = config.Require("webAppPath");
            var sourcePath = config.Require("sourcePath");
            ResourceFactory factory = new ResourceFactory(companyCode, location, environment);

            // Create a resource group
            var resourceGroup = factory.GetResourceGroup(scope: scope);

            // Create a storage account for Blobs
            var storageAccount = factory.GetStorageAccount(scope, "Standard", "LRS", resourceGroup.Name);

            // The container to put our files into
            var storageContainer = factory.GetContainer(scope: scope, storageAccountName: storageAccount.Name);

            // Azure SQL Server that we want to access from the application
            var administratorLoginPassword = factory.GetRandomPassword(length: 16).Result;
            var sqlServer = factory.GetSqlServer(scope: scope, resourceGroupName: resourceGroup.Name, administratorLogin: "manualadmin", administratorLoginPassword: administratorLoginPassword, version: "12.0");

            // Azure SQL Database that we want to access from the application
            var database = factory.GetDatabase(scope: scope, resourceGroupName: resourceGroup.Name, sqlServerName: sqlServer.Name, requestedServiceObjectiveName: "S0");

            // The connection string that has no credentials in it: authertication will come through MSI
            var connectionString = Output.Format($"Server=tcp:{sqlServer.Name}.database.windows.net;Database={database.Name};");

            // A file in Blob Storage that we want to access from the application
            var textBlob = factory.GetBlob(scope: scope, storageAccountName: storageAccount.Name, storageContainerName: storageContainer.Name, type: "block", source: sourcePath);

            // A plan to host the App Service
            var appServicePlanSku = factory.GetPlanSku(tier: "Basic", size: "B1");
            var appServicePlan = factory.GetPlan(scope: scope, resourceGroupName: resourceGroup.Name, sku: appServicePlanSku, kind: "App");

            // ASP.NET deployment package
            var content = new FileArchive(webAppPath);
            var blob = factory.GetZipBlob(scope: scope, storageAccountName: storageAccount.Name, storageContainerName: storageContainer.Name, type: "block", content: content);

            var clientConfig = await Pulumi.Azure.Core.Invokes.GetClientConfig();
            var tenantId = clientConfig.TenantId;
            var currentPrincipal = clientConfig.ObjectId;

            // Key Vault to store secrets (e.g. Blob URL with SAS)
            var vaultAccessPolicies = factory.GetKeyVaultAccessPolicy(tenantId: Output.Create(tenantId), objectId: Output.Create(currentPrincipal),
                secretPermissions: new List<string> { "delete", "get", "list", "set" });
            var vault = factory.GetKeyVault(scope: scope, resourceGroupName: resourceGroup.Name, tenantId: Output.Create(tenantId), accessPolicies: vaultAccessPolicies);

            // Put the URL of the zip Blob to KV
            var secret = factory.GetSecret(scope: scope, keyVaultId: vault.Id, blob: blob, storageAccount: storageAccount);
            var secretUri = Output.Format($"{secret.VaultUri}secrets/{secret.Name}/{secret.Version}");

            // The application hosted in App Service
            var app = factory.GetAppService(scope: scope, resourceGroupName: resourceGroup.Name, appServicePlanId: appServicePlan.Id, blobUrl: textBlob.Url, secretUri: secretUri,
                connectionString: connectionString, connectionStringName: "db", connectionStringType: "SQLAzure");

            // Work around a preview issue https://github.com/pulumi/pulumi-azure/issues/192
            var principalId = app.Identity.Apply(id => id.PrincipalId ?? "11111111-1111-1111-1111-111111111111");

            // Grant App Service access to KV secrets
            var policy = factory.GetAccessPolicy(scope: scope, keyVaultId: vault.Id, tenantId: Output.Create(tenantId), objectId: principalId,
                secretPermissions: new List<string> { "get" });

            // Make the App Service the admin of the SQL Server (double check if you want a more fine-grained security model in your real app)
            var sqlAdmin = factory.GetActiveDirectoryAdministrator(resourceGroupName: resourceGroup.Name, tenantId: Output.Create(tenantId), objectId: principalId,
                loginUsername: "adadmin", sqlServerName: sqlServer.Name);

            // Grant access from App Service to the container in the storage
            var blobPermission = factory.GetAssignment(principalId: principalId, storageAccountId: storageAccount.Id, storageContainerName: storageContainer.Name,
                roleDefinitionName: "Storage Blob Data Reader");

            // Add SQL firewall exceptions
            var firewallRules = app.OutboundIpAddresses.Apply(
                ips => ips.Split(",").Select(ip => factory.GetFirewallRule(scope: scope, resourceGroupName: resourceGroup.Name, startIpAddress: ip, endIpAddress: ip, sqlServerName: sqlServer.Name)).ToList());

            return new Dictionary<string, object?>
            {
                { "endpoint", Output.Format($"https://{app.DefaultSiteHostname}") },
            };
        });
    }
}
