using ApexPost.Application.Pricing;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform
{
    public class DomainRulesTests
    {
        private readonly TieredShippingPricingStrategy _pricing = new();

        [Fact]
        public void MoneyOrder_OverThresholdWithoutId_Throws()
        {
            // Arrange
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                BranchCode = new Domain.ValueObjects.BranchCode("B001"),
                CounterId = "C01",
                ClerkStaffId = "STF01"
            };

            tx.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Money Order", Domain.Enums.LineItemType.MoneyOrder, 1, new Domain.ValueObjects.Money(1510m), Domain.Enums.TaxCategory.Exempt,
                MoneyOrder: new MoneyOrderDetails(new Domain.ValueObjects.Money(1500m), "Alice", "Bob", new Domain.ValueObjects.Money(10m))));

            // Act
            Action act = () => tx.Complete("Cash", new Domain.ValueObjects.Money(1000m));

            // Assert
            Assert.Throws<DomainValidationException>(act);
        }

        [Fact]
        public void MoneyOrder_OverThresholdWithId_Succeeds()
        {
            // Arrange
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                BranchCode = new Domain.ValueObjects.BranchCode("B001"),
                CounterId = "C01",
                ClerkStaffId = "STF01"
            };

            tx.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Money Order", Domain.Enums.LineItemType.MoneyOrder, 1, new Domain.ValueObjects.Money(1510m), Domain.Enums.TaxCategory.Exempt, MoneyOrder: new MoneyOrderDetails(new Domain.ValueObjects.Money(1500m), "Alice", "Bob", new Domain.ValueObjects.Money(10m))));

            tx.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "ID Check", Domain.Enums.LineItemType.IdentityVerification, 1, Money.Zero, Domain.Enums.TaxCategory.Exempt, IdVerification: new IdVerificationDetails(Domain.Enums.DocumentType.Passport, "******1234", true)));

            // Act
            tx.Complete("Cash", new Money(1000m));

            // Assert
            Assert.Equal(TransactionStatus.Paid, tx.Status);
        }

        [Fact]
        public void Parcel_OverWeightLimit_Throws()
        {
            // Arrange
            var parcel = new ShippingDetails(true, Weight.FromKilograms(13.5m), new Dimensions(10, 10, 10), "Zone 1", ServiceLevel.Express);

            // Act
            Action act = () => _pricing.ValidateWeightLimits(parcel);

            // Assert
            Assert.Throws<DomainValidationException>(act);
        }

        [Fact]
        public void Void_ByNonManager_ThrowsUnauthorized()
        {
            // Arrange
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                BranchCode = new BranchCode("B001"),
                CounterId = "C01",
                ClerkStaffId = "STF01"
            };
            tx.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Stamp", LineItemType.RetailProduct, 1, new Money(2m), TaxCategory.StandardGst, new Sku("SKU-001")));
            tx.Complete("Cash", new Money(1000m));

            var clerk = new Staff
            {
                Id = "STF01",
                Name = "John",
                Email = "j@apex.com",
                Role = StaffRole.CounterClerk,
                HomeBranchCode = new BranchCode("B001")
            };

            // Act
            Action act = () => tx.Void(clerk, "Customer changed mind");

            // Assert
            Assert.Throws<UnauthorizedDomainActionException>(act);
        }

        [Fact]
        public void ReduceStock_BelowZero_ThrowsAndKeepsTheQuantity()
        {
            // Arrange
            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                BranchCode = new BranchCode("B001"),
                ProductSku = new Sku("SKU-001")
            };
            stock.AddStock(2);
            stock.ReduceStock(2);

            // Act
            var exception = Record.Exception(() => stock.ReduceStock(1));

            // Assert
            Assert.IsType<DomainValidationException>(exception);
            Assert.Equal(0, stock.QuantityOnHand);
        }

        [Fact]
        public void CalculateShippingPrice_StandardParcelInZone2_UsesServiceLevelZoneAndWeight()
        {
            // Arrange
            var parcel = new ShippingDetails(
                true,
                Weight.FromKilograms(2m),
                new Dimensions(30, 20, 10),
                "Zone 2",
                ServiceLevel.Standard);

            // Act
            var price = _pricing.CalculateShippingPrice(parcel);

            // Assert
            Assert.Equal(new Money(13m), price);
        }

        [Fact]
        public void CalculateShippingPrice_InjectedRuleAndSurcharge_UsesThem()
        {
            // Arrange
            var parcel = new ShippingDetails(
                true,
                Weight.FromKilograms(2m),
                new Dimensions(30, 20, 10),
                "Zone 2",
                ServiceLevel.Standard);
            var invoked = false;
            var custom = new TieredShippingPricingStrategy((details, zoneMultiplier) =>
            {
                invoked = true;
                return TieredShippingPricingStrategy.CalculateDefaultPrice(
                    details,
                    zoneMultiplier,
                    parcelWeightSurchargePerKilogram: 3m);
            });

            // Act
            var price = custom.CalculateShippingPrice(parcel, zoneMultiplier: 1.25m);

            // Assert
            Assert.True(invoked);
            Assert.Equal(new Money(18.5m), price);
        }

        [Fact]
        public void FromDocumentNumber_FullNumber_StoresOnlyTheMaskedLastFour()
        {
            // Act
            var details = IdVerificationDetails.FromDocumentNumber(DocumentType.Passport, "A123456789", true);

            // Assert
            Assert.Equal("******6789", details.MaskedDocNumber);
        }

        [Fact]
        public void Constructor_UnmaskedDocumentValue_Throws()
        {
            // Act
            Action act = () => new IdVerificationDetails(DocumentType.Passport, "A123456789", true);

            // Assert
            Assert.Throws<ArgumentException>(act);
        }

        [Fact]
        public void RefundRestockMarker_JsonRoundTrip_SurvivesAndPreventsDuplicateStock()
        {
            // Arrange
            var transactionId = Guid.NewGuid();
            var stock = new BranchStock
            {
                Id = Guid.NewGuid(),
                BranchCode = new BranchCode("B001"),
                ProductSku = new Sku("SKU-001")
            };
            stock.AddStock(4);
            stock.RestockForReversal(transactionId, 3);

            var restored = System.Text.Json.JsonSerializer.Deserialize<BranchStock>(
                System.Text.Json.JsonSerializer.Serialize(stock))
                ?? throw new InvalidOperationException("Stock round-trip returned null.");

            // Act
            restored.RestockForReversal(transactionId, 3);

            // Assert
            Assert.Equal(7, restored.QuantityOnHand);
            Assert.Contains(transactionId, restored.RestockedReversalTransactionIds);
        }

        [Fact]
        public void Transaction_JsonRoundTrip_KeepsStatusAndPayment()
        {
            // Arrange
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                BranchCode = new BranchCode("B001"),
                CounterId = "C01",
                ClerkStaffId = "STF001"
            };
            transaction.AddLineItem(new TransactionLineItem(
                Guid.NewGuid(),
                "Box",
                LineItemType.RetailProduct,
                1,
                new Money(5m),
                TaxCategory.StandardGst,
                new Sku("SKU-001")));
            transaction.Complete("Cash", new Money(1000m));
            var manager = new Staff
            {
                Id = "STF001",
                Name = "Manager",
                Email = "manager@example.test",
                Role = StaffRole.BranchManager,
                HomeBranchCode = new BranchCode("B001")
            };
            static Transaction RoundTrip(Transaction value) =>
                System.Text.Json.JsonSerializer.Deserialize<Transaction>(System.Text.Json.JsonSerializer.Serialize(value))
                ?? throw new InvalidOperationException("Transaction round-trip returned null.");

            // Act
            // Round-trip the sale, void the restored copy, then round-trip the reversal.
            var restored = RoundTrip(transaction);
            var (statusAfterRoundTrip, paymentAfterRoundTrip, linesAfterRoundTrip) = (restored.Status, restored.PaymentMethod, restored.LineItems.Count);
            restored.Void(manager, "Customer requested cancellation");
            var restoredReversal = RoundTrip(restored);

            // Assert
            Assert.Equal(TransactionStatus.Paid, statusAfterRoundTrip);
            Assert.Equal("Cash", paymentAfterRoundTrip);
            Assert.Equal(1, linesAfterRoundTrip);
            Assert.Equal(TransactionStatus.Voided, restoredReversal.Status);
            Assert.Equal("Customer requested cancellation", restoredReversal.VoidReason);
            Assert.Equal("STF001", restoredReversal.VoidedByManagerId);
        }

        [Fact]
        public void Add_DifferentCurrencies_Throws()
        {
            // Arrange
            var australianDollar = new Money(1m, "AUD");
            var usDollar = new Money(1m, "USD");

            // Act
            Action act = () => _ = australianDollar + usDollar;

            // Assert
            Assert.Throws<InvalidOperationException>(act);
        }

        [Fact]
        public void Complete_UnsupportedPaymentMethod_ThrowsAndStaysOpen()
        {
            // Arrange
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                BranchCode = new BranchCode("B001"),
                CounterId = "C01",
                ClerkStaffId = "STF01"
            };
            transaction.AddLineItem(new TransactionLineItem(
                Guid.NewGuid(), "Box", LineItemType.RetailProduct, 1, new Money(5m), TaxCategory.StandardGst, new Sku("SKU-001")));

            // Act
            var exception = Record.Exception(() => transaction.Complete("Cheque", new Money(1000m)));

            // Assert
            Assert.IsType<DomainValidationException>(exception);
            Assert.Equal(TransactionStatus.Open, transaction.Status);
        }

        [Fact]
        public void AddLineItem_BlankDescription_ThrowsFromTheReflectionValidator()
        {
            // Arrange
            var transaction = new Transaction
            {
                Id = Guid.NewGuid(),
                BranchCode = new BranchCode("B001"),
                CounterId = "C01",
                ClerkStaffId = "STF01"
            };
            var line = new TransactionLineItem(
                Guid.NewGuid(),
                " ",
                LineItemType.RetailProduct,
                1,
                new Money(5m),
                TaxCategory.StandardGst,
                new Sku("SKU-001"));

            // Act
            Action act = () => transaction.AddLineItem(line);

            // Assert
            Assert.Throws<DomainValidationException>(act);
        }

        [Fact]
        public void IsOpen_UtcTime_UsesTheBranchLocalTime()
        {
            // Arrange
            var branch = new Branch
            {
                Id = new BranchCode("B001"),
                Name = "Sydney",
                Region = "NSW",
                TimeZoneId = "Australia/Sydney",
                OpenTime = new TimeSpan(9, 0, 0),
                CloseTime = new TimeSpan(17, 0, 0)
            };

            // Act
            // 00:00Z is 11:00 in Sydney (daylight time) and 20:00Z is 07:00 the next morning.
            var openAtMidnightUtc = branch.IsOpen(DateTimeOffset.Parse("2026-10-25T00:00:00Z"));
            var openAtEightPmUtc = branch.IsOpen(DateTimeOffset.Parse("2026-10-25T20:00:00Z"));

            // Assert
            Assert.True(openAtMidnightUtc);
            Assert.False(openAtEightPmUtc);
        }

        [Fact]
        public void EnsureCanTransact_InactiveOrOtherBranchStaff_Throws()
        {
            // Arrange
            var staff = new Staff
            {
                Id = "STF01",
                Name = "Clerk",
                Email = "clerk@example.test",
                Role = StaffRole.CounterClerk,
                HomeBranchCode = new BranchCode("B001"),
                IsActive = false
            };
            var activeStaff = new Staff
            {
                Id = "STF02",
                Name = "Clerk",
                Email = "clerk2@example.test",
                Role = StaffRole.CounterClerk,
                HomeBranchCode = new BranchCode("B001")
            };
            var auditor = new Staff
            {
                Id = "STF03",
                Name = "Auditor",
                Email = "auditor@example.test",
                Role = StaffRole.Auditor,
                HomeBranchCode = new BranchCode("B001")
            };

            // Act
            Action inactiveAtHome = () => staff.EnsureCanTransact(new BranchCode("B001"));
            Action activeAtAnotherBranch = () => activeStaff.EnsureCanTransact(new BranchCode("B002"));
            Action auditorAtHome = () => auditor.EnsureCanTransact(new BranchCode("B001"));

            // Assert
            Assert.Throws<UnauthorizedDomainActionException>(inactiveAtHome);
            Assert.Throws<UnauthorizedDomainActionException>(activeAtAnotherBranch);
            Assert.Throws<UnauthorizedDomainActionException>(auditorAtHome);
        }
    }
}
