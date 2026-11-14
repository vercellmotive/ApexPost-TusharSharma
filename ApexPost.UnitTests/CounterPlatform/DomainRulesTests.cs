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

            // Act and assert
            Assert.Throws<DomainValidationException>(() => tx.Complete("Cash", new Domain.ValueObjects.Money(1000m)));
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

            // Act and assert
            Assert.Throws<DomainValidationException>(() => _pricing.ValidateWeightLimits(parcel));
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

           // Act and assert
           Assert.Throws<UnauthorizedDomainActionException>(() => tx.Void(clerk, "Customer changed mind"));
        }

        [Fact]
        public void Stock_CannotBeReducedBelowZero()
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

            // Act and assert
            Assert.Equal(0, stock.QuantityOnHand);
            Assert.Throws<DomainValidationException>(() => stock.ReduceStock(1));
            Assert.Equal(0, stock.QuantityOnHand);
        }

        [Fact]
        public void Pricing_UsesServiceLevelZoneAndWeight()
        {
            // Act
            var parcel = new ShippingDetails(
                true,
                Weight.FromKilograms(2m),
                new Dimensions(30, 20, 10),
                "Zone 2",
                ServiceLevel.Standard);

            // Assert
            Assert.Equal(new Money(13m), _pricing.CalculateShippingPrice(parcel));
        }

        [Fact]
        public void Pricing_UsesInjectedRuleAndConfigurableWeightSurcharge()
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
        public void IdentityVerification_StoresOnlyMaskedLastFourCharacters()
        {
            // Act
            var details = IdVerificationDetails.FromDocumentNumber(DocumentType.Passport, "A123456789", true);

            // Assert
            Assert.Equal("******6789", details.MaskedDocNumber);
        }

        [Fact]
        public void IdentityVerification_RejectsUnmaskedDocumentValues()
        {
            // Act and assert
            Assert.Throws<ArgumentException>(() =>
                new IdVerificationDetails(DocumentType.Passport, "A123456789", true));
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

            // Act
            var restored = System.Text.Json.JsonSerializer.Deserialize<Transaction>(
                System.Text.Json.JsonSerializer.Serialize(transaction))
                ?? throw new InvalidOperationException("Transaction round-trip returned null.");

            // Assert
            Assert.Equal(TransactionStatus.Paid, restored.Status);
            Assert.Equal("Cash", restored.PaymentMethod);
            Assert.Single(restored.LineItems);

            // Arrange (next step)
            var manager = new Staff
            {
                Id = "STF001",
                Name = "Manager",
                Email = "manager@example.test",
                Role = StaffRole.BranchManager,
                HomeBranchCode = new BranchCode("B001")
            };
            restored.Void(manager, "Customer requested cancellation");

            // Act
            var restoredReversal = System.Text.Json.JsonSerializer.Deserialize<Transaction>(
                System.Text.Json.JsonSerializer.Serialize(restored))
                ?? throw new InvalidOperationException("Transaction reversal round-trip returned null.");

            // Assert
            Assert.Equal(TransactionStatus.Voided, restoredReversal.Status);
            Assert.Equal("Customer requested cancellation", restoredReversal.VoidReason);
            Assert.Equal("STF001", restoredReversal.VoidedByManagerId);
        }

        [Fact]
        public void Money_RejectsArithmeticAcrossCurrencies()
        {
            // Act and assert
            Assert.Throws<InvalidOperationException>(() => new Money(1m, "AUD") + new Money(1m, "USD"));
        }

        [Fact]
        public void Transaction_RejectsUnsupportedPaymentMethod()
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

            // Act and assert
            Assert.Throws<DomainValidationException>(() => transaction.Complete("Cheque", new Money(1000m)));
            Assert.Equal(TransactionStatus.Open, transaction.Status);
        }

        [Fact]
        public void TransactionLineItem_ReflectionValidatorRejectsBlankDescription()
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

            // Act and assert
            Assert.Throws<DomainValidationException>(() => transaction.AddLineItem(line));
        }

        [Fact]
        public void Branch_UsesItsLocalTimeForOpeningHours()
        {
            // Act
            var branch = new Branch
            {
                Id = new BranchCode("B001"),
                Name = "Sydney",
                Region = "NSW",
                TimeZoneId = "Australia/Sydney",
                OpenTime = new TimeSpan(9, 0, 0),
                CloseTime = new TimeSpan(17, 0, 0)
            };

            // Assert
            Assert.True(branch.IsOpen(DateTimeOffset.Parse("2026-10-25T00:00:00Z")));
            Assert.False(branch.IsOpen(DateTimeOffset.Parse("2026-10-25T20:00:00Z")));
        }

        [Fact]
        public void InactiveOrOutOfBranchStaff_CannotTransact()
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

            // Act and assert
            Assert.Throws<UnauthorizedDomainActionException>(() => staff.EnsureCanTransact(new BranchCode("B001")));

            // Arrange (next step)
            var activeStaff = new Staff
            {
                Id = "STF02",
                Name = "Clerk",
                Email = "clerk2@example.test",
                Role = StaffRole.CounterClerk,
                HomeBranchCode = new BranchCode("B001")
            };

            // Act and assert
            Assert.Throws<UnauthorizedDomainActionException>(() => activeStaff.EnsureCanTransact(new BranchCode("B002")));

            // Arrange (next step)
            var auditor = new Staff
            {
                Id = "STF03",
                Name = "Auditor",
                Email = "auditor@example.test",
                Role = StaffRole.Auditor,
                HomeBranchCode = new BranchCode("B001")
            };

            // Act and assert
            Assert.Throws<UnauthorizedDomainActionException>(() => auditor.EnsureCanTransact(new BranchCode("B001")));
        }
    }
}
