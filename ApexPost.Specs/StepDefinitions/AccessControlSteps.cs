// Copilot-assisted: first drafted by GitHub Copilot (COPILOT-LOG.md, prompt 2), then reviewed and changed by hand.
// Kept: the Background, context injection, the request helpers and the Problem Details check. Changed: the sign-in
// steps moved to SignInSteps (the draft duplicated one and broke other features), the conditional "any access denial"
// step was rejected, and the 204, 200 and expired-token cases now assert what actually happened.
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Specs.Support;
using ApexPost.Testing.Api;
using Microsoft.EntityFrameworkCore;
using Reqnroll;

namespace ApexPost.Specs.StepDefinitions;

[Binding]
public sealed class AccessControlSteps(ApiScenarioContext context)
{
    private string _trackingNumber = string.Empty;

    [Given("shipment {string} has been registered")]
    public async Task GivenShipmentHasBeenRegistered(string trackingNumber)
    {
        _trackingNumber = trackingNumber;
        using var admin = context.Api.CreateClient(ApexPostRoles.Admin);
        using var created = await admin.PostAsJsonAsync("/api/v2/shipments", new
        {
            trackingNumber,
            originBranchCode = "B001",
            recipientName = "Mia Fictional",
            recipientPhone = "+61 2 5550 1234",
            recipientAddress = "12 Example Lane",
            city = "Sydney",
            weightKg = 2.5m,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    [Given("a partner system that sends no key")]
    public void GivenAPartnerWithNoKey() => UsePartnerKey(null);

    [Given("a partner system that sends a key we never gave")]
    public void GivenAPartnerWithAWrongKey() => UsePartnerKey("not-a-key-we-issued-0123456789abcdef");

    [Given("a partner system that sends the configured key")]
    public void GivenAPartnerWithTheConfiguredKey() => UsePartnerKey(ApiFactory.PartnerKey);

    [When("I look up the shipment")]
    public Task WhenILookUpTheShipment() => SendAsync(new HttpRequestMessage(HttpMethod.Get, Shipment));

    [When("I remove the shipment")]
    public Task WhenIRemoveTheShipment() => SendAsync(new HttpRequestMessage(HttpMethod.Delete, Shipment));

    [When("I try to change the status of the shipment")]
    public Task WhenITryToChangeTheStatus() => SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{Shipment}/status")
    {
        Content = JsonContent.Create(new { status = "PickedUp" }),
    });

    [When("I try to upload a scan for the shipment")]
    public Task WhenITryToUploadAScan() => UploadScanAsync("CR-1001");

    [When("I upload a scan recorded by courier {string}")]
    public Task WhenIUploadAScanRecordedBy(string courierId) => UploadScanAsync(courierId);

    [When("it looks up the shipment through the partner route")]
    public Task WhenThePartnerLooksItUp() =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/v2/partner/shipments/{_trackingNumber}"));

    [Then("I see shipment {string}")]
    public async Task ThenISeeShipment(string trackingNumber)
    {
        Assert.Equal(HttpStatusCode.OK, context.LastResponse!.StatusCode);
        var body = await context.LastResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(trackingNumber, body.GetProperty("trackingNumber").GetString());
    }

    [Then("I am refused with status {int}")]
    public void ThenIAmRefusedWithStatus(int status) => Assert.Equal((HttpStatusCode)status, context.LastResponse!.StatusCode);

    [Then("the refusal is explained as Problem Details")]
    public async Task ThenTheRefusalIsProblemDetails()
    {
        Assert.Equal("application/problem+json", context.LastResponse!.Content.Headers.ContentType?.MediaType);
        using var body = await JsonDocument.ParseAsync(await context.LastResponse.Content.ReadAsStreamAsync());
        Assert.Equal((int)context.LastResponse.StatusCode, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("title").GetString()));
    }

    [Then("the shipment is removed")]
    public async Task ThenTheShipmentIsRemoved()
    {
        Assert.Equal(HttpStatusCode.NoContent, context.LastResponse!.StatusCode);
        Assert.Equal(0, await CountShipmentsAsync());
    }

    [Then("the shipment still exists")]
    public async Task ThenTheShipmentStillExists() => Assert.Equal(1, await CountShipmentsAsync());

    [Then("no scan is stored")]
    public async Task ThenNoScanIsStored()
    {
        await using var db = context.Api.Database.CreateContext();
        Assert.Equal(0, await db.Scans.CountAsync());
    }

    [Then("it sees the status of shipment {string}")]
    public async Task ThenThePartnerSeesTheStatus(string trackingNumber)
    {
        Assert.Equal(HttpStatusCode.OK, context.LastResponse!.StatusCode);
        var body = await context.LastResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(trackingNumber, body.GetProperty("trackingNumber").GetString());
        Assert.Equal("Created", body.GetProperty("status").GetString());
    }

    [Then("it does not see the recipient's details")]
    public async Task ThenThePartnerDoesNotSeeTheRecipient()
    {
        var text = await context.LastResponse!.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Mia Fictional", text);
        Assert.DoesNotContain("5550 1234", text);
        Assert.DoesNotContain("12 Example Lane", text);
    }

    private string Shipment => $"/api/v2/shipments/{_trackingNumber}";

    private void UsePartnerKey(string? key)
    {
        context.Client?.Dispose();
        context.Client = context.Api.CreateDefaultClient();
        if (key is not null)
        {
            context.Client.DefaultRequestHeaders.Add("X-Api-Key", key);
        }
    }

    private Task UploadScanAsync(string courierId) => SendAsync(new HttpRequestMessage(HttpMethod.Post, $"{Shipment}/scans")
    {
        Content = JsonContent.Create(new
        {
            scans = new[]
            {
                new
                {
                    scanId = $"scan::{Guid.NewGuid():D}",
                    scanType = "Pickup",
                    courierId,
                    location = "Sydney depot",
                    scannedAtUtc = context.Api.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
                },
            },
        }),
    });

    private async Task<int> CountShipmentsAsync()
    {
        await using var db = context.Api.Database.CreateContext();
        return await db.Shipments.CountAsync(shipment => shipment.TrackingNumber == _trackingNumber);
    }

    private async Task SendAsync(HttpRequestMessage request)
    {
        context.LastResponse?.Dispose();
        using (request)
        {
            context.LastResponse = await context.Client.SendAsync(request);
        }
    }
}
