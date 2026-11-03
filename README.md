# Weather Images

Naseer Ahmed Arsalan, student number 722589.

Azure Functions application that generates 50 weather images using Buienradar observations and Picsum photos. A request returns a run ID immediately. Storage Queue workers fetch weather, fan out image jobs, draw weather overlays and upload JPEGs to private Blob Storage. Progress is stored in Table Storage, and results contain read-only SAS links valid for one hour.


## API

Examples: [local API](docs/api.http) and [deployed API](docs/api.deployed.http).

| Method | Endpoint | Purpose |
|---|---|---|
| GET | `/api/health` | Health check |
| POST | `/api/runs` | Start a batch; requires `x-functions-key` on Azure |
| GET | `/api/runs/{id}` | Progress and errors |
| GET | `/api/runs/{id}/images` | Generated image links |


## Run locally

Requirements: .NET 8 runtime and SDK 8 or later, Azure Functions Core Tools v4, Node.js and Azurite. Windows uses Arial; configure `FontPath` for another operating system.

Copy `Project/local.settings.example.json` to `Project/local.settings.json`. Start Azurite in a separate terminal:

```powershell
azurite --silent --location .artifacts/azurite --skipApiVersionCheck
```

Start the app:

```powershell
.\scripts\start-local.ps1
```

Test it from another terminal:

```powershell
.\scripts\smoke.ps1
```

For Visual Studio Community, open `WeatherImages.sln`, set `WeatherImages` as the startup project and press F5 with Azurite running. Local Core Tools disables key authentication by default.
