# Exercise authentication readiness without Azure calls or image generation.
$ErrorActionPreference = 'Stop'
$global:WeatherImagesAuthTestState = @{ Calls = 0; Mode = '' }
function Start-Sleep { param($Seconds) }
function Invoke-WebRequest {
    param($Uri, $Method, $Headers, [switch]$UseBasicParsing, $TimeoutSec)
    $global:WeatherImagesAuthTestState.Calls++
    $status = if ($global:WeatherImagesAuthTestState.Mode -eq 'startup') { if ($global:WeatherImagesAuthTestState.Calls -eq 1) { 503 } else { 401 } } elseif ($global:WeatherImagesAuthTestState.Mode -eq 'unauthorized-success') { 200 } else { 503 }
    [pscustomobject]@{ StatusCode = $status }
}
function Invoke-RestMethod { param($Uri, $Method, $Headers); throw 'AUTHENTICATION_PASSED' }
$smoke = Join-Path (Split-Path $PSScriptRoot -Parent) 'scripts/smoke.ps1'
foreach ($case in @('startup', 'unauthorized-success', 'unavailable')) {
    $global:WeatherImagesAuthTestState.Mode = $case
    $global:WeatherImagesAuthTestState.Calls = 0
    $message = ''
    try { & $smoke -BaseUrl 'https://example.invalid' -RequestKey 'test-only' -VerifyAuthentication }
    catch { $message = $_.Exception.Message }
    if ($case -eq 'startup' -and ($message -ne 'AUTHENTICATION_PASSED' -or $global:WeatherImagesAuthTestState.Calls -ne 3)) { throw "Startup recovery failed: $message" }
    if ($case -eq 'unauthorized-success' -and ($message -notlike '*got 200*' -or $global:WeatherImagesAuthTestState.Calls -ne 1)) { throw 'Unauthorized success was not rejected immediately.' }
    if ($case -eq 'unavailable' -and ($message -notlike '*got 503*' -or $global:WeatherImagesAuthTestState.Calls -ne 24)) { throw 'Unavailable host did not fail within the retry bound.' }
    Write-Host "PASS: $case"
}

