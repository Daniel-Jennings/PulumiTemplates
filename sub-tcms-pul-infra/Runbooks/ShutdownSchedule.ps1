workflow ShutdownSchedule
{
    Param (
        [Parameter(Mandatory=$true)]
        [Boolean]
        $Shutdown,
        [Parameter(Mandatory=$true)]
        [Boolean]
        $VerboseLogging
    )

    if($VerboseLogging) {
        Write-Output "**************************************************************************************************************";
        Write-Output "Starting ShutdownSchedule Task For Shutdown=$Shutdown";
    }

    $connectionName = "AzureRunAsConnection";
    try
    {
        $servicePrincipalConnection=Get-AutomationConnection -Name $connectionName        
 
        if($VerboseLogging) {
            Write-Output "Logging in to Azure...";
        }
        
        $naught = Add-AzureRmAccount `
            -ServicePrincipal `
            -TenantId $servicePrincipalConnection.TenantId `
            -ApplicationId $servicePrincipalConnection.ApplicationId `
            -CertificateThumbprint $servicePrincipalConnection.CertificateThumbprint 
    }
    catch {
        if (!$servicePrincipalConnection)
        {
            $ErrorMessage = "Connection $connectionName not found."
            throw $ErrorMessage
        } else{
            Write-Error -Message $_.Exception
            throw $_.Exception
        }
    }
    if($VerboseLogging) {
        Write-Output "**************************************************************************************************************";
        Write-Output "";
        Write-Output "**************************************************************************************************************";
        Write-Output "VIRTUAL MACHINES";
        Write-Output "";
    }
    
    $vmCheckCounter = 0;
    $vmPassCounter = 0;
    $vmFailCounter = 0;
    $vmWrongDayCounter = 0;
    $vmNoTagCounter = 0;
    $vmWrongTagCounter = 0;
    $allVms = Get-AzureRmResource | where {$_.ResourceType -like "Microsoft.Compute/virtualMachines"}

    if($VerboseLogging) {
        Write-Output "Checking $($allVms.Count) Virtual Machines For 'ShutdownSchedule' Tag";
    }
    
    Foreach -Parallel ($vm in $allVms) {
        $matchingTagFound = $false;
        $WORKFLOW:vmCheckCounter++;
        $vm_tags = $vm.Tags;
        if($vm_tags.Count -gt 0) {
            if($VerboseLogging) {
                Write-Output "$($vm.Name): Checking $($vm_tags.Count) Tags";
            }
            Foreach ($vm_tag_key in $vm_tags.Keys) {
                if($vm_tag_key -eq 'ShutdownSchedule') {
                    $matchingTagFound = $true;
                    $vm_tag_value = $vm_tags[$vm_tag_key];

                    $input_split = InlineScript { $USING:vm_tag_value -split ";" }
                    $input_hash = @{}
                    Foreach ($input in $input_split) { 
                        $input_object = InlineScript { $USING:input -split "=" };
                        $this_hash = @{$input_object[0]=$input_object[1]};
                        $input_hash += $this_hash;
                    }

                    $action = $input_hash["Action"];
                    $day_mask = $input_hash["DayMask"];
                    $day_of_week = (Get-Date).DayOfWeek;
                    #$day_of_week = "Sunday";
                    $day_of_week_value = $day_of_week.value__;
                    #$day_of_week_value = 0;
                    $day_from_mask = InlineScript { 
                        $day_of_week_value = $USING:day_of_week_value;
                        $day_mask = $USING:day_mask; 
                        $day_from_mask = $day_mask.Substring($day_of_week_value, 1); 
                        $day_from_mask;
                    };

                    if($day_from_mask -eq "0") {
                        if($WORKFLOW:VerboseLogging) {
                            Write-Output "$($vm.Name): Day Mask [$day_mask]; Today Is [$day_of_week]; No Action Required";
                        }
                        $WORKFLOW:vmWrongDayCounter++;
                    } else {
                        if($WORKFLOW:VerboseLogging) {
                            Write-Output "$($vm.Name): Day Mask [$day_mask]; Today Is [$day_of_week]; Action [$action] Required";
                        }
                        $action_result = $null;
                        if($WORKFLOW:Shutdown) {
                            if ($action -eq "ShutdownOnly" -Or $action -eq "ShutdownAndStartup") {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Runbook action is Shutdown, Tag action '$action' matches... shutting down";
                                }
                                $action_result = Stop-AzureRmVm -Name $vm.Name -ResourceGroupName $vm.ResourceGroupName -Force;
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Runbook action is Shutdown, Tag action '$action' does not match... skipping ";
                                }
                            }
                        } else {
                            if ($action -eq "StartupOnly" -Or $action -eq "ShutdownAndStartup") {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Runbook action is 'Startup', Tag action '$action' matches... starting up ";
                                }
                                $action_result = Start-AzureRmVm -Name $vm.Name -ResourceGroupName $vm.ResourceGroupName;
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Runbook action is 'Startup', Tag action '$action' does not match... skipping";
                                }
                            }
                        }
                        if($action_result.Status -eq 'Succeeded') {
                            if($WORKFLOW:Shutdown) {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Successfully stopped";
                                }
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Successfully started";
                                }
                            }
                            $WORKFLOW:vmPassCounter++;
                        } else {
                            if($WORKFLOW:Shutdown) {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Failed to stop";
                                }
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vm.Name): Failed to start";
                                }
                            }
                            $WORKFLOW:vmFailCounter++;
                        }
                    }
                }
            }
            if(!$matchingTagFound) {
                $WORKFLOW:vmWrongTagCounter++;
                if($WORKFLOW:VerboseLogging) {
                    Write-Output "$($vm.Name): No Matching Tags";
                }
            }
        } else {
            $WORKFLOW:vmNoTagCounter++;
        }
    }
    if($VerboseLogging) {
        Write-Output "**************************************************************************************************************";
        Write-Output "";
        Write-Output "**************************************************************************************************************";
        Write-Output "VM SCALE SETS";
        Write-Output "";
    }
    $vmssCheckCounter = 0;
    $vmssPassCounter = 0;
    $vmssFailCounter = 0;
    $vmssWrongDayCounter = 0;
    $vmssNoTagCounter = 0;
    $vmssWrongTagCounter = 0;
    $allVmss = Get-AzureRmResource | where {$_.ResourceType -like "Microsoft.Compute/virtualMachineScaleSets"};
    $m2 = $allVmss | measure;
    if($VerboseLogging) {
        Write-Output "Checking $($m2.Count) Scale Sets For 'ShutdownSchedule' Tag";
    }
    Foreach -Parallel ($vmss in $allVmss) {
        $matchingTagFound = $false;
        $WORKFLOW:vmssCheckCounter++;
        $vmss_tags = $vmss.Tags;
        if($vmss_tags.Count -gt 0) {
            if($WORKFLOW:VerboseLogging) {
                Write-Output "$($vmss.Name): Checking $($vmss_tags.Count) Tags";
            }
            Foreach ($vmss_tag_key in $vmss_tags.Keys) {
                if($vmss_tag_key -eq 'ShutdownSchedule') {
                    $matchingTagFound = $true;
                    $vmss_tag_value = $vmss_tags[$vmss_tag_key];

                    $input_split = InlineScript { $USING:vmss_tag_value -split ";" }
                    $input_hash = @{}
                    Foreach ($input in $input_split) { 
                        $input_object = InlineScript { $USING:input -split "=" };
                        $this_hash = @{$input_object[0]=$input_object[1]};
                        $input_hash += $this_hash;
                    }

                    $action = $input_hash["Action"];
                    $day_mask = $input_hash["DayMask"];
                    $day_of_week = (Get-Date).DayOfWeek;
                    #$day_of_week = "Sunday";
                    $day_of_week_value = $day_of_week.value__;
                    #$day_of_week_value = 0;
                    $day_from_mask = InlineScript { 
                        $day_of_week_value = $USING:day_of_week_value;
                        $day_mask = $USING:day_mask; 
                        $day_from_mask = $day_mask.Substring($day_of_week_value, 1); 
                        $day_from_mask;
                    };

                    if($day_from_mask -eq "0") {
                        if($WORKFLOW:VerboseLogging) {
                            Write-Output "$($vmss.Name): Day Mask [$day_mask]; Today Is [$day_of_week]; No Action Required";
                        }
                        $WORKFLOW:vmssWrongDayCounter++;
                    } else {
                        if($WORKFLOW:VerboseLogging) {
                            Write-Output "$($vmss.Name): Day Mask [$day_mask]; Today Is [$day_of_week]; Action [$action] Required";
                        }
                        $action_result = $null;
                        if($WORKFLOW:Shutdown) {
                            if ($action -eq "ShutdownOnly" -Or $action -eq "ShutdownAndStartup") {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Runbook action is Shutdown, Tag action '$action' matches... shutting down";
                                }
                                $action_result = Stop-AzureRmVmss -Name $vmss.Name -ResourceGroupName $vmss.ResourceGroupName -Force;
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Runbook action is Shutdown, Tag action '$action' does not match... skipping";
                                }
                            }
                        } else {
                            if ($action -eq "StartupOnly" -Or $action -eq "ShutdownAndStartup") {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Runbook action is 'Startup', Tag action '$action' requirement... starting up ";
                                }
                                $action_result = Start-AzureRmVmss -Name $vmss.Name -ResourceGroupName $vmss.ResourceGroupName;
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Runbook action is 'Startup', Tag action '$action' does not match... skipping";
                                }
                            }
                        }
                        if($action_result.Status -eq 'Succeeded') {
                            if($WORKFLOW:Shutdown) {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Successfully stopped";
                                }
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Successfully started";
                                }
                            }
                            $WORKFLOW:vmssPassCounter++;
                        } else {
                            if($WORKFLOW:Shutdown) {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Failed to stop";
                                }
                            } else {
                                if($WORKFLOW:VerboseLogging) {
                                    Write-Output "$($vmss.Name): Failed to start";
                                }
                            }
                            $WORKFLOW:vmssFailCounter++;
                        }
                    }
                }
            }
            if(!$matchingTagFound) {
                $WORKFLOW:vmssWrongTagCounter++;    
            }
        } else {
            $WORKFLOW:vmssNoTagCounter++;
        }
    }

    if($VerboseLogging) {
        Write-Output "**************************************************************************************************************";
    }

    InlineScript {
        $vmCountProps = @{'Counter'='Virtual Machine';'Checked'=$USING:vmCheckCounter;'Passed'=$USING:vmPassCounter;'Failed'=$USING:vmFailCounter;'No Tags'=$USING:vmNoTagCounter;'Wrong Day'=$USING:vmWrongDayCounter;'Wrong Tags'=$USING:vmWrongTagCounter};
        $vmCountObject = New-Object PSObject -Property $vmCountProps
        $vmssCountProps = @{'Counter'='VM Scale Set';'Checked'=$USING:vmssCheckCounter;'Passed'=$USING:vmssPassCounter;'Failed'=$USING:vmssFailCounter;'No Tags'=$USING:vmssNoTagCounter;'Wrong Day'=$USING:vmssWrongDayCounter;'Wrong Tags'=$USING:vmssWrongTagCounter};
        $vmssCountObject = New-Object PSObject -Property $vmssCountProps
        $countObject = @($vmCountObject;$vmssCountObject);
        $output = $countObject | Format-Table 'Counter', 'Checked', 'Passed', 'Failed', 'Wrong Day', 'Wrong Tags', 'No Tags' -AutoSize | Out-String;
        $formatted = $output -creplace '(?m)^\s*\r?\n',''
        if($USING:Verbose) {
            Write-Output "";
        }
        Write-Output "**************************************************************************************************************";
        Write-Output "TASK SUMMARY:";
        Write-Output "";
        $formatted;
        Write-Output "**************************************************************************************************************";
    }
}