[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previousEnvironment = @{}
Push-Location $root
try {
    if (!(Test-Path Project/local.settings.json)) { Copy-Item Project/local.settings.example.json Project/local.settings.json }
    dotnet build Project/WeatherImages.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    # Core Tools can generate settings in the output folder. Always replace those
    # with the project's settings, even if the generated file has a newer timestamp.
    Copy-Item Project/local.settings.json Project/bin/Debug/net8.0/local.settings.json -Force
    $settings = Get-Content Project/local.settings.json -Raw | ConvertFrom-Json
    if ($settings.IsEncrypted) { throw 'Local launcher requires unencrypted Project/local.settings.json. Use local.settings.example.json for emulator testing.' }
    if (!$settings.Values.AzureWebJobsStorage) { throw 'Project/local.settings.json is missing Values.AzureWebJobsStorage.' }
    # Pass settings explicitly to both the host and its isolated child worker.
    # This also avoids Core Tools resolving settings from a different project root.
    foreach ($property in $settings.Values.PSObject.Properties) {
        $previousEnvironment[$property.Name] = [Environment]::GetEnvironmentVariable($property.Name, 'Process')
        [Environment]::SetEnvironmentVariable($property.Name, [string]$property.Value, 'Process')
    }
    if (!$settings.IsEncrypted -and $settings.Values.AzureWebJobsStorage -eq 'UseDevelopmentStorage=true') {
        foreach ($port in @(10000, 10001, 10002)) {
            $client = New-Object System.Net.Sockets.TcpClient
            try { $client.Connect('127.0.0.1', $port) }
            catch { throw "Azurite is not listening on port $port. Start it with: azurite --silent --location .artifacts/azurite --skipApiVersionCheck" }
            finally { $client.Dispose() }
        }
    }
    $bundled = Join-Path $root '.artifacts/tools/node_modules/azure-functions-core-tools/bin/func.exe'
    $tool = if (Test-Path $bundled) { $bundled } else { (Get-Command func -ErrorAction Stop).Source }
    Push-Location Project/bin/Debug/net8.0
    try { & $tool start --no-build --dotnet-isolated } finally { Pop-Location }
} finally {
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
    }
    Pop-Location
}
