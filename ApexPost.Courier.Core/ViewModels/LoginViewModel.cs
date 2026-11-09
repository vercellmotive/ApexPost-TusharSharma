using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;

namespace ApexPost.Courier.Core.ViewModels;

/// <summary>Sign in with a development token: request one for a courier id, or paste one.</summary>
public sealed class LoginViewModel : ObservableObject
{
    private readonly ITokenStore _tokens;
    private readonly ApexApiClient _api;
    private readonly INavigator _navigator;
    private string _courierId = "CR-1001";
    private string _pastedToken = string.Empty;
    private string? _message;

    public LoginViewModel(ITokenStore tokens, ApexApiClient api, INavigator navigator)
    {
        _tokens = tokens;
        _api = api;
        _navigator = navigator;
        RequestTokenCommand = new AsyncCommand(RequestTokenAsync);
        UsePastedTokenCommand = new AsyncCommand(UsePastedTokenAsync);
    }

    public string CourierId { get => _courierId; set => Set(ref _courierId, value); }

    public string PastedToken { get => _pastedToken; set => Set(ref _pastedToken, value); }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public AsyncCommand RequestTokenCommand { get; }

    public AsyncCommand UsePastedTokenCommand { get; }

    public async Task RequestTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(CourierId))
        {
            Message = "Enter your courier id.";
            return;
        }

        var result = await _api.RequestDevelopmentTokenAsync(CourierId.Trim());
        if (!result.Succeeded)
        {
            Message = result.Message;
            return;
        }

        await SignInAsync(result.Value.AccessToken);
    }

    public async Task UsePastedTokenAsync()
    {
        var token = PastedToken.Trim();
        if (CourierIdentity.CourierIdFrom(token) is null)
        {
            Message = "That is not a courier token. It needs a courierId claim.";
            return;
        }

        await SignInAsync(token);
    }

    private async Task SignInAsync(string token)
    {
        await _tokens.SetAsync(token);
        PastedToken = string.Empty;
        Message = null;
        await _navigator.ShowMainAsync();
    }
}
