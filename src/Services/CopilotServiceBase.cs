using GitHub.Copilot.SDK;
using PipelineConverter.Extensions;

namespace PipelineConverter.Services;

/// <summary>
/// Base class for services that use GitHub Copilot SDK.
/// Handles client lifecycle management for both standalone and session-reuse modes.
/// </summary>
public abstract class CopilotServiceBase : IAsyncDisposable
{
    protected readonly CopilotClient? _client;
    protected readonly string _model;
    protected readonly TimeSpan _timeout;
    private readonly bool _ownsClient;
    protected bool _isStarted;

    /// <summary>
    /// The custom agent configuration, if any.
    /// </summary>
    public CustomAgentConfig? CustomAgent { get; }

    /// <summary>
    /// Creates a standalone service with its own Copilot client.
    /// </summary>
    protected CopilotServiceBase(string model, int timeoutSeconds, CustomAgentConfig? customAgent = null)
    {
        _client = new CopilotClient();
        _model = model;
        _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        CustomAgent = customAgent;
        _ownsClient = true;
    }

    /// <summary>
    /// Creates a service that uses an external client (for session reuse).
    /// </summary>
    protected CopilotServiceBase(TimeSpan timeout, CustomAgentConfig? customAgent = null)
    {
        _client = null;
        _model = string.Empty;
        _timeout = timeout;
        CustomAgent = customAgent;
        _ownsClient = false;
        _isStarted = true; // External client is assumed to be started
    }

    /// <summary>
    /// Starts the Copilot client connection.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isStarted) return;
        if (_client == null) return;
        
        await _client.StartAsync(cancellationToken);
        _isStarted = true;
    }

    /// <summary>
    /// Creates a service instance with a custom agent loaded from a markdown file.
    /// </summary>
    protected static T CreateWithAgentFromFile<T>(string model, int timeoutSeconds, string agentFilePath, Func<string, int, CustomAgentConfig, T> factory)
    {
        var customAgent = CustomAgentConfigExtensions.FromMarkdownFile(agentFilePath);
        return factory(model, timeoutSeconds, customAgent);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsClient && _isStarted && _client != null)
        {
            await _client.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }
}
