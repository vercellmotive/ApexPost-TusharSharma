using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.ViewModels;

namespace ApexPost.Courier.Pages;

/// <summary>The last sync, the circuit breaker state and the scans the server refused.</summary>
public sealed class SyncStatusPage : ContentPage
{
    public SyncStatusPage(SyncStatusViewModel viewModel, ITokenStore tokens, INavigator navigator)
    {
        Title = "Sync Status";
        BindingContext = viewModel;

        var lastSync = new Label();
        lastSync.SetBinding(Label.TextProperty, static (SyncStatusViewModel vm) => vm.LastSyncText);
        var result = new Label();
        result.SetBinding(Label.TextProperty, static (SyncStatusViewModel vm) => vm.LastResultText);
        var circuit = new Label();
        circuit.SetBinding(Label.TextProperty, static (SyncStatusViewModel vm) => vm.CircuitText);

        var failures = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var line = new Label { FontSize = 13, TextColor = Colors.Firebrick };
                line.SetBinding(Label.TextProperty, static (string text) => text);
                return line;
            }),
            EmptyView = new Label { Text = "No failures." },
        };
        failures.SetBinding(ItemsView.ItemsSourceProperty, static (SyncStatusViewModel vm) => vm.Failures);

        var signOut = new Button { Text = "Sign out" };
        signOut.Clicked += async (_, _) =>
        {
            await tokens.ClearAsync();
            await navigator.ShowLoginAsync();
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 10,
                Children =
                {
                    new OfflineBanner(viewModel),
                    new Label { Text = "Last sync", FontAttributes = FontAttributes.Bold },
                    lastSync,
                    result,
                    new Label { Text = "Circuit breaker", FontAttributes = FontAttributes.Bold },
                    circuit,
                    new Button { Text = "Sync now", Command = viewModel.SyncNowCommand },
                    new Label { Text = "Failures", FontAttributes = FontAttributes.Bold },
                    failures,
                    signOut,
                },
            },
        };
    }
}
