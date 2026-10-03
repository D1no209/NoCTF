[CmdletBinding()]
param([Parameter(Mandatory)][string]$Context)
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context = $Context
$script:Diagnosing = $true
# Deliberately exclude Pod specifications, environment, logs and event messages:
# provider errors and application payloads may contain protected Flag/token data.
$pods = Get-KubeJson @('get','pods','-A')
$report = @($pods.items | ForEach-Object {
    $pod=$_
    $node=if ($pod.spec.PSObject.Properties.Name -contains 'nodeName') { $pod.spec.nodeName } else { $null }
    $conditions=if ($pod.status.PSObject.Properties.Name -contains 'conditions') { @($pod.status.conditions | ForEach-Object {
        $reason=if ($_.PSObject.Properties.Name -contains 'reason') { $_.reason } else { $null }
        @{type=$_.type;status=$_.status;reason=$reason}
    }) } else { @() }
    @{namespace=$pod.metadata.namespace;pod=$pod.metadata.name;node=$node;phase=$pod.status.phase;conditions=$conditions}
})
$report | ConvertTo-Json -Depth 8
Invoke-Kube @('get','nodes','-o','wide')
Invoke-Kube @('-n','noctf','get','pvc')
Invoke-Kube @('-n','runtime','get','service','-o','wide')
