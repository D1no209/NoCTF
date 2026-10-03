[CmdletBinding()]
param([Parameter(Mandatory)][string]$Context)
. "$PSScriptRoot/Common.ps1"
trap { $failure=$_; Show-SafeFailureDiagnostics; throw $failure }
$script:Context=$Context
$prefix='nctfv-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'-'
$owned=@{}
try {
    foreach ($namespace in @('noctf','runtime','envoy-gateway-system','kube-system')) {
        $name=$prefix+$namespace
        Invoke-Kube @('create','namespace',$name) | Out-Null
        $created=Get-KubeJson @('get','namespace',$name)
        $owned[$name]=$created.metadata.uid
    }
    foreach ($environment in @('kind','production')) {
        $path=Join-Path $script:StateRoot "schema-$environment.yaml"
        Invoke-Checked kubectl @('kustomize',(Join-Path $script:KubernetesRoot "overlays/$environment")) | Set-Content $path -Encoding utf8NoBOM
        # kubectl emits one top-level JSON object per YAML document.
        $raw=Invoke-Kube @('create','--dry-run=client','--validate=false','-f',$path,'-o','json') | Out-String
        $array='['+[regex]::Replace($raw,'(?m)^\}\s*\r?\n\{',"},`n{")+']'
        $items=@(($array | ConvertFrom-Json -Depth 100) | Where-Object kind -ne 'Namespace')
        $resources=@{apiVersion='v1';kind='List';items=$items}
        foreach ($resource in $resources.items) {
            $resource.metadata.name=$prefix+$resource.metadata.name
            if ($resource.metadata.PSObject.Properties.Name -contains 'namespace') {
                $resource.metadata.namespace=$prefix+$resource.metadata.namespace
            }
        }
        $jsonPath=Join-Path $script:StateRoot "schema-$environment.json"
        Save-Json $jsonPath $resources
        Invoke-Kube @('create','--dry-run=server','-f',$jsonPath) | Out-Null
        Write-Host "$environment schema validation passed ($($resources.items.Count) resources)."
    }
} finally {
    foreach ($name in $owned.Keys) {
        $current=Get-KubeJson @('get','namespace',$name)
        if ($current.metadata.uid -eq $owned[$name]) { Invoke-Kube @('delete','namespace',$name,'--wait=false') | Out-Null }
    }
}
