using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Pulumi;
using Pulumi.Azure.KeyVault.Inputs;
using Pulumi.AzureAD.Inputs;
using PulumiFactory;

class Program
{
    static Task<int> Main()
    {
        return Deployment.RunAsync(async () => {

            // Get the configuration and required variables
            var config = new Pulumi.Config();
            var location = config.Require("location");
            var companyCode = config.Require("company_code");
            var environment = config.Require("environment");
            var scope = config.Require("default_scope");
            var tenantId = Output.Create<string>(config.Require("tenant_id"));
            var rbacGroups = config.RequireObject<JsonElement>("rbac_groups");
            List<string> rbacGroupsList = new List<string>();
            foreach (JsonElement group in rbacGroups.EnumerateArray())
            {
                rbacGroupsList.Add(group.ToString());
            }

            // Create the factory
            ResourceFactory factory = new ResourceFactory(companyCode, location, environment, scope);

            // Create the tags to be applied to these resources: scope
            Dictionary<string, string> tags = new Dictionary<string, string>();
            tags.Add("scope", scope);

            // Create the resource group, analytics workspace and automation account
            var resourceGroup = factory.GetResourceGroup(tags: tags);
            var workspace = factory.GetAnalyticsWorkspace(resourceGroupName: resourceGroup.Name, tags: tags);
            var automationAccount = factory.GetAutomationAccount(resourceGroupName: resourceGroup.Name, tags: tags);

            #region Runbooks
            // Runbook for UpdatePowershellModules
            DateTime now = DateTime.Now;
            int start = (int)now.DayOfWeek;
            int target = (int)DayOfWeek.Sunday;
            target = (target <= start ? target + 7 : target);
            DateTime nextSunday = now.AddDays(target - start);
            string publishContentLink = "https://raw.githubusercontent.com/Daniel-Jennings/PulumiTemplates/master/sub-tcms-pul-infra/Runbooks/UpdatePowershellModules.ps1";
            string runbookDescription = "A runbook to update all of the Powershell modules used in the automation account. Should be run weekly to ensure latest module code is available";
            string startTime = nextSunday.ToString("yyyy'-'MM'-'dd") + "T20:00:00-04:00";
            string scheduleDescription = "Run a task weekly on Sunday at 8PM";
            Dictionary<string, string> parameters = new Dictionary<string, string> {
                { "resourcegroupname", factory.ResourceNames["rg"][0] },
                { "automationaccountname", factory.ResourceNames["aacc"][0] }
            };
            var updatePowershellModulesRunbook = factory.GetAutomationRunbook(name: "UpdatePowershellModules", resourceGroupName: resourceGroup.Name,
                automationAccountName: automationAccount.Name, publishContentLink: publishContentLink, description: runbookDescription, runbookType: "PowerShell", tags: tags);
            var updatePowershellModulesSchedule = factory.GetAutomationSchedule(name: "WeeklySunday8PM", resourceGroupName: resourceGroup.Name, automationAccountName: automationAccount.Name,
                description: scheduleDescription, frequency: "Week", startTime: startTime, timezone: "America/Toronto", interval: 1, weekDays: new List<string> { "Sunday" });
            var updatePowershellModulesJob = factory.GetAutomationJobSchedule(resourceGroupName: resourceGroup.Name, automationAccountName: automationAccount.Name,
                runbookName: updatePowershellModulesRunbook.Name, scheduleName: updatePowershellModulesSchedule.Name,
                parameters: parameters);

            // Runbook for ShutdownSchedule
            DateTime tomorrow = now.AddDays(1);
            publishContentLink = "https://raw.githubusercontent.com/Daniel-Jennings/PulumiTemplates/master/sub-tcms-pul-infra/Runbooks/ShutdownSchedule.ps1";
            runbookDescription = "A runbook to control shutdown / startup schedules for VMs and Scale Sets";
            var shutdownScheduleRunbook = factory.GetAutomationRunbook(name: "ShutdownSchedule", resourceGroupName: resourceGroup.Name,
                automationAccountName: automationAccount.Name, publishContentLink: publishContentLink, description: runbookDescription, runbookType: "PowerShellWorkflow", tags: tags);

            // Schedule for Startup
            startTime = tomorrow.ToString("yyyy'-'MM'-'dd") + "T07:00:00-04:00";
            scheduleDescription = "Run a task daily at 7AM";
            parameters = new Dictionary<string, string> {
                { "shutdown", "false" },
                { "verboselogging", "false" }
            };
            var shutdownScheduleScheduleStartup = factory.GetAutomationSchedule(name: "Daily7AM", resourceGroupName: resourceGroup.Name, automationAccountName: automationAccount.Name,
                description: scheduleDescription, frequency: "Day", startTime: startTime, timezone: "America/Toronto", interval: 1);
            var shutdownScheduleJobStartup = factory.GetAutomationJobSchedule(resourceGroupName: resourceGroup.Name, automationAccountName: automationAccount.Name,
                runbookName: shutdownScheduleRunbook.Name, scheduleName: shutdownScheduleScheduleStartup.Name,
                parameters: parameters);

            // Schedule for Shutdown
            startTime = tomorrow.ToString("yyyy'-'MM'-'dd") + "T19:00:00-04:00";
            scheduleDescription = "Run a task daily at 7PM";
            parameters = new Dictionary<string, string> {
                { "shutdown", "true" },
                { "verboselogging", "false" }
            };
            var shutdownScheduleScheduleShutdown = factory.GetAutomationSchedule(name: "Daily7PM", resourceGroupName: resourceGroup.Name, automationAccountName: automationAccount.Name,
               description: scheduleDescription, frequency: "Day", startTime: startTime, timezone: "America/Toronto", interval: 1);
            var shutdownScheduleJobShutdown = factory.GetAutomationJobSchedule(resourceGroupName: resourceGroup.Name, automationAccountName: automationAccount.Name,
                runbookName: shutdownScheduleRunbook.Name, scheduleName: shutdownScheduleScheduleShutdown.Name,
                parameters: parameters);
            #endregion
            // Return any outputs that may be required for subsequent steps
            return new Dictionary<string, object?>
            {
                { "resourceGroupId", resourceGroup.Id },
                { "resourceGroupName", resourceGroup.Name },
                { "workspaceId", workspace.WorkspaceId },
            };
        });
    }
}