using Immons.Tools.Maui.Inspector;
using Microsoft.AspNetCore.SignalR.Client;

namespace SampleApp.SignalRDemo;

/// <summary>
/// Talks to samples/Immons.Tools.Maui.Inspector.SignalRServer so the web panel's SignalR view has
/// something to show: calls in both directions, a stream, a failing call, a masked password and
/// token, and — when the server is killed and restarted — the reconnect cycle.
/// </summary>
public sealed class SignalRDemoPage : ContentPage
{
    readonly Label _status = new() { AutomationId = "SignalRStatus", FontAttributes = FontAttributes.Bold };
    readonly Label _log = new() { AutomationId = "SignalRLog", FontSize = 12, TextColor = Colors.Gray };
    readonly List<string> _lines = [];
    HubConnection? _connection;

    public SignalRDemoPage()
    {
        Title = "SignalR demo";
        var url = Environment.GetEnvironmentVariable("HV_SIGNALR_URL")
                  ?? (DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5299" : "http://localhost:5299");

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 12),
                Spacing = 10,
                Children =
                {
                    new Label { Text = url + "/hubs/demo", FontSize = 12, TextColor = Colors.Gray },
                    _status,
                    Row(Button("Connect", "SignalRConnect", () => Connect(url)), Button("Disconnect", "SignalRDisconnect", Disconnect)),
                    Row(Button("Echo", "SignalREcho", Echo), Button("Shout (server → client)", "SignalRShout", Shout)),
                    Row(Button("Login (masked)", "SignalRLogin", Login), Button("Countdown stream", "SignalRCountdown", Countdown)),
                    Button("Failing call", "SignalRFail", Fail),
                    _log,
                },
            },
        };
        UpdateStatus();
    }

    protected override async void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // Popping the page stops the hub — the panel shows it as Closed.
        if (!Navigation.NavigationStack.Contains(this))
            await Disconnect();
    }

    async Task Connect(string url)
    {
        if (_connection != null)
            return;

        _connection = new HubConnectionBuilder()
            .WithUrl(url + "/hubs/demo", options =>
            {
#if DEBUG
                // negotiate (and long polling, if used) in the Network view
                options.HttpMessageHandlerFactory = inner => new MauiInspectorHttpHandler(inner);
#endif
            })
            .WithAutomaticReconnect(new KeepTrying())
#if DEBUG
            .WithMauiInspector("demo")   // ← the SignalR view; inert without UseMauiInspector
#endif
            .Build();

        _connection.On<string>("Message", text => Log("↓ Message: " + text));
        _connection.On<int, string>("Tick", (n, time) => Log($"↓ Tick #{n} at {time}"));
        _connection.Reconnecting += _ => { UpdateStatus(); return Task.CompletedTask; };
        _connection.Reconnected += _ => { UpdateStatus(); return Task.CompletedTask; };
        _connection.Closed += _ => { UpdateStatus(); return Task.CompletedTask; };

        try
        {
            UpdateStatus("Connecting…");
            await _connection.StartAsync();
            Log("connected: " + _connection.ConnectionId);
        }
        catch (Exception ex)
        {
            Log("start failed: " + ex.Message);
            await _connection.DisposeAsync();
            _connection = null;
        }
        UpdateStatus();
    }

    async Task Disconnect()
    {
        if (_connection is not { } connection)
            return;
        _connection = null;
        await connection.DisposeAsync();
        Log("disconnected");
        UpdateStatus();
    }

    Task Echo() => Call(async c => Log("Echo → " + await c.InvokeAsync<string>("Echo", "Hello at " + DateTime.Now.ToString("HH:mm:ss"))));

    Task Shout() => Call(c => c.InvokeAsync("Shout", "hello from the app"));

    Task Login() => Call(async c =>
    {
        var result = await c.InvokeAsync<LoginResult>("Login", new { user = "ann", password = "correct horse battery staple" });
        Log($"Login → token {result.AccessToken?[..12]}… (masked in the inspector)");
    });

    Task Countdown() => Call(async c =>
    {
        var items = new List<int>();
        await foreach (var i in c.StreamAsync<int>("Countdown", 5))
            items.Add(i);
        Log("Countdown → " + string.Join(", ", items));
    });

    Task Fail() => Call(c => c.InvokeAsync("Fail"));

    async Task Call(Func<HubConnection, Task> action)
    {
        if (_connection is not { State: HubConnectionState.Connected } connection)
        {
            Log("not connected");
            return;
        }
        try
        {
            await action(connection);
        }
        catch (Exception ex)
        {
            Log("error: " + ex.Message);
        }
    }

    void UpdateStatus(string? text = null) => MainThread.BeginInvokeOnMainThread(() =>
        _status.Text = text ?? "State: " + (_connection?.State.ToString() ?? "Disconnected"));

    void Log(string line) => MainThread.BeginInvokeOnMainThread(() =>
    {
        _lines.Insert(0, DateTime.Now.ToString("HH:mm:ss  ") + line);
        if (_lines.Count > 30)
            _lines.RemoveAt(_lines.Count - 1);
        _log.Text = string.Join(Environment.NewLine, _lines);
    });

    static Button Button(string text, string automationId, Func<Task> action)
    {
        var button = new Button { Text = text, AutomationId = automationId, HorizontalOptions = LayoutOptions.Fill };
        button.Clicked += async (_, _) => await action();
        return button;
    }

    static Grid Row(View left, View right)
    {
        var grid = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        grid.Add(left, 0);
        grid.Add(right, 1);
        return grid;
    }

    sealed class LoginResult
    {
        public string? User { get; set; }

        public string? AccessToken { get; set; }
    }

    /// <summary>Retries forever: 0 s, 2 s, then every 5 s.</summary>
    sealed class KeepTrying : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => retryContext.PreviousRetryCount switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(5),
        };
    }
}
