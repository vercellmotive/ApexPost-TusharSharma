using ApexPost.Courier.Core.ViewModels;

namespace ApexPost.Courier.Pages;

/// <summary>"Server unavailable – working offline", shown on every tab while the circuit is open or there is no network.</summary>
public sealed class OfflineBanner : Border
{
    public OfflineBanner(SyncStatusViewModel status)
    {
        BindingContext = status;
        BackgroundColor = Colors.DarkOrange;
        Padding = new Thickness(12, 8);
        StrokeThickness = 0;
        Content = new Label { TextColor = Colors.White, FontAttributes = FontAttributes.Bold, Text = status.OfflineBanner };
        this.SetBinding(IsVisibleProperty, static (SyncStatusViewModel vm) => vm.ShowOfflineBanner);
    }
}
