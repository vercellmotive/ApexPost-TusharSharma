using ApexPost.Courier.Offline.Domain;
using ApexPost.Specs.Support;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Domain;
using Reqnroll;
using DeviceScanType = ApexPost.Courier.Offline.Domain.ScanType;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>Steps for ScanSync.feature. State lives in the injected <see cref="ScanSyncContext"/>.</summary>
[Binding]
public sealed class ScanSyncSteps(ScanSyncContext context)
{
    [Given("shipment {string} is out for delivery")]
    public void GivenShipmentIsOutForDelivery(string trackingNumber)
    {
        var shipment = Shipment.Create(trackingNumber, "B001", Make.Recipient(), 2.5m, TestTime.Start);
        context.PickedUpAt = TestTime.Start.AddMinutes(5);
        shipment.MoveTo(ShipmentStatus.PickedUp, context.PickedUpAt);
        shipment.MoveTo(ShipmentStatus.InTransit, context.PickedUpAt.AddMinutes(5));
        shipment.MoveTo(ShipmentStatus.OutForDelivery, context.PickedUpAt.AddMinutes(10));
        context.Server.Seed(shipment);
        context.TrackingNumber = trackingNumber;
    }

    [Given("courier {string} is signed in on the device")]
    public void GivenCourierIsSignedIn(string courierId) => context.CourierId = courierId;

    [Given("the courier saved these scans while offline:")]
    public void GivenTheCourierSavedTheseScans(DataTable scans)
    {
        foreach (var row in scans.Rows)
        {
            Save(row["scan type"], row["location"], context.PickedUpAt.AddMinutes(int.Parse(row["minutes after pickup"])), signature: null);
        }
    }

    [Given("the courier saved a {string} scan at {string} while offline")]
    public void GivenTheCourierSavedAScan(string scanType, string location) =>
        Save(scanType, location, context.Clock.GetUtcNow().AddMinutes(-5), signature: scanType == "Delivered" ? "Mia Fictional" : null);

    [Given("the courier saved a {string} scan at {string} with no signature while offline")]
    public void GivenTheCourierSavedAScanWithNoSignature(string scanType, string location) =>
        Save(scanType, location, context.Clock.GetUtcNow().AddMinutes(-5), signature: null);

    [Given("the courier saved an {string} scan at {string} {int} minutes ago while offline")]
    public void GivenTheCourierSavedAScanMinutesAgo(string scanType, string location, int minutes) =>
        Save(scanType, location, context.Clock.GetUtcNow().AddMinutes(-minutes), signature: null);

    [Given("the shipment was delivered {int} minutes ago")]
    public async Task GivenTheShipmentWasDelivered(int minutes)
    {
        var shipment = await context.Server.FindAsync(context.TrackingNumber);
        shipment!.MoveTo(ShipmentStatus.Delivered, context.Clock.GetUtcNow().AddMinutes(-minutes));
    }

    [Given("the device has synced")]
    [When("the device syncs")]
    public async Task WhenTheDeviceSyncs() => context.LastSync = await context.CreateOutbox().SyncPendingAsync();

    [Given("the reply was lost, so the device still shows that scan as pending")]
    public void GivenTheReplyWasLost() => Assert.True(context.Device.Repository.UpdateSyncStatus(context.LastSaved!.Id, SyncStatus.Pending));

    [Then("{int} scans are synced on the device")]
    public void ThenScansAreSyncedOnTheDevice(int count)
    {
        Assert.Equal(count, context.LastSync?.Synced);
        Assert.Equal(count, context.Device.Repository.GetAll().Count(scan => scan.SyncStatus == SyncStatus.Synced));
        Assert.Empty(context.Device.Repository.FindPending());
    }

    [Then("the scan is synced on the device")]
    public void ThenTheScanIsSynced() => Assert.Equal(SyncStatus.Synced, context.Device.Repository.Get(context.LastSaved!.Id)?.SyncStatus);

    [Then("the scan is marked failed on the device because {string}")]
    public void ThenTheScanIsMarkedFailed(string reason)
    {
        Assert.Equal(SyncStatus.Failed, context.Device.Repository.Get(context.LastSaved!.Id)?.SyncStatus);
        Assert.Equal(reason, Assert.Single(context.LastSync!.Failures).Error);
    }

    [Then("the server holds {int} scan(s) for {string}")]
    public async Task ThenTheServerHoldsScans(int count, string trackingNumber)
    {
        var shipment = await context.Server.FindAsync(trackingNumber);
        Assert.Equal(count, (await context.Server.GetScansAsync(shipment!.Id)).Count);
    }

    private void Save(string scanType, string location, DateTimeOffset at, string? signature) =>
        context.LastSaved = context.Device.Repository.Create(ShipmentScan.CreatePending(
            context.TrackingNumber, Enum.Parse<DeviceScanType>(scanType), context.CourierId, location, at, recipientSignatureName: signature));
}
