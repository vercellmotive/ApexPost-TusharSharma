using ApexPost.Courier.Core.ViewModels;

namespace ApexPost.Courier.Pages;

/// <summary>The scans still waiting to upload, with the live count from the Part A change listener.</summary>
public sealed class PendingScansPage : ContentPage
{
    public PendingScansPage(PendingScansViewModel viewModel, SyncStatusViewModel status)
    {
        Title = "Pending";
        BindingContext = viewModel;

        var count = new Label { FontSize = 22, FontAttributes = FontAttributes.Bold };
        count.SetBinding(Label.TextProperty, static (PendingScansViewModel vm) => vm.Title);

        var list = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var tracking = new Label { FontAttributes = FontAttributes.Bold };
                tracking.SetBinding(Label.TextProperty, static (PendingScanItem item) => item.TrackingNumber);
                var detail = new Label { FontSize = 13 };
                detail.SetBinding(Label.TextProperty, static (PendingScanItem item) => item.Detail);
                return new VerticalStackLayout { Padding = new Thickness(0, 6), Children = { tracking, detail } };
            }),
            EmptyView = new Label { Text = "Nothing waiting. Every scan has been uploaded." },
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, static (PendingScansViewModel vm) => vm.Scans);

        Content = new Grid
        {
            Padding = 24,
            RowSpacing = 12,
            RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star)],
            Children = { new OfflineBanner(status), count.Row(1), list.Row(2) },
        };
    }
}

internal static class GridExtensions
{
    public static T Row<T>(this T view, int row)
        where T : BindableObject
    {
        Grid.SetRow(view, row);
        return view;
    }
}
