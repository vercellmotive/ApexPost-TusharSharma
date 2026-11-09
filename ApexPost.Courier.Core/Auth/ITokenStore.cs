namespace ApexPost.Courier.Core.Auth;

/// <summary>
/// Where the access token lives. The app's implementation uses MAUI SecureStorage (the Keychain on iOS and Mac,
/// the Keystore on Android, DPAPI on Windows), never Preferences, files or logs.
/// </summary>
public interface ITokenStore
{
    Task<string?> GetAsync();

    Task SetAsync(string token);

    Task ClearAsync();
}

/// <summary>Raised when the server refuses the token, so the app can go back to the Login page.</summary>
public sealed class SessionEvents
{
    public event EventHandler? SignedOut;

    public void RaiseSignedOut() => SignedOut?.Invoke(this, EventArgs.Empty);
}
