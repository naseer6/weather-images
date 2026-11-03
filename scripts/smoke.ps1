param([string]$BaseUrl = 'http://localhost:7071', [string]$RequestKey = '', [int]$TimeoutSeconds = 600, [switch]$VerifyAuthentication)
$ErrorActionPreference = 'Stop'
if ($VerifyAuthentication) {
    if (!$RequestKey) { throw 'RequestKey is required to verify cloud authentication.' }
    foreach ($testHeaders in @(@{}, @{ 'x-functions-key' = 'invalid-key-for-test' })) {
        for ($attempt = 1; $attempt -le 24; $attempt++) {
            $statusCode = 0
            try { $response = Invoke-WebRequest "$BaseUrl/api/runs" -Method Post -Headers $testHeaders -UseBasicParsing -TimeoutSec 10; $statusCode = [int]$response.StatusCode }
            catch {
                if ($_.Exception.Response) { $statusCode = [int]$_.Exception.Response.StatusCode }
                else { throw }
            }
            if ($statusCode -eq 401) { break }
            # Azure may still be recycling the host after accepting a ZIP deployment.
            # Retry availability errors only; a successful unauthorized request must fail.
            if ($statusCode -notin @(429, 502, 503, 504) -or $attempt -eq 24) {
                throw "Expected 401 for missing/invalid request key; got $statusCode after $attempt attempt(s). If unavailable, inspect the Function App logs."
            }
            Write-Host "Function host temporarily unavailable ($statusCode); waiting before authentication check ($attempt/24)."
            Start-Sleep -Seconds 5
        }
    }
    Write-Host 'PASS: missing and invalid request keys rejected.'
}
$headers = @{}
if ($RequestKey) { $headers['x-functions-key'] = $RequestKey }
$run = Invoke-RestMethod "$BaseUrl/api/runs" -Method Post -Headers $headers
if (!$run.id) { throw 'No run ID returned.' }
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    $status = Invoke-RestMethod "$BaseUrl/api/runs/$($run.id)"
    Write-Host "$($status.status): $($status.completed)/$($status.expected), failed=$($status.failed)"
    if ($status.status -in @('Failed', 'CompletedWithErrors')) { throw ($status | ConvertTo-Json -Depth 5) }
    if ($status.status -eq 'Completed') { break }
    if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'Timed out waiting for images.' }
    Start-Sleep -Seconds 5
} while ($true)
$results = Invoke-RestMethod "$BaseUrl/api/runs/$($run.id)/images"
if ($results.images.Count -ne 50 -or $results.expected -ne 50) { throw "Expected 50 images, got $($results.images.Count)." }
if (@($results.images.imageId | Select-Object -Unique).Count -ne 50) { throw 'Image IDs are not unique.' }
if (@($results.images.photoId | Select-Object -Unique).Count -ne 50) { throw 'Source photo IDs are not unique.' }
foreach ($image in $results.images) {
    $response = Invoke-WebRequest $image.url -Method Head -UseBasicParsing
    if ($response.Headers['Content-Type'] -notmatch 'image/jpeg') { throw 'Incorrect blob content type.' }
    if ([long]($response.Headers['Content-Length'] -join '') -le 1000) { throw 'Image unexpectedly small.' }
}
Write-Host "PASS: $($run.id) generated $($results.expected) downloadable JPEG images from $($results.uniqueStations) distinct stations ($($results.repeatedImages) repeated station images)."
[pscustomobject]@{ RunId = $run.id; BaseUrl = $BaseUrl; Status = $results.status; Images = $results.images.Count; UniqueStations = $results.uniqueStations }
