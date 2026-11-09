using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Core.ViewModels;
using ApexPost.Courier.Offline.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Core.Tests;

/// <summary>A fake server: answers requests from a script and records what it received.</summary>
public sealed class ScriptedServer : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _script = new();

    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Fallback { get; set; }

    public ConcurrentQueue<(HttpMethod Method, string Path, string? Authorization, string? Body)> Received { get; } = new();

    public int Calls => Received.Count;

    public ScriptedServer Then(HttpStatusCode status, string? json = null, string mediaType = "application/json") =>
        Then(_ => Task.FromResult(Respond(status, json, mediaType)));

    /// <summary>Answers after a delay, like a slow server. The wait ends early if the client gives up.</summary>
    public ScriptedServer ThenSlow(TimeSpan delay, HttpStatusCode status, string? json = null) =>
        Then(async _ =>
        {
            await Task.Delay(delay, _cancellation.Value);
            return Respond(status, json);
        });

    /// <summary>The cancellation token of the request being answered, for slow steps.</summary>
    private readonly AsyncLocal<CancellationToken> _cancellation = new();

    public CancellationToken RequestAborted => _cancellation.Value;

    public ScriptedServer ThenThrow(Exception exception) => Then(_ => Task.FromException<HttpResponseMessage>(exception));

    public ScriptedServer Then(Func<HttpRequestMessage, Task<HttpResponseMessage>> step)
    {
        _script.Enqueue(step);
        return this;
    }

    public static HttpResponseMessage Respond(HttpStatusCode status, string? json, string mediaType = "application/json") => new(status)
    {
        Content = json is null ? new ByteArrayContent([]) : new StringContent(json, Encoding.UTF8, mediaType),
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Received.Enqueue((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
        _cancellation.Value = cancellationToken;
        if (_script.TryDequeue(out var step) || (step = Fallback) is not null)
        {
            var response = await step(request);
            response.RequestMessage = request;
            return response;
        }

        throw new InvalidOperationException($"The script has no answer for {request.Method} {request.RequestUri}.");
    }
}

public sealed class MemoryTokenStore(string? token = null) : ITokenStore
{
    public string? Token { get; private set; } = token;

    public Task<string?> GetAsync() => Task.FromResult(Token);

    public Task SetAsync(string token)
    {
        Token = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Token = null;
        return Task.CompletedTask;
    }
}

public sealed class FakeConnectivity(bool online = true) : IConnectivityMonitor
{
    public bool IsOnline { get; private set; } = online;

    public event EventHandler<bool>? ConnectivityChanged;

    public void Set(bool online)
    {
        IsOnline = online;
        ConnectivityChanged?.Invoke(this, online);
    }
}

public sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

public sealed class RecordingNavigator : INavigator
{
    public List<string> Visits { get; } = [];

    public Task ShowLoginAsync()
    {
        Visits.Add("login");
        return Task.CompletedTask;
    }

    public Task ShowMainAsync()
    {
        Visits.Add("main");
        return Task.CompletedTask;
    }
}

public sealed class LogCollector : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public string All => string.Join("\n", Lines);

    public ILogger CreateLogger(string categoryName) => new Collector(this);

    public void Dispose()
    {
    }

    private sealed class Collector(LogCollector owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue($"{logLevel}: {formatter(state, exception)}");
    }
}

/// <summary>
/// The app's real DI wiring for the API client (auth handler and resilience pipeline) in front of a scripted server,
/// with delays and timeouts shrunk so tests run fast.
/// </summary>
public sealed class ClientHarness : IDisposable
{
    public const string Token = "header.eyJjb3VyaWVySWQiOiJDUi0xMDAxIn0.signature";

    public static readonly ApexResilienceOptions FastOptions = new()
    {
        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        AttemptTimeout = TimeSpan.FromMilliseconds(300),
        TotalTimeout = TimeSpan.FromSeconds(10),
        SamplingDuration = TimeSpan.FromSeconds(5),
        BreakDuration = TimeSpan.FromMilliseconds(600),
    };

    private readonly ServiceProvider _services;

    public ClientHarness(string? token = Token, ApexResilienceOptions? options = null, HttpMessageHandler? primary = null)
    {
        Tokens = new MemoryTokenStore(token);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<ITokenStore>(Tokens);
        services.AddSingleton(Session);
        services.AddTransient<AuthHeaderHandler>();
        services.AddHttpClient<ApexApiClient>(client => client.BaseAddress = new Uri("https://api.test/"))
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .AddApexResilience(options ?? FastOptions)
            .ConfigurePrimaryHttpMessageHandler(() => primary ?? Server);
        _services = services.BuildServiceProvider();
    }

    public ScriptedServer Server { get; } = new();

    public MemoryTokenStore Tokens { get; }

    public SessionEvents Session { get; } = new();

    public LogCollector Logs { get; } = new();

    public ApexApiClient Client => _services.GetRequiredService<ApexApiClient>();

    public CircuitStateMonitor Circuit => _services.GetRequiredService<CircuitStateMonitor>();

    public void Dispose() => _services.Dispose();
}

/// <summary>A real Couchbase Lite courier database in a temporary folder.</summary>
public sealed class TempCourierDatabase : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "apexpost-core-tests", Guid.NewGuid().ToString("N"));

    public TempCourierDatabase()
    {
        Database = new CourierDatabase(new CourierDatabaseOptions { Directory = _folder });
        Repository = new CouchbaseScanRepository(Database);
    }

    public CourierDatabase Database { get; }

    public CouchbaseScanRepository Repository { get; }

    public void Dispose()
    {
        Database.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        for (var waited = 0; !condition(); waited += 20)
        {
            if (waited > timeoutMs)
            {
                throw new TimeoutException("The condition never became true.");
            }

            await Task.Delay(20);
        }
    }
}
