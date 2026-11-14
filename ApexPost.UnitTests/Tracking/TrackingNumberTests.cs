using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Domain;

namespace ApexPost.UnitTests.Tracking;

/// <summary>R7: the tracking number format <c>^APX-\d{8}$</c>, with the boundaries either side of eight digits.</summary>
public sealed class TrackingNumberTests
{
    [Theory]
    [InlineData("APX-00000000")]
    [InlineData("APX-12345678")]
    [InlineData("APX-99999999")]
    public void IsValid_WellFormedNumber_ReturnsTrue(string trackingNumber)
    {
        // Act
        var valid = TrackingNumber.IsValid(trackingNumber);

        // Assert
        Assert.True(valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("APX-1234567")]      // seven digits: one short
    [InlineData("APX-123456789")]    // nine digits: one too many
    [InlineData("apx-12345678")]     // the prefix is upper case
    [InlineData("APX12345678")]      // no hyphen
    [InlineData("APX-1234567A")]     // a letter in place of a digit
    [InlineData("APX-١٢٣٤٥٦٧٨")]     // Arabic-Indic digits are digits to .NET, but not to the format
    [InlineData(" APX-12345678")]    // leading space
    [InlineData("APX-12345678\n")]   // trailing newline, which $ alone would allow
    public void IsValid_MalformedNumber_ReturnsFalse(string? trackingNumber)
    {
        // Act
        var valid = TrackingNumber.IsValid(trackingNumber);

        // Assert
        Assert.False(valid);
    }

    [Theory]
    [InlineData("APX-1234567")]
    [InlineData("APX-123456789")]
    [InlineData(" APX-12345678")]
    public void Create_MalformedTrackingNumber_ThrowsArgumentException(string trackingNumber)
    {
        // Act
        Action act = () => Shipment.Create(trackingNumber, "B001", Make.Recipient(), 1m, TestTime.Start);

        // Assert
        var exception = Assert.Throws<ArgumentException>(act);
        Assert.Equal("trackingNumber", exception.ParamName);
    }
}
