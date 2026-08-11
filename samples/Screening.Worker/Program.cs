using NATS.Client.Core;
using Raksawi.Observability;
using Screening.Domain;
using Screening.Worker;

var builder = Host.CreateApplicationBuilder(args);

var couchDbUrl = builder.Configuration["CouchDb:Url"] ?? "http://admin:password@localhost:5984";

builder.AddRaksawiObservability(o =>
{
    // Must differ from the API. Two services sharing a name merge into one node
    // and the trace stops showing a hop.
    o.ServiceName = "screening-worker";
    o.ServiceNamespace = "kyc";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318");
    o.SamplingRatio = 1.0;
    o.ActivitySources.Add(ScreeningTelemetry.ActivitySourceName);
    o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
});

builder.Services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts
{
    Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222",
}));

builder.Services.AddHttpClient<ApplicationRepository>(client =>
    client.BaseAddress = new Uri(couchDbUrl));

// Scoped, not singleton. It depends on a typed HttpClient, and a singleton
// holding one pins a single handler forever — DNS changes stop being noticed.
// The consumer opens a scope per message, which also mirrors the per-request
// scope the API gets for free.
builder.Services.AddScoped<ScreeningService>();
builder.Services.AddHostedService<ScreeningConsumer>();

builder.Build().Run();
