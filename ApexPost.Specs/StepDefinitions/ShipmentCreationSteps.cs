using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Domain.ValueObjects;
using ApexPost.Specs.Support;
using ApexPost.Testing.Tracking;
using Microsoft.EntityFrameworkCore;
using Reqnroll;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>Steps for ShipmentCreation.feature. State lives in the injected <see cref="ApiScenarioContext"/>.</summary>
[Binding]
public sealed class ShipmentCreationSteps(ApiScenarioContext context)
{
    private const string Shipments = "/api/v2/shipments";

    [Given("the parcel is handed in at branch {string}, which is open")]
    public async Task GivenTheParcelIsHandedInAtAnOpenBranch(string branchCode)
    {
        var branch = await new FakeBranches().GetByCodeAsync(new BranchCode(branchCode));
        Assert.True(branch?.IsActive, $"The test data must have branch {branchCode} open.");
        context.OriginBranch = branchCode;
    }

    [Given("shipment {string} is already registered")]
    public async Task GivenShipmentIsAlreadyRegistered(string trackingNumber)
    {
        using var response = await RegisterAsync(trackingNumber, 2m, "Mia Fictional", "Sydney");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [When(@"^I register shipment ""(APX-[0-9]+)"" weighing ([0-9.]+) kg for ""([^""]+)"" in ""([^""]+)""$")]
    public async Task WhenIRegisterShipment(string trackingNumber, string weight, string recipient, string city) =>
        context.LastResponse = await RegisterAsync(trackingNumber, decimal.Parse(weight, CultureInfo.InvariantCulture), recipient, city);

    [Then("the shipment is registered")]
    public void ThenTheShipmentIsRegistered()
    {
        Assert.Equal(HttpStatusCode.Created, context.LastResponse?.StatusCode);
        Assert.NotNull(context.LastResponse?.Headers.Location);
    }

    [Then("its status is {string}")]
    public async Task ThenItsStatusIs(string status)
    {
        var body = await context.LastResponse!.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(status, body.GetProperty("status").GetString());
    }

    [Then("it can be found at the address the API returned")]
    public async Task ThenItCanBeFoundAtTheReturnedAddress()
    {
        using var found = await context.Client.GetAsync(context.LastResponse!.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    [Then("the shipment is refused because the tracking number is taken")]
    public async Task ThenTheShipmentIsRefusedAsADuplicate()
    {
        Assert.Equal(HttpStatusCode.Conflict, context.LastResponse?.StatusCode);
        var problem = await context.LastResponse!.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Duplicate tracking number", problem.GetProperty("title").GetString());
    }

    [Then("there is still only one shipment {string}")]
    public async Task ThenThereIsStillOnlyOneShipment(string trackingNumber)
    {
        await using var db = context.Api.Database.CreateContext();
        Assert.Equal(1, await db.Shipments.CountAsync(shipment => shipment.TrackingNumber == trackingNumber));
    }

    [Then("the shipment is refused for its weight")]
    public async Task ThenTheShipmentIsRefusedForItsWeight()
    {
        Assert.Equal(HttpStatusCode.BadRequest, context.LastResponse?.StatusCode);
        var problem = await context.LastResponse!.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("weightKg", out _), "The 400 must name the weightKg field.");
    }

    private Task<HttpResponseMessage> RegisterAsync(string trackingNumber, decimal weightKg, string recipient, string city) =>
        context.Client.PostAsJsonAsync(Shipments, new
        {
            trackingNumber,
            originBranchCode = context.OriginBranch,
            recipientName = recipient,
            recipientPhone = "+61 2 5550 1234",
            recipientAddress = "12 Example Lane",
            city,
            weightKg,
        });
}
