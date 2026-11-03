using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace WeatherImages;

public record Station(int Id, string Name, double? Temperature, double? Humidity, double? Wind, string Description, string ObservedAt);
public record ImageJob(string RunId, Station Station, int? ImageId = null, string? PhotoId = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string RowKey => ImageId is { } index ? $"image-{index:D3}" : Station.Id.ToString();
}

public sealed class Pipeline
{
    private readonly TableClient table;
    private readonly BlobContainerClient blobs;
    private readonly QueueClient starts;
    private readonly QueueClient images;
    private readonly HttpClient http;
    private readonly SemaphoreSlim initLock = new(1, 1);
    private bool initialized;
    public Pipeline(HttpClient http)
    {
        this.http = http;
        var connection = Environment.GetEnvironmentVariable("AzureWebJobsStorage") ?? throw new InvalidOperationException("AzureWebJobsStorage is required.");
        table = new TableClient(connection, "runs");
        blobs = new BlobContainerClient(connection, "images");
        var options = new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 };
        starts = new QueueClient(connection, "start-jobs", options);
        images = new QueueClient(connection, "image-jobs", options);
    }
    private async Task Initialize()
    {
        if (initialized) return;
        await initLock.WaitAsync();
        try
        {
            if (initialized) return;
            await Task.WhenAll(table.CreateIfNotExistsAsync(), blobs.CreateIfNotExistsAsync(), starts.CreateIfNotExistsAsync(), images.CreateIfNotExistsAsync());
            initialized = true;
        }
        finally { initLock.Release(); }
    }
    private static async Task<HttpResponseData> Json(HttpRequestData request, HttpStatusCode code, object value)
    {
        var response = request.CreateResponse(code);
        await response.WriteAsJsonAsync(value);
        response.StatusCode = code;
        return response;
    }
    [Function("Health")]
    public Task<HttpResponseData> Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData request) => Json(request, HttpStatusCode.OK, new { status = "healthy" });

    [Function("CreateRun")]
    public async Task<HttpResponseData> Create([HttpTrigger(AuthorizationLevel.Function, "post", Route = "runs")] HttpRequestData request)
    {
        await Initialize();
        var id = Guid.NewGuid().ToString("N");
        await table.AddEntityAsync(new TableEntity(id, "run") { ["Status"] = "Queued", ["Expected"] = 50, ["ImagePlanVersion"] = 1, ["CreatedAt"] = DateTimeOffset.UtcNow });
        // A failed enqueue is returned as 503; no accepted run is silently lost.
        try { await starts.SendMessageAsync(id); }
        catch { await table.UpdateEntityAsync(new TableEntity(id, "run") { ["Status"] = "Failed", ["Error"] = "Unable to enqueue run." }, ETag.All, TableUpdateMode.Merge); return await Json(request, HttpStatusCode.ServiceUnavailable, new { error = "Unable to start run. Retry the request." }); }
        var root = request.Url.GetLeftPart(UriPartial.Authority);
        var response = await Json(request, HttpStatusCode.Accepted, new { id, statusUrl = $"{root}/api/runs/{id}", imagesUrl = $"{root}/api/runs/{id}/images" });
        response.Headers.Add("Location", $"{root}/api/runs/{id}");
        return response;
    }
    [Function("GetRun")]
    public Task<HttpResponseData> Status([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "runs/{id}")] HttpRequestData request, string id) => Read(request, id, false);
    [Function("GetImages")]
    public Task<HttpResponseData> Results([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "runs/{id}/images")] HttpRequestData request, string id) => Read(request, id, true);
    private async Task<HttpResponseData> Read(HttpRequestData request, string id, bool results)
    {
        if (!Guid.TryParseExact(id, "N", out _)) return await Json(request, HttpStatusCode.BadRequest, new { error = "Invalid run ID." });
        await Initialize();
        var run = await table.GetEntityIfExistsAsync<TableEntity>(id, "run");
        if (!run.HasValue) return await Json(request, HttpStatusCode.NotFound, new { error = "Run not found." });
        var rows = new List<TableEntity>();
        await foreach (var row in table.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {id}"))) if (row.RowKey != "run") rows.Add(row);
        int complete = rows.Count(r => r.GetString("Status") == "Completed"), failed = rows.Count(r => r.GetString("Status") == "Failed");
        var expected = run.Value!.GetInt32("Expected") ?? 50;
        var status = run.Value.GetString("Status") == "Failed" ? "Failed" : complete + failed == expected ? (failed == 0 ? "Completed" : "CompletedWithErrors") : run.Value.GetString("Status");
        var expires = DateTimeOffset.UtcNow.AddHours(1);
        var links = results ? rows.Where(r => r.GetString("Status") == "Completed").OrderBy(r => r.RowKey).Select(r => new { imageId = r.GetInt32("ImageId"), photoId = r.GetString("PhotoId"), stationId = r.GetInt32("StationId"), station = r.GetString("Station"), url = blobs.GetBlobClient(r.GetString("Blob")).GenerateSasUri(BlobSasPermissions.Read, expires).ToString() }).ToArray() : null;
        return await Json(request, HttpStatusCode.OK, new { id, status, expected, uniqueStations = run.Value.GetInt32("UniqueStations"), repeatedImages = run.Value.GetInt32("RepeatedImages"), completed = complete, failed, pending = Math.Max(0, expected - complete - failed), createdAt = run.Value.GetDateTimeOffset("CreatedAt"), error = run.Value.GetString("Error"), errors = rows.Where(r => r.GetString("Status") == "Failed").Select(r => new { imageId = r.GetInt32("ImageId"), station = r.GetString("Station"), error = r.GetString("Error") }), images = links, linksExpireAt = results ? expires : (DateTimeOffset?)null, attribution = "Weather: https://www.buienradar.nl | Photos: https://picsum.photos" });
    }
    [Function("StartJob")]
    public async Task Start([QueueTrigger("start-jobs", Connection = "AzureWebJobsStorage")] string id)
    {
        await Initialize();
        var run = (await table.GetEntityAsync<TableEntity>(id, "run")).Value;
        // Persist the snapshot before fan-out so retries use exactly the same stations and readings.
        Station[] stations;
        if (run.GetString("Snapshot") is { } snapshot) stations = JsonSerializer.Deserialize<Station[]>(snapshot)!;
        else
        {
            using var document = JsonDocument.Parse(await http.GetStringAsync("https://data.buienradar.nl/2.0/feed/json"));
            stations = document.RootElement.GetProperty("actual").GetProperty("stationmeasurements").EnumerateArray().Select(s => new Station(s.GetProperty("stationid").GetInt32(), s.GetProperty("stationname").GetString() ?? "Station", Number(s, "temperature"), Number(s, "humidity"), Number(s, "windspeed"), Text(s, "weatherdescription"), Text(s, "timestamp"))).DistinctBy(s => s.Id).Take(50).ToArray();
            if (stations.Length == 0) throw new InvalidOperationException("Buienradar returned no stations.");
            await table.UpdateEntityAsync(new TableEntity(id, "run") { ["Snapshot"] = JsonSerializer.Serialize(stations) }, ETag.All, TableUpdateMode.Merge);
        }
        ImageJob[] jobs;
        if (run.GetString("Jobs") is { } plan) jobs = JsonSerializer.Deserialize<ImageJob[]>(plan)!;
        else if (run.GetInt32("ImagePlanVersion") == 1)
        {
            // Use 50 distinct catalog photos, rather than random seeds which can collide.
            using var catalog = JsonDocument.Parse(await http.GetStringAsync("https://picsum.photos/v2/list?limit=100"));
            var photos = catalog.RootElement.EnumerateArray().Select(p => p.GetProperty("id").GetString()!).Distinct().OrderBy(_ => Random.Shared.Next()).Take(50).ToArray();
            if (photos.Length != 50) throw new InvalidOperationException("Picsum returned fewer than 50 distinct photos.");
            jobs = Enumerable.Range(0, 50).Select(i => new ImageJob(id, stations[i % stations.Length], i + 1, photos[i])).ToArray();
            await table.UpdateEntityAsync(new TableEntity(id, "run") { ["Jobs"] = JsonSerializer.Serialize(jobs), ["Expected"] = 50, ["UniqueStations"] = stations.Length, ["RepeatedImages"] = 50 - stations.Length, ["Status"] = "Processing" }, ETag.All, TableUpdateMode.Merge);
        }
        else
        {
            // Preserve jobs and row keys for runs created before the 50-image change.
            jobs = stations.Select(s => new ImageJob(id, s)).ToArray();
            await table.UpdateEntityAsync(new TableEntity(id, "run") { ["Expected"] = stations.Length, ["Status"] = "Processing" }, ETag.All, TableUpdateMode.Merge);
        }
        foreach (var job in jobs)
        {
            var row = await table.GetEntityIfExistsAsync<TableEntity>(id, job.RowKey);
            if (row.HasValue && row.Value!.GetString("Status") is "Completed" or "Failed") continue;
            await images.SendMessageAsync(JsonSerializer.Serialize(job));
        }
    }
    private static double? Number(JsonElement s, string name) => s.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) ? result : null;
    private static string Text(JsonElement s, string name) => s.TryGetProperty(name, out var value) ? value.ToString() : "Unknown";

    [Function("ImageJob")]
    public async Task Render([QueueTrigger("image-jobs", Connection = "AzureWebJobsStorage")] string message)
    {
        await Initialize();
        var job = JsonSerializer.Deserialize<ImageJob>(message) ?? throw new InvalidOperationException("Invalid job.");
        var station = job.Station;
        var existing = await table.GetEntityIfExistsAsync<TableEntity>(job.RunId, job.RowKey);
        if (existing.HasValue && existing.Value!.GetString("Status") is "Completed" or "Failed") return;
        // Retries overwrite the deterministic blob and row, avoiding duplicate results/counters.
        var photoUrl = job.PhotoId is { } photoId ? $"https://picsum.photos/id/{photoId}/1200/800" : $"https://picsum.photos/seed/{job.RunId}-{station.Id}/1200/800";
        var bytes = await http.GetByteArrayAsync(photoUrl);
        using var image = Image.Load(bytes);
        var fontPath = Environment.GetEnvironmentVariable("FontPath") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        var fonts = new FontCollection();
        var font = fonts.Add(fontPath).CreateFont(30);
        var text = $"{station.Name}\n{station.Temperature?.ToString("0.0") ?? "N/A"} C  |  Humidity {station.Humidity}%  |  Wind {station.Wind} m/s\n{station.Description}\nObserved: {station.ObservedAt}\nWeather: buienradar.nl | Photo: picsum.photos";
        image.Mutate(c => c.Fill(Color.FromRgba(0, 0, 0, 210), new RectangleF(0, 500, 1200, 300)).DrawText(text, font, Color.White, new PointF(24, 515)));
        using var output = new MemoryStream();
        await image.SaveAsJpegAsync(output);
        output.Position = 0;
        var blob = $"{job.RunId}/{job.RowKey}.jpg";
        await blobs.GetBlobClient(blob).UploadAsync(output, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/jpeg" } });
        var result = new TableEntity(job.RunId, job.RowKey) { ["Status"] = "Completed", ["StationId"] = station.Id, ["Station"] = station.Name, ["Blob"] = blob, ["CompletedAt"] = DateTimeOffset.UtcNow };
        if (job.ImageId is { } imageId) result["ImageId"] = imageId;
        if (job.PhotoId is { } sourcePhoto) result["PhotoId"] = sourcePhoto;
        await table.UpsertEntityAsync(result);
    }
    [Function("StartPoison")]
    public async Task StartPoison([QueueTrigger("start-jobs-poison", Connection = "AzureWebJobsStorage")] string id)
    {
        await Initialize();
        await table.UpdateEntityAsync(new TableEntity(id, "run") { ["Status"] = "Failed", ["Error"] = "Weather retrieval or fan-out failed after five attempts. See Application Insights." }, ETag.All, TableUpdateMode.Merge);
    }
    [Function("ImagePoison")]
    public async Task ImagePoison([QueueTrigger("image-jobs-poison", Connection = "AzureWebJobsStorage")] string message)
    {
        await Initialize();
        var job = JsonSerializer.Deserialize<ImageJob>(message)!;
        var row = await table.GetEntityIfExistsAsync<TableEntity>(job.RunId, job.RowKey);
        if (row.HasValue && row.Value!.GetString("Status") == "Completed") return;
        var failure = new TableEntity(job.RunId, job.RowKey) { ["Status"] = "Failed", ["StationId"] = job.Station.Id, ["Station"] = job.Station.Name, ["Error"] = "Image generation failed after five attempts. See Application Insights." };
        if (job.ImageId is { } imageId) failure["ImageId"] = imageId;
        await table.UpsertEntityAsync(failure);
    }
}
