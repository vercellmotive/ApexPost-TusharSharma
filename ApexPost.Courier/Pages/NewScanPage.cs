using ApexPost.Courier.Core.ViewModels;
using ApexPost.Courier.Offline.Domain;

namespace ApexPost.Courier.Pages;

/// <summary>Record a scan. It is saved on the device as Pending first, online or not.</summary>
public sealed class NewScanPage : ContentPage
{
    public NewScanPage(NewScanViewModel viewModel, SyncStatusViewModel status)
    {
        Title = "New Scan";
        BindingContext = viewModel;

        var tracking = new Entry { Placeholder = "Tracking number, APX-12345678" };
        tracking.SetBinding(Entry.TextProperty, static (NewScanViewModel vm) => vm.TrackingNumber, BindingMode.TwoWay);

        var type = new Picker { Title = "Scan type", ItemsSource = NewScanViewModel.ScanTypes.ToList() };
        type.SetBinding(Picker.SelectedItemProperty, static (NewScanViewModel vm) => vm.ScanType, BindingMode.TwoWay);

        var location = new Entry { Placeholder = "Location" };
        location.SetBinding(Entry.TextProperty, static (NewScanViewModel vm) => vm.Location, BindingMode.TwoWay);

        var notes = new Entry { Placeholder = "Notes (optional)" };
        notes.SetBinding(Entry.TextProperty, static (NewScanViewModel vm) => vm.Notes, BindingMode.TwoWay);

        var signature = new Entry { Placeholder = "Signed by" };
        signature.SetBinding(Entry.TextProperty, static (NewScanViewModel vm) => vm.SignatureName, BindingMode.TwoWay);
        signature.SetBinding(IsVisibleProperty, static (NewScanViewModel vm) => vm.NeedsSignature);

        var message = new Label();
        message.SetBinding(Label.TextProperty, static (NewScanViewModel vm) => vm.Message);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 12,
                Children =
                {
                    new OfflineBanner(status),
                    tracking,
                    type,
                    location,
                    notes,
                    signature,
                    new Button { Text = "Save scan", Command = viewModel.SaveCommand },
                    message,
                },
            },
        };
    }
}
