using System.Globalization;
using ApexPost.Testing.Tracking;
using ApexPost.Tracking.Domain;

namespace ApexPost.UnitTests.Tracking;

/// <summary>R7: a shipment weighs from 0.1 to 70 kg, both limits included.</summary>
public sealed class ShipmentWeightTests
{
    [Theory]
    [InlineData("0.1")]      // the lower limit itself
    [InlineData("0.101")]
    [InlineData("35")]
    [InlineData("69.999")]
    [InlineData("70")]       // the upper limit itself
    public void Create_WeightWithinLimits_KeepsTheWeight(string weight)
    {
        // Arrange
        var weightKg = decimal.Parse(weight, CultureInfo.InvariantCulture);

        // Act
        var shipment = Shipment.Create("APX-12345678", "B001", Make.Recipient(), weightKg, TestTime.Start);

        // Assert
        Assert.Equal(weightKg, shipment.WeightKg);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.099")]    // just under the lower limit
    [InlineData("-1")]
    [InlineData("70.001")]   // just over the upper limit
    [InlineData("1000")]
    public void Create_WeightOutsideLimits_ThrowsArgumentOutOfRangeException(string weight)
    {
        // Arrange
        var weightKg = decimal.Parse(weight, CultureInfo.InvariantCulture);

        // Act
        Action act = () => Shipment.Create("APX-12345678", "B001", Make.Recipient(), weightKg, TestTime.Start);

        // Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(act);
        Assert.Equal("weightKg", exception.ParamName);
    }

    [Fact]
    public void ReplaceDetails_WeightOverLimit_ThrowsAndKeepsTheOldWeight()
    {
        // Arrange
        var shipment = Make.Shipment();

        // Act
        Action act = () => shipment.ReplaceDetails(Make.Recipient(), 70.001m, TestTime.Start);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(act);
        Assert.Equal(2.5m, shipment.WeightKg);
    }

    [Theory]
    [InlineData("", "+61 2 5550 1234", "12 Example Lane", "Sydney")]
    [InlineData("Mia Fictional", " ", "12 Example Lane", "Sydney")]
    [InlineData("Mia Fictional", "+61 2 5550 1234", "", "Sydney")]
    [InlineData("Mia Fictional", "+61 2 5550 1234", "12 Example Lane", "  ")]
    public void Create_BlankRecipientField_ThrowsArgumentException(string name, string phone, string address, string city)
    {
        // Arrange
        var recipient = new Recipient(name, phone, address, city);

        // Act
        Action act = () => Shipment.Create("APX-12345678", "B001", recipient, 1m, TestTime.Start);

        // Assert
        Assert.ThrowsAny<ArgumentException>(act);
    }
}
