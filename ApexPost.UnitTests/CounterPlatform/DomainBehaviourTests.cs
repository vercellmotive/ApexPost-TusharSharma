using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public class DomainBehaviourTests
{
    private static readonly BranchCode B001 = new("B001");

    private static Transaction NewTransaction() => new()
    {
        Id = Guid.NewGuid(),
        BranchCode = B001,
        CounterId = "C01",
        ClerkStaffId = "STF001"
    };

    private static Staff NewStaff(StaffRole role, string branch = "B001", bool isActive = true) => new()
    {
        Id = "STF001",
        Name = "Test",
        Email = "test@apexpost.com",
        Role = role,
        HomeBranchCode = new BranchCode(branch),
        IsActive = isActive
    };

    private static Product NewProduct(string sku = "SKU-001") => new()
    {
        Id = new Sku(sku),
        Name = "Box",
        Category = ProductCategories.Packaging,
        UnitPrice = new Money(5m),
        TaxCategory = TaxCategory.StandardGst
    };

    [Fact]
    public void Branch_RejectsCounterFromAnotherBranch()
    {
        // Arrange
        var branch = new Branch
        {
            Id = B001,
            Name = "Sydney",
            Region = "NSW",
            OpenTime = TimeSpan.Zero,
            CloseTime = TimeSpan.FromHours(23),
            CounterTerminals = ["C01"]
        };

        branch.EnsureHasCounter("C01");

        // Act and assert
        Assert.Throws<DomainValidationException>(() => branch.EnsureHasCounter("C99"));
    }

    [Theory]
    [InlineData(StaffRole.CounterClerk, "B001", true)]
    [InlineData(StaffRole.BranchManager, "B002", true)]
    [InlineData(StaffRole.BranchManager, "B001", false)]
    public void Staff_ManagesOnlyTheirBranch_WhenActiveManager(StaffRole role, string branch, bool isActive)
    {
        // Arrange
        var staff = NewStaff(role, branch, isActive);

        // Act
        Action act = () => staff.EnsureManages(B001);

        // Assert
        Assert.Throws<UnauthorizedDomainActionException>(act);
    }

    [Fact]
    public void Staff_ActiveManagerOfBranch_IsAccepted()
    {
        // Arrange
        var manager = NewStaff(StaffRole.BranchManager);

        // Act
        var exception = Record.Exception(() => manager.EnsureManages(B001));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public void Transaction_AddRetailProduct_CopiesPriceAndTaxFromProduct()
    {
        // Arrange
        var transaction = NewTransaction();

        // Act
        transaction.AddRetailProduct(NewProduct(), 2);

        // Assert
        var line = Assert.Single(transaction.LineItems);
        Assert.Equal(LineItemType.RetailProduct, line.ItemType);
        Assert.Equal(new Sku("SKU-001"), line.ProductSku);
        Assert.Equal(new Money(10m), line.TotalPrice);
    }

    [Fact]
    public void Transaction_AddMoneyOrder_ChargesAmountPlusFeeButCountsOnlyFeeAsRevenue()
    {
        // Arrange
        var transaction = NewTransaction();

        // Act
        transaction.AddMoneyOrder(new Money(100m), " Alice ", " Bob ", new Money(10m));

        // Assert
        var line = Assert.Single(transaction.LineItems);
        Assert.Equal(new Money(110m), line.UnitPrice);
        Assert.Equal(new Money(10m), line.Revenue);
        Assert.Equal("Alice", line.MoneyOrder!.Sender);
    }

    [Theory]
    [InlineData(0, "Alice", "Bob")]
    [InlineData(100, " ", "Bob")]
    [InlineData(100, "Alice", "")]
    public void Transaction_AddMoneyOrder_RejectsIncompleteOrders(int amount, string sender, string recipient)
    {
        // Arrange
        var transaction = NewTransaction();

        // Act
        Action act = () => transaction.AddMoneyOrder(new Money(amount), sender, recipient, new Money(10m));

        // Assert
        Assert.Throws<DomainValidationException>(act);
    }

    [Fact]
    public void Transaction_RetailQuantities_MergesLinesAndIgnoresServices()
    {
        // Arrange
        var transaction = NewTransaction();
        transaction.AddRetailProduct(NewProduct(), 2);
        transaction.AddRetailProduct(NewProduct(), 3);
        transaction.AddRetailProduct(NewProduct("SKU-002"), 1);
        transaction.AddMoneyOrder(new Money(100m), "Alice", "Bob", new Money(10m));

        // Act
        var quantities = transaction.RetailQuantities();

        // Assert
        Assert.Equal(2, quantities.Count);
        Assert.Contains((new Sku("SKU-001"), 5), quantities);
        Assert.Contains((new Sku("SKU-002"), 1), quantities);
    }

    [Fact]
    public void Transaction_CannotAddLinesAfterCompletion()
    {
        // Arrange
        var transaction = NewTransaction();
        transaction.AddRetailProduct(NewProduct(), 1);
        transaction.Complete("Cash", new Money(1000m));

        // Act and assert
        Assert.Throws<DomainValidationException>(() => transaction.AddRetailProduct(NewProduct(), 1));
    }

    [Fact]
    public void Product_Create_NormalisesItsInput()
    {
        // Act
        var product = Product.Create(new Sku("SKU-001"), "  Tape  ", "packaging", 2.50m, TaxCategory.StandardGst);

        // Assert
        Assert.Equal("Tape", product.Name);
        Assert.Equal(ProductCategories.Packaging, product.Category);
        Assert.Equal(new Money(2.50m), product.UnitPrice);
    }

    [Theory]
    [InlineData(" ", "Packaging", 1, TaxCategory.StandardGst)]
    [InlineData("Tape", "Unknown", 1, TaxCategory.StandardGst)]
    [InlineData("Tape", "Packaging", 0, TaxCategory.StandardGst)]
    [InlineData("Tape", "Packaging", 1, (TaxCategory)99)]
    public void Product_Create_RejectsInvalidInput(string name, string category, int price, TaxCategory tax)
    {
        // Act and assert
        Assert.Throws<DomainValidationException>(() => Product.Create(new Sku("SKU-001"), name, category, price, tax));
    }

    [Fact]
    public void Product_WithAnUnsupportedCategory_CannotBeSold()
    {
        // Arrange
        var product = new Product { Id = new Sku("SKU-001"), Name = "Old", Category = "Retired", UnitPrice = new Money(1m), TaxCategory = TaxCategory.Exempt };

        // Act and assert
        Assert.Throws<DomainValidationException>(product.EnsureSellable);
        Product.Create(new Sku("SKU-002"), "Tape", ProductCategories.Packaging, 1m, TaxCategory.Exempt).EnsureSellable();
    }

    [Fact]
    public void ShippingZone_OnlyOffersAPositiveMultiplier()
    {
        // Act and assert
        Assert.Equal(1.5m, new ShippingZone { Id = "Zone 5", Name = "Zone 5", Multiplier = 1.5m }.UsableMultiplier());
        Assert.Throws<DomainValidationException>(() => new ShippingZone { Id = "Zone 0", Name = "Zone 0", Multiplier = 0m }.UsableMultiplier());
    }
}
