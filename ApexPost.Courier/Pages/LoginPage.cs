using ApexPost.Courier.Core.ViewModels;

namespace ApexPost.Courier.Pages;

/// <summary>Sign in with a development token: ask the API for one, or paste one from dotnet user-jwts or /dev/token.</summary>
public sealed class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        Title = "Sign in";
        BindingContext = viewModel;

        var pasted = new Editor { Placeholder = "Or paste an access token", HeightRequest = 120 };
        pasted.SetBinding(Editor.TextProperty, static (LoginViewModel vm) => vm.PastedToken, BindingMode.TwoWay);

        var message = new Label { TextColor = Colors.Firebrick };
        message.SetBinding(Label.TextProperty, static (LoginViewModel vm) => vm.Message);

        var form = new VerticalStackLayout
        {
            Padding = 24,
            Spacing = 12,
            Children =
            {
                new Label { Text = "ApexPost Courier", FontSize = 28, FontAttributes = FontAttributes.Bold },
                new Label { Text = "Paste an access token. A real identity provider replaces this; see SECURITY.md." },
            },
        };

#if DEBUG
        // Debug builds only: ask the local API for a development token. A Release build has no such button.
        var courierId = new Entry { Placeholder = "Courier id, such as CR-1001" };
        courierId.SetBinding(Entry.TextProperty, static (LoginViewModel vm) => vm.CourierId, BindingMode.TwoWay);
        form.Children.Add(courierId);
        form.Children.Add(new Button { Text = "Get development token (Debug build)", Command = viewModel.RequestTokenCommand });
#endif

        form.Children.Add(pasted);
        form.Children.Add(new Button { Text = "Use pasted token", Command = viewModel.UsePastedTokenCommand });
        form.Children.Add(message);
        Content = new ScrollView { Content = form };
    }
}
