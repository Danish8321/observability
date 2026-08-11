using Microsoft.AspNetCore.Mvc;
using NATS.Client.Core;
using Raksawi.Observability;
using Raksawi.Observability.Kyc;
using Screening.Api;
using Screening.Domain;

var builder = WebApplication.CreateBuilder(args);

var couchDbUrl = builder.Configuration["CouchDb:Url"] ?? "http://admin:password@localhost:5984";

// ---------------------------------------------------------------------------
// Telemetry. One call, and it is the only telemetry setup in this service.
// ---------------------------------------------------------------------------
builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "screening-api";        // bare, kebab-case (ADR-0006)
    o.ServiceNamespace = "kyc";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318");
    o.SamplingRatio = 1.0;                  // required outside Development (ADR-0010)

    // Without this the service's own spans are dropped silently.
    o.ActivitySources.Add(ScreeningTelemetry.ActivitySourceName);

    // Without this, CouchDB document identifiers reach the store in url.full.
    // It is an exact host match and it fails open (ADR-0023).
    o.CouchDbHosts.Add(new Uri(couchDbUrl).Host);
});

builder.Services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts
{
    Url = builder.Configuration["Nats:Url"] ?? "nats://localhost:4222",
}));

builder.Services.AddHttpClient<ApplicationRepository>(client =>
{
    // HttpClient ignores userinfo embedded in a URI (RFC 3986 §3.2.1 is not
    // honoured), so credentials in couchDbUrl need an explicit header.
    var couchDbUri = new Uri(couchDbUrl);
    client.BaseAddress = new Uri(couchDbUri.GetLeftPart(UriPartial.Authority));
    if (!string.IsNullOrEmpty(couchDbUri.UserInfo))
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(couchDbUri.UserInfo)));
    }
});

builder.Services.AddSingleton<ApplicationPublisher>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseCorrelation();

// ---------------------------------------------------------------------------
// Submit an application.
//
// Returns 202 and hands off asynchronously. That shape is deliberate: it is
// exactly the case where a caller sees success and the work silently never
// completes, which is the failure this whole platform exists to make visible.
// ---------------------------------------------------------------------------
app.MapPost("/applications", async (
    ApplicationRequest request,
    HttpContext context,
    ApplicationRepository repository,
    ApplicationPublisher publisher,
    CancellationToken cancellationToken) =>
{
    // Set on the ASP.NET Core server span, so a search by application finds the
    // inbound request and not only the internal work.
    KycTelemetry.SetApplicationId(request.ApplicationId);

    var correlationId = context.CorrelationId();

    var existing = await repository.GetAsync(request.ApplicationId, cancellationToken);
    if (existing is not null)
    {
        return Results.Conflict(new { message = "Application already exists" });
    }

    var document = new ApplicationDocument
    {
        Status = ApplicationStatus.Received,
        Applicant = request.Applicant,     // 🔒 Class 3 — stored, never emitted
        CorrelationId = correlationId,
    };

    if (!await repository.SaveAsync(request.ApplicationId, document, cancellationToken))
    {
        return Results.Conflict(new { message = "Concurrent write, retry" });
    }

    await publisher.PublishAsync(request.ApplicationId, correlationId, cancellationToken);

    return Results.Accepted(
        $"/applications/{request.ApplicationId}",
        new { request.ApplicationId, status = ApplicationStatus.Received, correlationId });
});

// Read back. In the demo this is what proves the work never completed.
app.MapGet("/applications/{id}", async (
    string id,
    ApplicationRepository repository,
    CancellationToken cancellationToken) =>
{
    KycTelemetry.SetApplicationId(id);

    var document = await repository.GetAsync(id, cancellationToken);

    return document is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            applicationId = id,
            document.Status,
            document.Outcome,
            document.CorrelationId,
            // 🔒 Applicant deliberately not returned. Diagnostic reads should
            // not be a route to personal data.
        });
});

// Liveness. Excluded from tracing at the collector rather than here — filtering
// in every service is how services drift apart.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Exposed so the API can be exercised by integration tests.</summary>
public partial class Program;
