[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [string]$Location = 'westeurope',
    [string]$NamePrefix = 'weatherimages',
    [string]$Subscription
)
$ErrorActionPreference = 'Stop'
function Assert-Exit([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed ($LASTEXITCODE)." } }
Push-Location $PSScriptRoot
try {
    if ($Subscription) { az account set --subscription $Subscription; Assert-Exit 'Select subscription' }
    az account show --output none; Assert-Exit 'Azure login (run az login first)'
    dotnet publish Project/WeatherImages.csproj -c Release -o .artifacts/publish; Assert-Exit 'Publish'
    # The zip root must contain host.json, not the publish directory itself.
    Compress-Archive -Path .artifacts/publish/* -DestinationPath .artifacts/app.zip -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $PSScriptRoot '.artifacts/app.zip'))
    try {
        $entries = @($archive.Entries.FullName.Replace('\', '/'))
        foreach ($required in @('host.json', 'functions.metadata', 'worker.config.json', 'extensions.json', '.azurefunctions/Microsoft.Azure.WebJobs.Extensions.Storage.Queues.dll')) {
            if ($required -notin $entries) { throw "Deployment zip is missing $required." }
        }
    } finally { $archive.Dispose() }
    az group create --name $ResourceGroup --location $Location --output none; Assert-Exit 'Create resource group'
    $raw = az deployment group create --resource-group $ResourceGroup --template-file infra/main.bicep --parameters namePrefix=$NamePrefix --query properties.outputs -o json
    Assert-Exit 'Deploy infrastructure'
    $outputs = ($raw -join "`n") | ConvertFrom-Json
    $app = $outputs.functionAppName.value
    $base = $outputs.baseUrl.value
    az functionapp deployment source config-zip --resource-group $ResourceGroup --name $app --src .artifacts/app.zip --output none
    Assert-Exit 'Deploy function package'
    $healthy = $false
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { $health = Invoke-RestMethod "$base/api/health"; if ($health.status -eq 'healthy') { $healthy = $true; break } } catch { }
        Start-Sleep -Seconds 10
    }
    if (!$healthy) { throw 'Function did not become healthy. Check Azure logs.' }
    $key = az functionapp keys list --resource-group $ResourceGroup --name $app --query functionKeys.default -o tsv
    Assert-Exit 'Read request credential'
    if (!$key) { throw 'No function key returned.' }
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Output "::add-mask::$key" }
    @{ baseUrl = $base; requestKey = $key; resourceGroup = $ResourceGroup; functionApp = $app } | ConvertTo-Json | Set-Content deployment.private.json
    $verification = & ./scripts/smoke.ps1 -BaseUrl $base -RequestKey $key -VerifyAuthentication
    $template = Get-Content docs/api.http -Raw
    $template.Replace('http://localhost:7071', $base).Replace('REPLACE_WITH_RETURNED_ID', $verification.RunId) | Set-Content docs/api.deployed.http
    @{ baseUrl = $base; runId = $verification.RunId; status = $verification.Status; images = $verification.Images; uniqueStations = $verification.UniqueStations; verifiedAt = [DateTimeOffset]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content docs/deployment-verification.json
    Write-Host "Deployed and verified 50 images at $base. Credential saved in ignored deployment.private.json. Set requestKey in your HTTP client."
} finally { Pop-Location }
