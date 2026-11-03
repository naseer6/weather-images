using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using SixLabors.ImageSharp;
using WeatherImages;

Environment.SetEnvironmentVariable("AzureWebJobsStorage", "UseDevelopmentStorage=true");
Environment.SetEnvironmentVariable("FontPath", "C:/Windows/Fonts/arial.ttf");
var live = args.Contains("--live");
using var http = live ? new HttpClient { Timeout = TimeSpan.FromSeconds(45) } : new HttpClient(new FixtureHandler());
var pipeline = new Pipeline(http);
var table = new TableClient("UseDevelopmentStorage=true", "runs");
await table.CreateIfNotExistsAsync();
var queue = new QueueClient("UseDevelopmentStorage=true", "image-jobs", new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });
await queue.CreateIfNotExistsAsync();
await queue.ClearMessagesAsync(); // Only run against the isolated development emulator.
var id = Guid.NewGuid().ToString("N");
await table.AddEntityAsync(new TableEntity(id, "run") { ["Status"] = "Queued", ["Expected"] = 50, ["ImagePlanVersion"] = 1 });
await pipeline.Start(id);
var expected = (await table.GetEntityAsync<TableEntity>(id, "run")).Value.GetInt32("Expected")!.Value;
Check(expected == 50, "Exactly 50 images expected");
var planned = JsonSerializer.Deserialize<ImageJob[]>((await table.GetEntityAsync<TableEntity>(id, "run")).Value.GetString("Jobs")!)!;
Check(planned.Length == 50 && planned.Select(j => j.RowKey).Distinct().Count() == 50, "50 independent image job IDs");
Check(planned.Select(j => j.PhotoId).Distinct().Count() == 50, "50 different source photos");
Check(planned.GroupBy(j => j.Station.Id).All(g => g.Select(j => j.Station).Distinct().Count() == 1), "Repeated stations retain identical weather readings");
if (!live) Check(planned.Select(j => j.Station.Id).Distinct().Count() == 39, "39 real station IDs reused across 50 images");
var count = 0;
while (true)
{
    var messages = (await queue.ReceiveMessagesAsync(32, TimeSpan.FromMinutes(5))).Value;
    if (messages.Length == 0) break;
    foreach (var message in messages)
    {
        var job = JsonSerializer.Deserialize<ImageJob>(message.MessageText)!;
        Check(job.RunId == id, "Fan-out message run ID");
        await pipeline.Render(message.MessageText);
        await pipeline.Render(message.MessageText); // At-least-once duplicate delivery.
        await queue.DeleteMessageAsync(message.MessageId, message.PopReceipt);
        count++;
    }
}
Check(count == expected, $"Exactly {expected} fan-out messages (actual {count})");
var rows = new List<TableEntity>();
await foreach (var row in table.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"PartitionKey eq {id}"))) rows.Add(row);
Check(rows.Count(r => r.GetString("Status") == "Completed") == expected, $"{expected} completed image rows");
Check(rows.Where(r => r.RowKey != "run").Select(r => r.GetString("Blob")).Distinct().Count() == 50, "50 separate blob paths");
var blobs = new BlobContainerClient("UseDevelopmentStorage=true", "images");
var sample = blobs.GetBlobClient(rows.First(r => r.RowKey != "run").GetString("Blob"));
using var stream = new MemoryStream();
await sample.DownloadToAsync(stream);
stream.Position = 0;
using var image = Image.Load(stream);
Check(image.Width == 1200 && image.Height == 800, "JPEG image dimensions");
Directory.CreateDirectory(".artifacts");
stream.Position = 0;
await File.WriteAllBytesAsync(".artifacts/sample.jpg", stream.ToArray());
var sas = sample.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddMinutes(5));
using var sasHttp = new HttpClient();
Check((await sasHttp.GetAsync(sas)).IsSuccessStatusCode, "Read-only SAS download");
await pipeline.Start(id);
Check((await queue.PeekMessagesAsync()).Value.Length == 0, "Completed fan-out retry enqueues no duplicate work");
var failedId = Guid.NewGuid().ToString("N");
await table.AddEntityAsync(new TableEntity(failedId, "run") { ["Status"] = "Queued" });
await pipeline.StartPoison(failedId);
Check((await table.GetEntityAsync<TableEntity>(failedId, "run")).Value.GetString("Status") == "Failed", "Start poison recorded");
await pipeline.ImagePoison(JsonSerializer.Serialize(new ImageJob(failedId, new Station(1, "Failed station", null, null, null, "Unknown", "Unknown"))));
Check((await table.GetEntityAsync<TableEntity>(failedId, "1")).Value.GetString("Status") == "Failed", "Image poison recorded");
Console.WriteLine($"PASS: {(live ? "live" : "fixture")} feed, {expected} images, queue acknowledgement, duplicate delivery, SAS, poison failure states.");

static void Check(bool condition, string label) { if (!condition) throw new Exception(label); Console.WriteLine($"PASS: {label}"); }

sealed class FixtureHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.Host == "data.buienradar.nl")
        {
            var stations = Enumerable.Range(1, 39).Select(i => new { stationid = i, stationname = $"Station {i}", temperature = i == 1 ? (double?)null : 18.5, humidity = 75, windspeed = 3.2, weatherdescription = "Partly cloudy", timestamp = "2026-09-30T12:00:00" });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { actual = new { stationmeasurements = stations } }), Encoding.UTF8, "application/json") });
        }
        if (request.RequestUri!.AbsolutePath == "/v2/list")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Enumerable.Range(1, 100).Select(i => new { id = i.ToString() })), Encoding.UTF8, "application/json") });
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(1200, 800, Color.CornflowerBlue);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(stream.ToArray()) });
    }
}
