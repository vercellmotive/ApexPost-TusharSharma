namespace ApexPost.Courier.Core.Sync;

/// <summary>Whether the device has a network. The app's implementation wraps MAUI Connectivity.</summary>
public interface IConnectivityMonitor
{
    bool IsOnline { get; }

    /// <summary>Raised with the new state when the device goes online or offline.</summary>
    event EventHandler<bool>? ConnectivityChanged;
}
