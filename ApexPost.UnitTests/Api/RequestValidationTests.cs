using System.ComponentModel.DataAnnotations;
using ApexPost.Api.Contracts;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.UnitTests.Api;

/// <summary>
/// R7: the request DTO rules, run through the same DataAnnotations validator that [ApiController] uses, without an
/// HTTP server. The API tests check that a failure becomes a 400 ValidationProblemDetails; these check each rule and
/// its boundaries.
/// </summary>
public sealed class RequestValidationTests
{
    private readonly FakeTimeProvider _clock = TestTime.NewClock();

    private static CreateShipmentRequest ValidShipment(
        string? trackingNumber = "APX-12345678",
        decimal? weightKg = 2.5m,
        string? phone = "+61 2 5550 1234",
        string? name = "Mia Fictional",
        string? address = "12 Example Lane",
        string? city = "Sydney") => new()
        {
            TrackingNumber = trackingNumber,
            OriginBranchCode = "B001",
            RecipientName = name,
            RecipientPhone = phone,
            RecipientAddress = address,
            City = city,
            WeightKg = weightKg,
        };

    private ScanUploadItem ValidScan(DateTimeOffset? at = null, ScanType type = ScanType.Pickup, string? signature = null) => new()
    {
        ScanId = Make.NewScanId(),
        ScanType = type,
        CourierId = Make.CourierId,
        Location = "Sydney depot",
        ScannedAtUtc = at ?? _clock.GetUtcNow(),
        RecipientSignatureName = signature,
    };

    // The validator gets the fake clock through the service provider, exactly as MVC passes the request services.
    private List<ValidationResult> Validate(object instance)
    {
        using var services = new ServiceCollection().AddSingleton<TimeProvider>(_clock).BuildServiceProvider();
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance, services, items: null), results, validateAllProperties: true);
        return results;
    }

    private static IEnumerable<string> Members(IEnumerable<ValidationResult> results) => results.SelectMany(result => result.MemberNames);

    [Fact]
    public void CreateShipmentRequest_AllFieldsValid_HasNoErrors()
    {
        // Arrange
        var request = ValidShipment();

        // Act
        var results = Validate(request);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("0.1", true)]
    [InlineData("70", true)]
    [InlineData("0.09", false)]
    [InlineData("70.01", false)]
    [InlineData("0", false)]
    public void CreateShipmentRequest_WeightBoundary_ValidOnlyFrom0Point1To70(string weight, bool expectedValid)
    {
        // Arrange
        var request = ValidShipment(weightKg: decimal.Parse(weight, System.Globalization.CultureInfo.InvariantCulture));

        // Act
        var results = Validate(request);

        // Assert
        Assert.Equal(expectedValid, !Members(results).Contains(nameof(CreateShipmentRequest.WeightKg)));
    }

    [Theory]
    [InlineData("APX-12345678", true)]
    [InlineData("APX-1234567", false)]
    [InlineData("APX-123456789", false)]
    [InlineData("XYZ-12345678", false)]
    [InlineData(null, false)]
    public void CreateShipmentRequest_TrackingNumber_MustMatchTheFormat(string? trackingNumber, bool expectedValid)
    {
        // Arrange
        var request = ValidShipment(trackingNumber: trackingNumber);

        // Act
        var results = Validate(request);

        // Assert
        Assert.Equal(expectedValid, !Members(results).Contains(nameof(CreateShipmentRequest.TrackingNumber)));
    }

    [Theory]
    [InlineData(nameof(CreateShipmentRequest.RecipientName))]
    [InlineData(nameof(CreateShipmentRequest.RecipientPhone))]
    [InlineData(nameof(CreateShipmentRequest.RecipientAddress))]
    [InlineData(nameof(CreateShipmentRequest.City))]
    [InlineData(nameof(CreateShipmentRequest.WeightKg))]
    public void CreateShipmentRequest_RequiredFieldMissing_ReportsThatField(string field)
    {
        // Arrange
        var request = field switch
        {
            nameof(CreateShipmentRequest.RecipientName) => ValidShipment(name: null),
            nameof(CreateShipmentRequest.RecipientPhone) => ValidShipment(phone: null),
            nameof(CreateShipmentRequest.RecipientAddress) => ValidShipment(address: null),
            nameof(CreateShipmentRequest.City) => ValidShipment(city: null),
            _ => ValidShipment(weightKg: null),
        };

        // Act
        var results = Validate(request);

        // Assert
        Assert.Equal([field], Members(results));
    }

    [Theory]
    [InlineData("+61 2 5550 1234", true)]
    [InlineData("(02) 5550-1234", true)]
    [InlineData("+61 2 5550 1234 5678", true)]    // 15 digits: the most allowed
    [InlineData("5550123", false)]                // 7 digits: one too few
    [InlineData("+61 2 5550 1234 56789", false)]  // 16 digits: one too many
    [InlineData("02--5550", false)]
    [InlineData("phone 0255501234", false)]
    public void CreateShipmentRequest_PhoneNumber_Needs8To15DigitsInAPlainLayout(string phone, bool expectedValid)
    {
        // Arrange
        var request = ValidShipment(phone: phone);

        // Act
        var results = Validate(request);

        // Assert
        Assert.Equal(expectedValid, !Members(results).Contains(nameof(CreateShipmentRequest.RecipientPhone)));
    }

    [Fact]
    public void ScanUploadItem_ScanTimeAtTheClockTolerance_IsAccepted()
    {
        // Arrange
        var scan = ValidScan(at: _clock.GetUtcNow() + ShipmentService.ScanClockTolerance);

        // Act
        var results = Validate(scan);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void ScanUploadItem_ScanTimeOneSecondBeyondTheTolerance_IsRejectedByNotInFuture()
    {
        // Arrange
        var scan = ValidScan(at: _clock.GetUtcNow() + ShipmentService.ScanClockTolerance + TimeSpan.FromSeconds(1));

        // Act
        var results = Validate(scan);

        // Assert
        var error = Assert.Single(results);
        Assert.Equal([nameof(ScanUploadItem.ScannedAtUtc)], error.MemberNames);
        Assert.Contains("cannot be in the future", error.ErrorMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void ScanUploadItem_DeliveredWithoutSignature_IsRejectedByTheValidatableRule(string? signature)
    {
        // Arrange
        var scan = ValidScan(type: ScanType.Delivered, signature: signature);

        // Act
        var results = Validate(scan);

        // Assert
        var error = Assert.Single(results);
        Assert.Equal(["recipientSignatureName"], error.MemberNames);
    }

    [Theory]
    [InlineData(ScanType.Delivered, "Mia Fictional")]
    [InlineData(ScanType.Failed, null)]
    [InlineData(ScanType.Pickup, null)]
    public void ScanUploadItem_SignatureRule_AppliesOnlyToDelivered(ScanType type, string? signature)
    {
        // Arrange
        var scan = ValidScan(type: type, signature: signature);

        // Act
        var results = Validate(scan);

        // Assert
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(ScanUploadRequest.MaximumScans, true)]
    [InlineData(ScanUploadRequest.MaximumScans + 1, false)]
    public void ScanUploadRequest_BatchSize_From1ToMaximum(int count, bool expectedValid)
    {
        // Arrange
        var request = new ScanUploadRequest { Scans = Enumerable.Range(0, count).Select(_ => ValidScan()).ToList() };

        // Act
        var results = Validate(request);

        // Assert
        Assert.Equal(expectedValid, results.Count == 0);
    }
}
