using ApexPost.Application.Pricing;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.Events;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Services;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class DomainEdgeCaseTests
{
    private readonly TieredShippingPricingStrategy _pricing = new();

    [Fact]
    public void LetterOverItsWeightLimit_IsRejected()
    {
        var letter = new ShippingDetails(false, Weight.FromKilograms(0.6m), default, "Zone 1", ServiceLevel.Standard);

        Assert.Throws<DomainValidationException>(() => _pricing.ValidateWeightLimits(letter));
    }

    [Fact]
    public void LetterWithoutDimensions_IsAccepted()
    {
        var letter = new ShippingDetails(false, Weight.FromKilograms(0.2m), default, "Zone 1", ServiceLevel.Express);

        _pricing.ValidateWeightLimits(letter);
    }

    [Fact]
    public void ParcelWithoutDimensions_IsRejected()
    {
        var parcel = new ShippingDetails(true, Weight.FromKilograms(1m), default, "Zone 1", ServiceLevel.Standard);

        Assert.Throws<DomainValidationException>(() => _pricing.ValidateWeightLimits(parcel));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ZeroWeight_IsRejected(bool isParcel)
    {
        var details = new ShippingDetails(isParcel, Weight.FromKilograms(0m), new Dimensions(1, 1, 1), "Zone 1", ServiceLevel.Standard);

        Assert.Throws<DomainValidationException>(() => _pricing.ValidateWeightLimits(details));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NegativeDimensions_AreRejected(bool isParcel)
    {
        var details = new ShippingDetails(isParcel, Weight.FromKilograms(1m), new Dimensions(-1, 1, 1), "Zone 1", ServiceLevel.Standard);

        Assert.Throws<DomainValidationException>(() => _pricing.ValidateWeightLimits(details));
    }

    [Fact]
    public void LetterWithNoSize_IsAcceptedButAParcelIsNot()
    {
        var letter = new ShippingDetails(false, Weight.FromKilograms(0.1m), new Dimensions(0, 0, 0), "Zone 1", ServiceLevel.Standard);
        var parcel = letter with { IsParcel = true };

        _pricing.ValidateWeightLimits(letter);
        Assert.Throws<DomainValidationException>(() => _pricing.ValidateWeightLimits(parcel));
    }

    [Fact]
    public void LineItems_CannotBeChangedWithoutAddLineItem()
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            BranchCode = new BranchCode("B001"),
            CounterId = "C01",
            ClerkStaffId = "STF001"
        };

        Assert.False(transaction.LineItems is List<TransactionLineItem>);
        Assert.True(((ICollection<TransactionLineItem>)transaction.LineItems).IsReadOnly);
    }

    [Fact]
    public void DomainEventIdentity_IsStableAcrossReads()
    {
        IDomainEvent domainEvent = new StockLowEvent(new BranchCode("B001"), new Sku("SKU-001"), 1);

        Assert.Equal(domainEvent.EventId, domainEvent.EventId);
        Assert.Equal(domainEvent.OccurredOnUtc, domainEvent.OccurredOnUtc);
    }

    [Fact]
    public void FourCharacterDocumentNumber_IsNeverStoredUnmasked()
    {
        Assert.Throws<ArgumentException>(() =>
            IdVerificationDetails.FromDocumentNumber(DocumentType.Passport, "AB12", true));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void LowStockRule_UsesItsConfiguredThreshold(int quantity, bool expected)
    {
        Assert.Equal(expected, new LowStockRule(5).IsLow(quantity));
    }

    [Fact]
    public void PasswordHasher_VerifiesOnlyTheOriginalPasswordAndSaltsEachHash()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var first = hasher.Hash("correct-horse-battery");
        var second = hasher.Hash("correct-horse-battery");

        Assert.NotEqual(first, second);
        Assert.DoesNotContain("correct-horse-battery", first);
        Assert.True(hasher.Verify("correct-horse-battery", first));
        Assert.False(hasher.Verify("wrong-password", first));
        Assert.False(hasher.Verify("correct-horse-battery", null));
    }

    [Fact]
    public void TransactionTotals_SplitRetailFromServices()
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            BranchCode = new BranchCode("B001"),
            CounterId = "C01",
            ClerkStaffId = "STF001"
        };
        transaction.AddLineItem(new TransactionLineItem(
            Guid.NewGuid(), "Tape", LineItemType.RetailProduct, 2, new Money(10m), TaxCategory.StandardGst, new Sku("SKU-001")));
        transaction.AddLineItem(new TransactionLineItem(
            Guid.NewGuid(),
            "Shipping",
            LineItemType.ParcelShipping,
            1,
            new Money(5m),
            TaxCategory.Exempt,
            Shipping: new ShippingDetails(true, Weight.FromKilograms(1m), new Dimensions(1, 1, 1), "Zone 1", ServiceLevel.Standard)));

        Assert.Equal(new Money(22m), transaction.RetailTotal);
        Assert.Equal(new Money(5m), transaction.ServicesTotal);
    }

    [Fact]
    public void ServicesTotal_CountsOnlyTheMoneyOrderFeeAsRevenue()
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            BranchCode = new BranchCode("B001"),
            CounterId = "C01",
            ClerkStaffId = "STF001"
        };
        transaction.AddLineItem(new TransactionLineItem(
            Guid.NewGuid(),
            "Money order",
            LineItemType.MoneyOrder,
            1,
            new Money(510m),
            TaxCategory.Exempt,
            MoneyOrder: new MoneyOrderDetails(new Money(500m), "A", "B", new Money(10m))));

        Assert.Equal(new Money(10m), transaction.ServicesTotal);
        Assert.Equal(new Money(510m), transaction.GrandTotal);
    }
}
