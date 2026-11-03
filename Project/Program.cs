using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using WeatherImages;

new HostBuilder().ConfigureFunctionsWorkerDefaults().ConfigureServices(services =>
{
    services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(45) });
    services.AddSingleton<Pipeline>();
}).Build().Run();
