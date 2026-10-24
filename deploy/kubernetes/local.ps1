#Requires -Version 7.0
<#
.SYNOPSIS
    ParcelFlow on a local minikube cluster: install, port-forward, uninstall.

.DESCRIPTION
    up       Loads the locally built images (docker compose build) into minikube and installs the
             Helm chart with deploy/kubernetes/values-local.yaml, the secrets of the repository .env
             and the Keycloak realm file.
    forward  Port-forwards the portals (4201-4203), Keycloak (8180), the gateway (5100) and the
             RabbitMQ console (15672) on 127.0.0.1. Runs until Ctrl+C, or in the background with
             -Detach (stop with -Stop or with "down").
    down     Stops the port-forwards and uninstalls the release. -DeleteNamespace also removes the
             namespace (and the PostgreSQL / RabbitMQ volumes), -StopCluster stops minikube.

    Safety: every helm / kubectl call targets the context named by PARCELFLOW_KUBE_CONTEXT (default
    "parcelflow") explicitly, and the script refuses to run unless that context is a minikube cluster.
    The current kubectl context is never used.

.EXAMPLE
    docker compose build
    ./deploy/kubernetes/local.ps1 up -StartCluster
    ./deploy/kubernetes/local.ps1 forward

.NOTES
    Environment: PARCELFLOW_KUBE_CONTEXT (context and minikube profile, default parcelflow),
    PARCELFLOW_MINIKUBE_CPUS (4), PARCELFLOW_MINIKUBE_MEMORY (8g), PARCELFLOW_MINIKUBE_CNI (empty: the
    minikube default, use "calico" to enforce the network policies), HELM, KUBECTL, MINIKUBE (tool paths).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory)]
    [ValidateSet('up', 'forward', 'down')]
    [string] $Command,

    [string] $Namespace = 'parcelflow',
    [string] $Release = 'parcelflow',

    # up: start the minikube profile when it is not running.
    [switch] $StartCluster,
    # up: the images are already loaded in minikube.
    [switch] $SkipImageLoad,
    # up: Helm wait timeout.
    [string] $Timeout = '15m',

    # forward: local bind address.
    [string] $Address = '127.0.0.1',
    # forward: return immediately, leaving the port-forwards in the background.
    [switch] $Detach,
    # forward: stop the background port-forwards.
    [switch] $Stop,

    # down: delete the namespace too (PersistentVolumeClaims included).
    [switch] $DeleteNamespace,
    # down: stop the minikube profile.
    [switch] $StopCluster
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$ChartPath = Join-Path $RepoRoot 'deploy' 'helm' 'parcelflow'
$ValuesFile = Join-Path $PSScriptRoot 'values-local.yaml'
# Forward slashes: Helm parses --set-file values and treats backslashes as escapes.
$RealmFile = (Join-Path $RepoRoot 'deploy' 'keycloak' 'parcelflow-realm.json').Replace('\', '/')
$EnvFile = Join-Path $RepoRoot '.env'

$KubeContext = if ($env:PARCELFLOW_KUBE_CONTEXT) { $env:PARCELFLOW_KUBE_CONTEXT } else { 'parcelflow' }
$Helm = if ($env:HELM) { $env:HELM } else { 'helm' }
$Kubectl = if ($env:KUBECTL) { $env:KUBECTL } else { 'kubectl' }
$Minikube = if ($env:MINIKUBE) { $env:MINIKUBE } else { 'minikube' }

# Secret keys of the chart, mapped to the docker compose .env variables.
$SecretVariables = [ordered]@{
    'postgres-password'          = 'POSTGRES_PASSWORD'
    'rabbitmq-password'          = 'RABBITMQ_PASSWORD'
    'keycloak-admin-password'    = 'KEYCLOAK_ADMIN_PASSWORD'
    'notification-client-secret' = 'NOTIFICATION_CLIENT_SECRET'
    'demo-user-password'         = 'DEMO_USER_PASSWORD'
}

# The 13 images built by docker compose (parcelflow/<name>:<tag>).
$Images = @(
    'customer', 'shipment', 'tracking', 'warehouse', 'delivery', 'billing', 'notification', 'reporting',
    'gateway', 'customer-portal', 'operations-portal', 'admin-portal', 'rabbitmq'
)

# Resource prefix, as computed by the parcelflow.fullname helper (release name containing the chart name).
$Prefix = if ($Release.Contains('parcelflow')) { $Release } else { "$Release-parcelflow" }

# Local port -> service and service port.
$Forwards = @(
    [pscustomobject]@{ Name = 'customer portal'; Port = 4201; Service = "$Prefix-customer-portal"; Target = 8080; Url = 'http://localhost:4201' }
    [pscustomobject]@{ Name = 'operations portal'; Port = 4202; Service = "$Prefix-operations-portal"; Target = 8080; Url = 'http://localhost:4202' }
    [pscustomobject]@{ Name = 'admin portal'; Port = 4203; Service = "$Prefix-admin-portal"; Target = 8080; Url = 'http://localhost:4203' }
    [pscustomobject]@{ Name = 'Keycloak'; Port = 8180; Service = "$Prefix-keycloak"; Target = 8080; Url = 'http://localhost:8180/admin' }
    [pscustomobject]@{ Name = 'gateway'; Port = 5100; Service = "$Prefix-gateway"; Target = 8080; Url = 'http://localhost:5100' }
    [pscustomobject]@{ Name = 'RabbitMQ console'; Port = 15672; Service = "$Prefix-rabbitmq"; Target = 15672; Url = 'http://localhost:15672' }
)

$StateDir = Join-Path ([System.IO.Path]::GetTempPath()) "parcelflow-k8s-$KubeContext-$Namespace"
$StateFile = Join-Path $StateDir 'port-forward.json'

function Write-Step([string] $Message) {
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# Runs a native command and throws when it fails. Simple function on purpose: arguments such as
# "-p" or "--kube-context" are passed through $args instead of being bound as parameters.
function Invoke-Tool {
    $filePath = $args[0]
    $arguments = @($args | Select-Object -Skip 1)
    & $filePath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$filePath $($arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Assert-Tool([string] $FilePath, [string] $Hint) {
    if (-not (Get-Command $FilePath -ErrorAction SilentlyContinue)) {
        throw "'$FilePath' not found. $Hint"
    }
}

function Test-ClusterRunning {
    $state = & $Minikube status -p $KubeContext --format '{{.Host}}/{{.APIServer}}' 2>$null
    return ($LASTEXITCODE -eq 0 -and "$state".Trim() -eq 'Running/Running')
}

# Refuses any context that is not the minikube cluster of the profile, so that no command can reach
# another cluster. Reads the kubeconfig only, never the current context.
function Assert-LocalContext {
    $config = & $Kubectl config view -o json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $config) {
        throw 'Unable to read the kubeconfig (kubectl config view).'
    }
    $context = @($config.contexts | Where-Object { $_.name -eq $KubeContext }) | Select-Object -First 1
    if (-not $context) {
        throw "Kube context '$KubeContext' not found. Create it with: minikube start -p $KubeContext --driver=docker (or run 'up -StartCluster')."
    }
    $cluster = @($config.clusters | Where-Object { $_.name -eq $context.context.cluster }) | Select-Object -First 1
    $providers = @()
    if ($cluster -and $cluster.cluster.PSObject.Properties['extensions']) {
        $providers = @($cluster.cluster.extensions | ForEach-Object { $_.extension.provider })
    }
    if ($providers -notcontains 'minikube.sigs.k8s.io') {
        throw "Kube context '$KubeContext' is not a minikube cluster: refusing to touch it. Set PARCELFLOW_KUBE_CONTEXT to a minikube profile."
    }
}

function Read-DotEnv {
    if (-not (Test-Path $EnvFile)) {
        throw "Missing $EnvFile. Copy .env.example to .env and set every secret."
    }
    $values = @{}
    foreach ($line in Get-Content $EnvFile) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $separator = $trimmed.IndexOf('=')
        if ($separator -lt 1) { continue }
        $key = $trimmed.Substring(0, $separator).Trim()
        if ($key.StartsWith('export ')) { $key = $key.Substring(7).Trim() }
        $value = $trimmed.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and (($value[0] -eq '"' -and $value[-1] -eq '"') -or ($value[0] -eq "'" -and $value[-1] -eq "'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $values[$key] = $value
    }
    return $values
}

# YAML single-quoted scalar: only the quote itself needs escaping.
function ConvertTo-YamlString([string] $Value) {
    return "'" + $Value.Replace("'", "''") + "'"
}

function Invoke-Up {
    Assert-Tool $Minikube 'Install minikube: https://minikube.sigs.k8s.io/docs/start/'
    Assert-Tool $Kubectl 'Install kubectl or set KUBECTL.'
    Assert-Tool $Helm 'Install Helm 3 or set HELM.'

    if (-not (Test-ClusterRunning)) {
        if (-not $StartCluster) {
            throw "minikube profile '$KubeContext' is not running. Start it with 'up -StartCluster' or: minikube start -p $KubeContext --driver=docker --cpus 4 --memory 8g"
        }
        $cpus = if ($env:PARCELFLOW_MINIKUBE_CPUS) { $env:PARCELFLOW_MINIKUBE_CPUS } else { '4' }
        $memory = if ($env:PARCELFLOW_MINIKUBE_MEMORY) { $env:PARCELFLOW_MINIKUBE_MEMORY } else { '8g' }
        # --keep-context: never switch the user's current kubectl context to minikube.
        $startArgs = @('start', '-p', $KubeContext, '--keep-context', '--driver=docker', "--cpus=$cpus", "--memory=$memory")
        if ($env:PARCELFLOW_MINIKUBE_CNI) { $startArgs += "--cni=$($env:PARCELFLOW_MINIKUBE_CNI)" }
        # minikube still switches the current context when it restarts an existing profile: put the
        # user's context back afterwards.
        $previousContext = & $Kubectl config current-context 2>$null
        if ($LASTEXITCODE -ne 0) { $previousContext = $null }
        Write-Step "Starting minikube profile $KubeContext"
        Invoke-Tool $Minikube @startArgs
        if ($previousContext -and $previousContext -ne $KubeContext) {
            Invoke-Tool $Kubectl config use-context $previousContext | Out-Null
        }
    }
    Assert-LocalContext

    $dotEnv = Read-DotEnv
    $missing = @($SecretVariables.Values | Where-Object { -not $dotEnv[$_] })
    if ($missing.Count -gt 0) {
        throw "Set $($missing -join ', ') in $EnvFile."
    }
    $tag = if ($dotEnv['PARCELFLOW_TAG']) { $dotEnv['PARCELFLOW_TAG'] } else { 'local' }

    if (-not $SkipImageLoad) {
        Assert-Tool 'docker' 'Docker is needed to read the locally built images.'
        foreach ($name in $Images) {
            $image = "parcelflow/${name}:$tag"
            & docker image inspect $image *> $null
            if ($LASTEXITCODE -ne 0) {
                throw "Image $image not found locally. Build the images first: docker compose build"
            }
            Write-Step "Loading $image into minikube"
            Invoke-Tool $Minikube -p $KubeContext image load $image
        }
    }

    # Secrets go through a temporary values file (no shell quoting issue, nothing on the command line),
    # deleted right after the install.
    $secretDir = Join-Path ([System.IO.Path]::GetTempPath()) "parcelflow-secrets-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $secretDir | Out-Null
    # The Windows temporary directory is per user; elsewhere it is shared, so restrict the directory to
    # its owner before any secret is written (as local.sh does).
    if (-not $IsWindows) {
        & chmod 700 $secretDir
        if ($LASTEXITCODE -ne 0) { throw "Unable to restrict the permissions of $secretDir" }
    }
    try {
        $secretFile = Join-Path $secretDir 'secrets.yaml'
        $lines = @('secrets:', '  values:')
        foreach ($entry in $SecretVariables.GetEnumerator()) {
            $lines += "    $($entry.Key): $(ConvertTo-YamlString $dotEnv[$entry.Value])"
        }
        Set-Content -Path $secretFile -Value $lines -Encoding utf8NoBOM

        Write-Step "Installing release $Release in namespace $Namespace (context $KubeContext)"
        Invoke-Tool $Helm upgrade --install $Release $ChartPath `
            --kube-context $KubeContext --namespace $Namespace --create-namespace `
            -f $ValuesFile -f $secretFile `
            --set-file "infrastructure.keycloak.realm=$RealmFile" `
            --set-string "image.tag=$tag" `
            --wait --timeout $Timeout
    }
    finally {
        Remove-Item -Recurse -Force $secretDir -ErrorAction SilentlyContinue
    }

    Invoke-Tool $Kubectl --context $KubeContext -n $Namespace get pods
    Write-Host ''
    Write-Host 'Installed. Run "./deploy/kubernetes/local.ps1 forward" to reach the platform.'
    Write-Host "Images rebuilt with the same tag are not picked up automatically: reload them, then run"
    Write-Host "  kubectl --context $KubeContext -n $Namespace rollout restart deployment"
}

function Get-ForwardState {
    if (-not (Test-Path $StateFile)) { return @() }
    return @(Get-Content $StateFile -Raw | ConvertFrom-Json)
}

function Stop-Forwards {
    foreach ($entry in Get-ForwardState) {
        $process = Get-Process -Id $entry.Pid -ErrorAction SilentlyContinue
        if ($process -and $process.ProcessName -like 'kubectl*') {
            Stop-Process -Id $entry.Pid -Force -ErrorAction SilentlyContinue
            Write-Host "Stopped port-forward $($entry.Port) (pid $($entry.Pid))"
        }
    }
    Remove-Item -Force $StateFile -ErrorAction SilentlyContinue
}

function Test-PortFree([string] $BindAddress, [int] $Port) {
    try {
        $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Parse($BindAddress), $Port)
        $listener.Start()
        $listener.Stop()
        return $true
    }
    catch {
        return $false
    }
}

# Browsers may resolve localhost to ::1 first: another process listening there would shadow the forward.
function Test-Ipv6Listener([int] $Port) {
    $client = $null
    try {
        $client = [System.Net.Sockets.TcpClient]::new([System.Net.Sockets.AddressFamily]::InterNetworkV6)
        return $client.ConnectAsync([System.Net.IPAddress]::IPv6Loopback, $Port).Wait(300)
    }
    catch {
        return $false
    }
    finally {
        if ($client) { $client.Dispose() }
    }
}

function Start-Forward($Forward) {
    $log = Join-Path $StateDir "port-forward-$($Forward.Port).log"
    $arguments = @('--context', $KubeContext, '-n', $Namespace, 'port-forward', '--address', $Address,
        "svc/$($Forward.Service)", "$($Forward.Port):$($Forward.Target)")
    $options = @{
        FilePath               = $Kubectl
        ArgumentList           = $arguments
        PassThru               = $true
        RedirectStandardOutput = $log
        RedirectStandardError  = "$log.err"
    }
    # Hidden window on Windows: the port-forward survives the console with -Detach.
    if ($IsWindows) { $options['WindowStyle'] = 'Hidden' }
    return Start-Process @options
}

function Invoke-Forward {
    if ($Stop) {
        Stop-Forwards
        return
    }
    Assert-Tool $Kubectl 'Install kubectl or set KUBECTL.'
    Assert-LocalContext
    Stop-Forwards
    New-Item -ItemType Directory -Force -Path $StateDir | Out-Null

    $isIpAddress = [System.Net.IPAddress]::TryParse($Address, [ref] $null)
    foreach ($forward in $Forwards) {
        if ($isIpAddress -and -not (Test-PortFree $Address $forward.Port)) {
            throw "Port $Address`:$($forward.Port) is already in use ($($forward.Name))."
        }
        if ($Address -eq '127.0.0.1' -and (Test-Ipv6Listener $forward.Port)) {
            Write-Warning "Another process listens on [::1]:$($forward.Port): if localhost resolves to it, use http://127.0.0.1:$($forward.Port) or stop that process (the realm only allows localhost redirects for the portals)."
        }
    }

    $running = @{}
    foreach ($forward in $Forwards) {
        $running[$forward.Port] = Start-Forward $forward
    }
    $Forwards | ForEach-Object { [pscustomobject]@{ Port = $_.Port; Pid = $running[$_.Port].Id } } |
        ConvertTo-Json -AsArray | Set-Content -Path $StateFile -Encoding utf8NoBOM

    Write-Host ''
    foreach ($forward in $Forwards) {
        Write-Host ('  {0,-18} {1}' -f $forward.Name, $forward.Url)
    }
    Write-Host ''
    Write-Host "Logs: $StateDir"

    if ($Detach) {
        Write-Host 'Port-forwards running in the background. Stop them with: ./deploy/kubernetes/local.ps1 forward -Stop'
        return
    }

    Write-Host 'Press Ctrl+C to stop the port-forwards.'
    try {
        while ($true) {
            Start-Sleep -Seconds 2
            # A port-forward ends when its pod restarts: start it again.
            foreach ($forward in $Forwards) {
                if ($running[$forward.Port].HasExited) {
                    Write-Warning "Port-forward $($forward.Port) ($($forward.Name)) stopped, restarting it."
                    $running[$forward.Port] = Start-Forward $forward
                }
            }
            $Forwards | ForEach-Object { [pscustomobject]@{ Port = $_.Port; Pid = $running[$_.Port].Id } } |
                ConvertTo-Json -AsArray | Set-Content -Path $StateFile -Encoding utf8NoBOM
        }
    }
    finally {
        Stop-Forwards
    }
}

function Invoke-Down {
    Stop-Forwards
    Assert-Tool $Kubectl 'Install kubectl or set KUBECTL.'
    Assert-Tool $Helm 'Install Helm 3 or set HELM.'
    Assert-LocalContext

    & $Helm status $Release --kube-context $KubeContext --namespace $Namespace *> $null
    if ($LASTEXITCODE -eq 0) {
        Write-Step "Uninstalling release $Release from namespace $Namespace (context $KubeContext)"
        Invoke-Tool $Helm uninstall $Release --kube-context $KubeContext --namespace $Namespace --wait
    }
    else {
        Write-Host "Release $Release not found in namespace $Namespace."
    }

    if ($DeleteNamespace) {
        Write-Step "Deleting namespace $Namespace (volumes included)"
        Invoke-Tool $Kubectl --context $KubeContext delete namespace $Namespace --ignore-not-found --wait=true
    }
    else {
        Write-Host "PostgreSQL and RabbitMQ volumes are kept (PersistentVolumeClaims in $Namespace). Use -DeleteNamespace to remove them."
    }

    if ($StopCluster) {
        Write-Step "Stopping minikube profile $KubeContext"
        Invoke-Tool $Minikube stop -p $KubeContext
    }
}

switch ($Command) {
    'up' { Invoke-Up }
    'forward' { Invoke-Forward }
    'down' { Invoke-Down }
}
