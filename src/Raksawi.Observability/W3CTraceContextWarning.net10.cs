#if NET10_0_OR_GREATER
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Raksawi.Observability;

/// <summary>
/// Writes the ADR-0005 trace-context correction warning once, at host start.
/// </summary>
/// <remarks>
/// <para>
/// Registered only when the correction actually happened, so its presence in
/// the service collection is itself the condition. It exists because
/// <c>AddRaksawiObservability</c> runs before the host is built and therefore
/// has no logger to write to.
/// </para>
/// <para>
/// A warning and never a throw: telemetry setup must not be able to fail a
/// service start (Rev 3 I3.6).
/// </para>
/// </remarks>
internal sealed class W3CTraceContextWarning : IHostedService
{
    private readonly ILogger<W3CTraceContextWarning> _logger;

    public W3CTraceContextWarning(ILogger<W3CTraceContextWarning> logger) => _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Constant template, no interpolation (ADR-0004).
        _logger.LogWarning(ServiceIdentity.W3CCorrectedMessage);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
#endif
