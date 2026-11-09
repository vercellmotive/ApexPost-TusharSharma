using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Core.ViewModels;

namespace ApexPost.Courier;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, SessionEvents session, INavigator navigator)
    {
        InitializeComponent();
        _services = services;

        // A 401 from the API clears the token (AuthHeaderHandler); then the courier signs in again.
        session.SignedOut += async (_, _) => await navigator.ShowLoginAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(_services));
        window.Created += async (_, _) => await StartAsync();
        return window;
    }

    private async Task StartAsync()
    {
        var token = await _services.GetRequiredService<ITokenStore>().GetAsync();
        if (CourierIdentity.CourierIdFrom(token) is null)
        {
            await _services.GetRequiredService<INavigator>().ShowLoginAsync();
            return;
        }

        await _services.GetRequiredService<INavigator>().ShowMainAsync();

        // Anything saved while the app was closed goes up now, if there is a network.
        await _services.GetRequiredService<ScanSyncService>().SyncPendingAsync();
    }
}
