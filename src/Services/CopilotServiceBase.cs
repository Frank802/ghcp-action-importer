using GitHub.Copilot.SDK;
using PipelineConverter.Utilities;

namespace PipelineConverter.Services;

/// <summary>
/// Base class for Copilot-backed pipeline services.
/// All services share a single <see cref="CopilotClient"/>; each operation creates its own
/// dedicated session configured with the service's custom agent.
/// </summary>
public abstract class CopilotServiceBase : IAsyncDisposable
{
    private readonly CopilotClient _client;
    protected readonly string _model;
    protected readonly TimeSpan _timeout;

    /// <summary>The custom agent injected into every session this service creates.</summary>
    public CustomAgentConfig? CustomAgent { get; }

    protected CopilotServiceBase(CopilotClient client, string model, TimeSpan timeout, CustomAgentConfig? customAgent = null)
    {
        _client = client;
        _model = model;
        _timeout = timeout;
        CustomAgent = customAgent;
    }

    /// <summary>
    /// Creates a new dedicated session for this service, scoped to a single pipeline operation.
    /// The session is pre-configured with the service's custom agent and a pipeline-scoped ID
    /// for traceability. An optional <paramref name="configure"/> callback allows adding
    /// service-specific settings (e.g. tools) before the session is created.
    /// </summary>
    protected Task<CopilotSession> CreateSessionAsync(
        string pipelineName,
        CancellationToken cancellationToken,
        Action<SessionConfig>? configure = null)
    {
        var config = new SessionConfig
        {
            SessionId = $"{ServicePrefix}-{SessionIdSanitizer.SanitizeSessionId(pipelineName)}-{Guid.NewGuid():N}",
            Model = _model,
            CustomAgents = CustomAgent is not null ? [CustomAgent] : null
        };
        configure?.Invoke(config);
        return _client.CreateSessionAsync(config, cancellationToken);
    }

    private string ServicePrefix =>
        GetType().Name
            .Replace("Copilot", "", StringComparison.Ordinal)
            .Replace("Service", "", StringComparison.Ordinal)
            .ToLowerInvariant();

    /// <summary>
    /// Services do not own the <see cref="CopilotClient"/>; disposal is a no-op.
    /// The client lifetime is managed by <see cref="ParallelPipelineProcessor"/>.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
