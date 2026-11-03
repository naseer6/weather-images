# Verification — 30 September 2026

## Final preparation — 3 November 2026

- Debug build of app and checks passed with zero errors/warnings.
- Release publish completed successfully. A fresh ZIP of the published output includes host/worker metadata and the queue extension assemblies.
- Fresh Azurite fixture integration run passed: 39 stations, 50 independent jobs/photos, unchanged repeated readings, 50 completed image rows/blobs, duplicate delivery, SAS download and poison states.
- Updated Bicep compiled successfully; Function App explicitly uses a 64-bit worker.
- PowerShell scripts passed syntax checks; deployment archive checks passed against the existing release archive.
- Deployment now runs authentication checks and the full 50-image cloud smoke test, then writes public deployment evidence. GitHub CI waits for all emulator ports before testing and uploads only public evidence after deployment.
- Exact account setup instructions are in `docs/github-and-azure.md`.

These cloud checks are prepared, not executed: no Azure deployment or GitHub account configuration was performed. The earlier live local test below remains the external API evidence.

## Earlier verification

No Azure resources were deployed. No GitHub repository was created, no collaborator invited, and no email sent.

Passed locally:

- Debug build: zero errors and warnings.
- Release `dotnet publish`: succeeded; package contains metadata for eight functions.
- `az bicep build`: template compiled successfully.
- PowerShell syntax checks for deployment and smoke scripts.
- Azurite integration test with a 50-station fixture: 50 queue messages, JPEG generation, 50 completed Table rows, duplicate-delivery handling, acknowledged/deleted messages, SAS downloads, and poison failure states.
- Actual Azure Functions v4 host with live Buienradar and Picsum access: POST started run `162bee98b1f64556bd6f5891c07b8e62`; asynchronous completion produced 39 images, no failures. The smoke script verified every image URL returned JPEG content.
- Live HTTP health returned 200; invalid run ID returned 400; unknown run returned 404.
- Image overlay visually inspected on a generated fixture JPEG.

Update: the user requested exactly 50 images using the 39 available stations, reusing 11 with different photos and unchanged weather. New runs persist 50 distinct photo IDs and independent image job IDs; existing runs retain their original counts. The updated fixture uses 39 stations and verifies 50 jobs, 50 distinct photos, identical readings for repeated stations, 50 separate blobs, retry deduplication and poison handling. The build passes with zero warnings/errors.

The updated live HTTP smoke test passed for run `c1b1a7072d364b05a4131647404ca9b5`: `Completed`, 50/50 images, zero failures, all 50 JPEG URLs downloadable. This supersedes the earlier 39-image result for new runs. The feed still contains 39 distinct stations; producing 50 images does not create 50 distinct stations. No NOAA integration was added.

Still requires cloud verification after deployment: key authentication enforcement, Azure host/font availability, actual HTTPS links, GitHub identity federation and automatic deployment. The deployment script waits for health; run the smoke script afterward to verify the full cloud pipeline.

To reproduce fixture checks, start Azurite and stop the Functions host first (it would consume test queue messages), then run:

```powershell
dotnet run --project tests/WeatherImages.Checks.csproj
```

The test clears the local emulator's `image-jobs` queue and hardcodes development storage. Use an isolated development emulator. `--live` swaps fixtures for actual public APIs.
