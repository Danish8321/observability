using Microsoft.Data.SqlClient;
using Raksawi.Observability;
using Raksawi.Observability.SqlClient;

// A service that is not KYC and keeps its data in SQL Server: mechanism layer
// plus the SQL Server integration, and nothing else (ADR-0001, ADR-0042).
var builder = WebApplication.CreateBuilder(args);

builder.AddRaksawiObservability(o =>
{
    o.ServiceName = "ledger-api";
    o.ServiceNamespace = "treasury";
    o.OtlpEndpoint = new Uri(builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4318");
    o.SamplingRatio = 1.0;
});
builder.AddRaksawiSqlClient();

var connectionString = builder.Configuration["Sql:ConnectionString"]
    ?? "Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true";

var app = builder.Build();

// No table: the query needs no schema, so the sample needs no migration. The
// statement text is what the allowlist must keep out of the span.
app.MapGet("/balances/{account}", async (string account, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    // Not a KYC service, so its log fields are filtered by the denylist
    // (ADR-0043): AccountId arrives with nothing declared; Email, an
    // estate-denylisted name, is dropped in-process; StatementRef is dropped
    // if this service lists it in Observability:Logs:DeniedKeys; and Contact
    // is kept with its email-shaped value redacted by the collector.
    logger.LogInformation(
        "Balance read for {AccountId}; statement to {Email}, copy to {Contact}, ref {StatementRef}",
        account, "statements@example.test", "copies@example.test", "stmt-ref-1");

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var command = new SqlCommand("SELECT CAST(0 AS decimal(18,2)) AS balance WHERE @account IS NOT NULL", connection);
    command.Parameters.AddWithValue("@account", account);
    var balance = await command.ExecuteScalarAsync(cancellationToken) as decimal? ?? 0m;
    return Results.Ok(new { account, balance });
});

app.Run();
