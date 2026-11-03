# Weather Images

Author: Naseer Ahmed Arsalan — student number 722589. AI was used to assist implementation; review and understand the code before submission.

An authenticated POST returns a unique run ID immediately. A Storage Queue trigger retrieves a fresh Buienradar snapshot of up to 50 distinct stations and creates exactly 50 image jobs. With 39 stations, the first 11 stations receive a second image with identical weather readings and a different photo. The job plan selects 50 distinct Picsum photo IDs. Image workers draw the readings and store JPEGs in a private blob container. Table Storage records each image separately. Public status/results APIs return progress and one-hour, read-only SAS links. Completed images are available while other images are processing.

## Run locally

For Visual Studio Community, open `WeatherImages.sln`, set `WeatherImages` as the startup project and use F5 with Azurite running. The `WeatherImages.Checks` project is a console integration-check runner; do not start it concurrently with the Functions host because they share development queues.

Prerequisites: .NET 8 runtime and SDK 8 or later, Azure Functions Core Tools v4, and Azurite with Blob, Queue and Table services. On Windows Arial is used. To run on another OS, set `FontPath` to a TrueType font.

1. Copy `Project/local.settings.example.json` to `Project/local.settings.json`.
2. Start Azurite (`npx azurite --location .artifacts/azurite`).
3. Run `dotnet build Project/WeatherImages.csproj`.
4. Run `./scripts/start-local.ps1` (or in `Project`, run `func start`).
5. Use `docs/api.http` or run `./scripts/smoke.ps1`.

Local Functions Core Tools disables key authentication by default. Azure enforces the function key on POST. Status/results are public and the random run ID acts as a capability; avoid publishing private run IDs. No secrets belong in Git.

## Deploy when ready

Follow [GitHub and Azure setup](docs/github-and-azure.md) for the complete account setup, deployment, automatic deployment and submission steps.

Deployment creates billable Azure resources. No deployment has been performed by the implementation task.

Student-cost defaults: Windows Consumption (Y1), one Standard LRS storage account shared by blobs/queues/tables, and Application Insights disabled. There is no paid always-on compute or container registry. Storage transactions/capacity, content files, bandwidth and compute beyond any applicable allowance can still consume credits. Add `-EnableMonitoring` only when paid telemetry is wanted. Redeployment does not delete monitoring resources created by an earlier deployment.

```powershell
az login
./deploy.ps1 -ResourceGroup rg-weather-images -Location francecentral
```

The script publishes using `dotnet`, provisions a Windows Consumption Function App plus Storage queues, table and private blobs using Bicep, then zip-deploys using Azure CLI. Application Insights is optional via `-EnableMonitoring`. The script waits for health, saves the URL/key in ignored `deployment.private.json`, and verifies authentication plus the 50-image pipeline. It generates `docs/api.deployed.http` with the actual HTTPS endpoint. Enter the key in your HTTP client's private environment; share it privately with the assessor. Do not commit the key.

```powershell
./scripts/smoke.ps1 -BaseUrl https://YOUR-APP.azurewebsites.net -RequestKey YOUR-KEY
```

## GitHub automation

Create a GitHub repository and push these files. Set up Azure workload identity federation for GitHub's `azure` environment and provide secrets `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, plus variable `AZURE_RESOURCE_GROUP`. The identity needs rights to deploy these resources and read deployment/function keys. The workflow builds and checks every main push. Set `ENABLE_AZURE_DEPLOY=true` to deploy automatically; manual workflow dispatch also deploys. Configure environment approval if desired. Deployment is disabled on push until enabled.

Add GitHub user `Hijdra` as repository collaborator, or invite them to your organization and project if those are used. This needs the repository owner's action. See `docs/submission.md` for the completion checklist and draft email.

## Reliability and operations

Queue triggers use Base64 messages, acknowledge/delete after success, retry failures five times and then move them to poison queues. Dedicated poison consumers mark failed runs/images in Table Storage. Weather snapshot and image-plan persistence prevent changing readings or photos mid-retry. Each image has its own ID, row and blob path, while station IDs remain the real provider IDs. Deterministic image rows prevent duplicate outputs when delivery repeats; progress is computed from image rows, avoiding counter races. A transient enqueue failure returns 503. Logs contain failed function exceptions for diagnosis. Existing runs created before this change retain their original station-based jobs and image counts.

The assessment requests 50 distinct stations, but the live feed supplied 39. At the user's request, new runs generate exactly 50 images by cycling through the available stations without altering their weather. Status reports `expected: 50`, `uniqueStations: 39`, and `repeatedImages: 11` for this feed. Results include `imageId`, `photoId`, and the original `stationId`. This produces 50 images, not 50 distinct stations; confirm this assessment discrepancy before submission. An empty feed or insufficient photo catalog retries and fails. `CompletedWithErrors` means all image attempts finished but some failed. `Failed` means initial fetch/fan-out exhausted retries. Re-run POST for a fresh batch. Expired links can be renewed by fetching results again. Stored data remains until manually cleaned up. Buienradar attribution is included in images and API responses with a hyperlink. Picsum needs no API key. NOAA is not used.

## Requirements

| Assessment | Implementation |
|---|---|
| Fast public request API + authentication | POST `/api/runs`, function key, 202 and Location |
| Current weather, 50 stations | Buienradar snapshot; 39 available stations reused for 50 images (documented discrepancy) |
| Background fan-out, multiple Storage queues | `start-jobs`, `image-jobs`, poison queues |
| Public image API and weather overlay | Picsum and ImageSharp |
| Blob storage and image retrieval | Private `images` container; GET results generates SAS |
| Progress saved in Table | `runs` table, public status endpoint |
| HTTP documentation | `docs/api.http`; deployed file generated by script |
| Bicep and dotnet/Azure CLI deployment | `infra/main.bicep`, `deploy.ps1` |
| Automated GitHub build/deploy | `.github/workflows/deploy.yml` |
| Live Azure endpoint, GitHub repo/access | User completes deployment and repository setup |

The cloud endpoint and working cloud links only exist after deployment. Local build success alone does not prove Azure deployment or live external-service availability.

See `docs/verification.md` for completed checks, live test results and remaining cloud checks. Local tools installed for verification are in ignored `.artifacts/tools`; dependencies and emulator data are also ignored.
