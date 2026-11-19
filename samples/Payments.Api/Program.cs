using Npgsql;
using Raksawi.Observability;
using Raksawi.Observability.Npgsql;

// A service that is not KYC and keeps its data in Postgres: mechanism layer
// plus the Postgres integration, and nothing else (ADR-0001, ADR-0042).
var builder = WebApplication.CreateBuilder(args);

builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "payments-api";
    o.ServiceNamespace = "treasury";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318");
    o.SamplingRatio = 1.0;
});
builder.AddRaksawiNpgsql();

var dataSource = NpgsqlDataSource.Create(builder.Configuration["Postgres:ConnectionString"]
    ?? "Host=localhost;Database=postgres;Username=postgres");

var app = builder.Build();

// No table: the query needs no schema, so the sample needs no migration. The
// statement text is what the allowlist must keep out of the span.
app.MapGet("/payments/{payment}/amount", async (string payment, CancellationToken cancellationToken) =>
{
    await using var command = dataSource.CreateCommand("SELECT CAST(0 AS numeric(18,2)) WHERE @payment IS NOT NULL");
    command.Parameters.AddWithValue("payment", payment);
    var amount = await command.ExecuteScalarAsync(cancellationToken) as decimal? ?? 0m;
    return Results.Ok(new { payment, amount });
});

app.Run();
