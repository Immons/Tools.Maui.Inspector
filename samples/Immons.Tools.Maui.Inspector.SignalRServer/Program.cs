using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;

// Test hub for the sample app's SignalR page. Kill and restart this process to watch the
// app's connection go Reconnecting → Reconnected in the inspector's SignalR view.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("SIGNALR_URLS") ?? "http://0.0.0.0:5299");
builder.Services.AddSignalR();
builder.Services.AddHostedService<Ticker>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(2));

var app = builder.Build();
app.UseWebSockets();
app.MapGet("/", () => "Inspector sample SignalR server — hub at /hubs/demo");
app.MapHub<DemoHub>("/hubs/demo");
app.Run();

/// <summary>Methods the sample app calls; "Message" and "Tick" are what it listens to.</summary>
public sealed class DemoHub : Hub
{
    public string Echo(string text) => text;

    /// <summary>Answers through a server → client call instead of a return value.</summary>
    public Task Shout(string text) => Clients.Caller.SendAsync("Message", "server: " + text.ToUpperInvariant());

    /// <summary>Returns a fake token — the inspector masks it (and the password) in what it stores.</summary>
    public object Login(LoginRequest request) => new
    {
        user = request.User,
        accessToken = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJkZW1vIn0.c2lnbmF0dXJl",
        expiresIn = 3600,
    };

    public async IAsyncEnumerable<int> Countdown(int from, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = from; i >= 0; i--)
        {
            yield return i;
            await Task.Delay(300, cancellationToken);
        }
    }

    /// <summary>Fails on purpose: a completion with an error.</summary>
    public Task Fail() => throw new HubException("This call fails on purpose.");
}

public sealed record LoginRequest(string User, string Password);

/// <summary>A server push every 10 s, so the timeline has incoming traffic of its own.</summary>
sealed class Ticker(IHubContext<DemoHub> hub) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var n = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            await hub.Clients.All.SendAsync("Tick", ++n, DateTimeOffset.Now.ToString("HH:mm:ss"), stoppingToken);
        }
    }
}
