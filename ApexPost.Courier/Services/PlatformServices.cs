using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Core.ViewModels;

namespace ApexPost.Courier.Services;

/// <summary>
/// Keeps the access token in MAUI SecureStorage: the Keychain on iOS and Mac, the Android Keystore, and DPAPI on
/// Windows. Never Preferences, a file or a log.
/// </summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    private const string Key = "apexpost.access_token";

    public async Task<string?> GetAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(Key);
        }
        catch (Exception)
        {
            // The stored value cannot be decrypted, for example after a restore onto a new device or a changed lock
            // screen. Forget it, so the courier signs in again instead of the app failing to start.
            SecureStorage.Default.Remove(Key);
            return null;
        }
    }

    public Task SetAsync(string token) => SecureStorage.Default.SetAsync(Key, token);

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(Key);
        return Task.CompletedTask;
    }
}

/// <summary>MAUI Connectivity, raising an event when the device goes online or offline.</summary>
public sealed class MauiConnectivityMonitor : IConnectivityMonitor, IDisposable
{
    private bool _online;

    public MauiConnectivityMonitor()
    {
        _online = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        Connectivity.Current.ConnectivityChanged += OnChanged;
    }

    public bool IsOnline => _online;

    public event EventHandler<bool>? ConnectivityChanged;

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnChanged;

    private void OnChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var online = e.NetworkAccess == NetworkAccess.Internet;
        if (online != _online)
        {
            _online = online;
            ConnectivityChanged?.Invoke(this, online);
        }
    }
}

public sealed class MainThreadDispatcher : IUiDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);
}

public sealed class ShellNavigator : INavigator
{
    public Task ShowLoginAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//login"));

    public Task ShowMainAsync() => MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync("//newscan"));
}
