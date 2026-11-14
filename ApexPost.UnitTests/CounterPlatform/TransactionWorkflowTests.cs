using ApexPost.Application.Interfaces;
using ApexPost.Application.Services;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

// Application services against fake repositories and a spy unit of work: commit once, or never on failure.
public sealed class TransactionWorkflowTests
{
    private const string Branch = "B001";
    private const string Sku1 = "SKU-001";

    [Fact]
    public async Task FailedBusinessValidation_NeverCommitsAndLeavesStockUnchanged()
    {
        var (database, persistence, service) = Arrange(stock: 10);
        var transaction = CreateTransaction();
        service.AddMoneyOrder(transaction, new Money(1500m), "Sender", "Recipient");

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CompleteWithRetailItemsAsync(transaction, [new RetailItemRequest(new Sku(Sku1), 3)], "Cash"));

        Assert.Equal(0, persistence.CommitCount);
        Assert.Equal(10, database.StockOf(Branch, Sku1));
        Assert.Empty(database.AllTransactions());
    }

    [Fact]
    public async Task UnknownProductInBasket_NeverCommits()
    {
        var (database, persistence, service) = Arrange(stock: 10);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.CompleteWithRetailItemsAsync(
                CreateTransaction(),
                [new RetailItemRequest(new Sku(Sku1), 3), new RetailItemRequest(new Sku("SKU-404"), 1)],
                "Cash"));

        Assert.Equal(0, persistence.CommitCount);
        Assert.Equal(10, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public async Task InsufficientStock_FailsWithoutCommitting()
    {
        var (database, persistence, service) = Arrange(stock: 2);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CompleteWithRetailItemsAsync(CreateTransaction(), [new RetailItemRequest(new Sku(Sku1), 3)], "Cash"));

        Assert.Equal(0, persistence.CommitCount);
        Assert.Equal(2, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public async Task FailedSave_LeavesNothingBehind()
    {
        var (database, persistence, service) = Arrange(stock: 10);
        persistence.FailNextSaveWith = new IOException("Simulated database failure.");

        await Assert.ThrowsAsync<IOException>(() =>
            service.CompleteWithRetailItemsAsync(CreateTransaction(), [new RetailItemRequest(new Sku(Sku1), 3)], "Cash"));

        Assert.Equal(10, database.StockOf(Branch, Sku1));
        Assert.Empty(database.AllTransactions());
    }

    [Fact]
    public async Task SuccessfulMultiLineSale_CommitsOnceAndCombinesDuplicateSkus()
    {
        var (database, persistence, service) = Arrange(stock: 10);
        var transaction = CreateTransaction();

        await service.CompleteWithRetailItemsAsync(
            transaction,
            [new RetailItemRequest(new Sku(Sku1), 1), new RetailItemRequest(new Sku(Sku1), 2)],
            "Card");

        Assert.Equal(1, persistence.CommitCount);
        Assert.Equal(7, database.StockOf(Branch, Sku1));
        Assert.Equal(TransactionStatus.Paid, transaction.Status);
        Assert.Single(transaction.LineItems);
        Assert.Equal(3, transaction.LineItems[0].Quantity);
        Assert.Single(database.AllTransactions());
    }

    [Fact]
    public async Task ConcurrentChangeToTheLastUnit_RaisesConflictAndSavesNothing()
    {
        var (database, _, service) = Arrange(stock: 1);
        var stockRepository = new InMemoryPersistence(database);
        var first = new TransactionService(
            stockRepository, stockRepository, stockRepository, stockRepository, stockRepository, stockRepository,
            new StubCourierRateService(), new StubCatalogueService(TestProduct()), new StubIdentityVerificationService(),
            new StubEventDispatcher(), new TransactionRules(new Money(1000m), new Money(20000m)));
        await ((IStockRepository)stockRepository).GetManyAsync(new BranchCode(Branch), [new Sku(Sku1)]);

        // A second clerk takes the last unit after the first clerk has read it.
        await service.CompleteWithRetailItemsAsync(CreateTransaction(), [new RetailItemRequest(new Sku(Sku1), 1)], "Cash");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            first.CompleteWithRetailItemsAsync(CreateTransaction(), [new RetailItemRequest(new Sku(Sku1), 1)], "Cash"));
        Assert.Equal(0, database.StockOf(Branch, Sku1));
        Assert.Single(database.AllTransactions());
    }

    [Fact]
    public async Task StaleStockAtSave_RaisesConcurrencyConflict()
    {
        var (database, persistence, service) = Arrange(stock: 5);
        await ((IStockRepository)persistence).GetManyAsync(new BranchCode(Branch), [new Sku(Sku1)]);
        database.SimulateConcurrentStockChange(Branch, Sku1, 4);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.CompleteWithRetailItemsAsync(CreateTransaction(), [new RetailItemRequest(new Sku(Sku1), 1)], "Cash"));
        Assert.Empty(database.AllTransactions());
        Assert.Equal(4, database.StockOf(Branch, Sku1));
    }

    private static CounterCheckoutRequest OneUnitSale() =>
        new("C01", [new RetailItemRequest(new Sku(Sku1), 1)], [], [], false, DocumentType.Passport, null, "Cash");

    [Fact]
    public async Task Checkout_RetriesAConflictAndCommitsOnce()
    {
        var (database, persistence, service) = Arrange(stock: 5);
        persistence.FailNextSaveWith = new ConcurrencyConflictException("Lost the race.");

        await service.CheckoutAsync(Branch, "STF001", OneUnitSale(), Guid.NewGuid());

        Assert.Equal(1, persistence.ResetCount);
        Assert.Equal(1, persistence.CommitCount);
        Assert.Equal(4, database.StockOf(Branch, Sku1));
        Assert.Single(database.AllTransactions());
    }

    [Fact]
    public async Task Checkout_RetryRechecksStockSoItCannotOversell()
    {
        var (database, persistence, service) = Arrange(stock: 1);
        persistence.FailNextSaveWith = new ConcurrencyConflictException("Lost the race.");
        database.SimulateConcurrentStockChange(Branch, Sku1, 0);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CheckoutAsync(Branch, "STF001", OneUnitSale(), Guid.NewGuid()));

        Assert.Equal(0, database.StockOf(Branch, Sku1));
        Assert.Empty(database.AllTransactions());
    }

    [Fact]
    public async Task Checkout_GivesUpAfterBoundedAttempts()
    {
        var (database, _, service) = Arrange(stock: 5);
        var conflicting = new AlwaysConflictingPersistence(database);
        service = new TransactionService(
            conflicting.Inner, conflicting.Inner, conflicting.Inner, conflicting.Inner, conflicting.Inner, conflicting,
            new StubCourierRateService(), new StubCatalogueService(TestProduct()), new StubIdentityVerificationService(),
            new StubEventDispatcher(), new TransactionRules(new Money(1000m), new Money(20000m)));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            service.CheckoutAsync(Branch, "STF001", OneUnitSale(), Guid.NewGuid()));

        Assert.Equal(5, conflicting.Saves);
        Assert.Equal(5, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public void MoneyOrder_RequiresVerificationOnlyAboveTheThreshold()
    {
        var threshold = new Money(1000m);

        Assert.False(new MoneyOrderDetails(new Money(1000m), "A", "B", new Money(10m)).RequiresVerification(threshold));
        Assert.True(new MoneyOrderDetails(new Money(1000.01m), "A", "B", new Money(10m)).RequiresVerification(threshold));
    }

    [Fact]
    public async Task RefundRestocksInOneCommitAndCannotRepeat()
    {
        var (database, persistence, service) = Arrange(stock: 10, manager: true);
        var transaction = CreateTransaction();
        await service.CompleteWithRetailItemsAsync(transaction, [new RetailItemRequest(new Sku(Sku1), 3)], "Card");
        Assert.Equal(7, database.StockOf(Branch, Sku1));

        var commitsBefore = persistence.CommitCount;
        await service.RefundAsync(transaction.Id, "STF009", "Customer returned goods");

        Assert.Equal(commitsBefore + 1, persistence.CommitCount);
        var saved = database.TransactionById(transaction.Id);
        Assert.Equal(TransactionStatus.Refunded, saved.Status);
        Assert.True(saved.InventoryRestocked);
        Assert.Equal(10, database.StockOf(Branch, Sku1));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.RefundAsync(transaction.Id, "STF009", "Duplicate request"));
        Assert.Equal(10, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public async Task VoidRestocksRetailStock()
    {
        var (database, _, service) = Arrange(stock: 6, manager: true);
        var transaction = CreateTransaction();
        await service.CompleteWithRetailItemsAsync(transaction, [new RetailItemRequest(new Sku(Sku1), 2)], "Cash");

        await service.VoidAsync(transaction.Id, "STF009", "Duplicate sale");

        Assert.Equal(TransactionStatus.Voided, database.TransactionById(transaction.Id).Status);
        Assert.Equal(6, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public async Task VoidByClerk_IsRejectedWithoutCommitting()
    {
        var (database, persistence, service) = Arrange(stock: 6, manager: true);
        var transaction = CreateTransaction();
        await service.CompleteWithRetailItemsAsync(transaction, [new RetailItemRequest(new Sku(Sku1), 2)], "Cash");
        var commitsBefore = persistence.CommitCount;

        await Assert.ThrowsAsync<UnauthorizedDomainActionException>(() =>
            service.VoidAsync(transaction.Id, "STF001", "Not allowed"));

        Assert.Equal(commitsBefore, persistence.CommitCount);
        Assert.Equal(TransactionStatus.Paid, database.TransactionById(transaction.Id).Status);
        Assert.Equal(4, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public async Task StockAdjustment_IsManagerScopedAndCannotOverdraw()
    {
        var (database, persistence, _) = Arrange(stock: 10, manager: true);
        var inventory = new InventoryService(persistence, persistence, new StubEventDispatcher());

        var updated = await inventory.AdjustStockAsync("STF009", new BranchCode(Branch), new Sku(Sku1), -3, StockAdjustmentReason.Damage);

        Assert.Equal(7, updated);
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            inventory.AdjustStockAsync("STF009", new BranchCode(Branch), new Sku(Sku1), -8, StockAdjustmentReason.Damage));
        await Assert.ThrowsAsync<UnauthorizedDomainActionException>(() =>
            inventory.AdjustStockAsync("STF009", new BranchCode("B002"), new Sku(Sku1), 1, StockAdjustmentReason.Replenishment));
        await Assert.ThrowsAsync<UnauthorizedDomainActionException>(() =>
            inventory.AdjustStockAsync("STF001", new BranchCode(Branch), new Sku(Sku1), 1, StockAdjustmentReason.Replenishment));
        Assert.Equal(7, database.StockOf(Branch, Sku1));
    }

    [Fact]
    public async Task Checkout_SupportsSeveralShipmentsAndMoneyOrdersInOneCommit()
    {
        var (_, persistence, service) = Arrange(stock: 10);
        var parcel = new ShippingDetails(true, Weight.FromKilograms(1m), new Dimensions(10, 10, 10), "Zone 1", ServiceLevel.Standard);
        var letter = new ShippingDetails(false, Weight.FromKilograms(0.1m), default, "Zone 2", ServiceLevel.Express);

        var transaction = await service.CheckoutAsync(
            Branch,
            "STF001",
            new CounterCheckoutRequest(
                "C01",
                [new RetailItemRequest(new Sku(Sku1), 1)],
                [parcel, letter],
                [new MoneyOrderRequest(100m, "A", "B"), new MoneyOrderRequest(50m, "C", "D")],
                false,
                DocumentType.Passport,
                null,
                "Card"),
            Guid.NewGuid());

        Assert.Equal(1, persistence.CommitCount);
        Assert.Equal(2, transaction.LineItems.Count(item => item.ItemType == LineItemType.ParcelShipping));
        Assert.Equal(2, transaction.LineItems.Count(item => item.ItemType == LineItemType.MoneyOrder));
        Assert.Equal(new Money(40m), transaction.ServicesTotal);
    }

    [Fact]
    public async Task Checkout_RequiresIdentityWhenAnyMoneyOrderIsHighValue()
    {
        var (_, persistence, service) = Arrange(stock: 10);

        await Assert.ThrowsAsync<DomainValidationException>(() => service.CheckoutAsync(
            Branch,
            "STF001",
            new CounterCheckoutRequest(
                "C01",
                [],
                [],
                [new MoneyOrderRequest(100m, "A", "B"), new MoneyOrderRequest(1500m, "C", "D")],
                false,
                DocumentType.Passport,
                null,
                "Cash"),
            Guid.NewGuid()));
        Assert.Equal(0, persistence.CommitCount);
    }

    [Fact]
    public async Task Checkout_MoneyOrderExactlyAtTheThreshold_NeedsNoIdentity()
    {
        var (_, persistence, service) = Arrange(stock: 10);

        var transaction = await service.CheckoutAsync(Branch, "STF001", MoneyOrderCheckout(1000m), Guid.NewGuid());

        Assert.Equal(TransactionStatus.Paid, transaction.Status);
        Assert.Equal(1, persistence.CommitCount);
    }

    [Fact]
    public async Task Checkout_MoneyOrderOneCentAboveTheThreshold_RequiresIdentity()
    {
        var (_, persistence, service) = Arrange(stock: 10);

        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CheckoutAsync(Branch, "STF001", MoneyOrderCheckout(1000.01m), Guid.NewGuid()));
        Assert.Equal(0, persistence.CommitCount);
    }

    private static CounterCheckoutRequest MoneyOrderCheckout(decimal amount) => new(
        "C01",
        [],
        [],
        [new MoneyOrderRequest(amount, "A", "B")],
        false,
        DocumentType.Passport,
        null,
        "Cash");

    private static (InMemoryDatabase Database, InMemoryPersistence Persistence, TransactionService Service) Arrange(int stock, bool manager = false)
    {
        var database = InMemoryDatabase.Empty();
        database.AddBranch(new Branch
        {
            Id = new BranchCode(Branch),
            Name = "Test branch",
            Region = "NSW",
            TimeZoneId = "UTC",
            OpenTime = TimeSpan.Zero,
            CloseTime = new TimeSpan(23, 59, 59),
            CounterTerminals = ["C01"]
        });
        database.AddStaff(new Staff
        {
            Id = "STF001",
            Name = "Test Clerk",
            Email = "clerk@example.test",
            Role = StaffRole.CounterClerk,
            HomeBranchCode = new BranchCode(Branch)
        });
        if (manager)
        {
            database.AddStaff(new Staff
            {
                Id = "STF009",
                Name = "Test Manager",
                Email = "manager@example.test",
                Role = StaffRole.BranchManager,
                HomeBranchCode = new BranchCode(Branch)
            });
        }

        var row = new BranchStock { Id = Guid.NewGuid(), BranchCode = new BranchCode(Branch), ProductSku = new Sku(Sku1) };
        row.AddStock(stock);
        database.AddStock(row);
        database.AddProduct(TestProduct());

        var persistence = new InMemoryPersistence(database);
        var service = new TransactionService(
            persistence, persistence, persistence, persistence, persistence, persistence,
            new StubCourierRateService(), new StubCatalogueService(TestProduct()), new StubIdentityVerificationService(),
            new StubEventDispatcher(), new TransactionRules(new Money(1000m), new Money(20000m)));
        return (database, persistence, service);
    }

    private static Product TestProduct() => new()
    {
        Id = new Sku(Sku1),
        Name = "Shipping box",
        Category = "Packaging",
        UnitPrice = new Money(5m),
        TaxCategory = TaxCategory.StandardGst
    };

    private static Transaction CreateTransaction() => new()
    {
        Id = Guid.NewGuid(),
        BranchCode = new BranchCode(Branch),
        CounterId = "C01",
        ClerkStaffId = "STF001"
    };

    private sealed class AlwaysConflictingPersistence(InMemoryDatabase database) : IUnitOfWork
    {
        public InMemoryPersistence Inner { get; private set; } = new(database);
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            throw new ConcurrencyConflictException("Lost the race.");
        }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) => work(ct);

        public void DiscardPendingChanges() => Inner.DiscardPendingChanges();
    }

    private sealed class StubCatalogueService(Product product) : ICatalogueService
    {
        public ValueTask<IReadOnlyList<Product>> GetProductsAsync(CancellationToken ct = default) =>
            ValueTask.FromResult<IReadOnlyList<Product>>([product]);

        public ValueTask<Product?> GetProductBySkuAsync(Sku sku, CancellationToken ct = default) =>
            ValueTask.FromResult<Product?>(product.Id == sku ? product : null);
    }

    private sealed class StubCourierRateService : ICourierRateService
    {
        public Task<Money> GetEstimatedCourierRateAsync(ShippingDetails details, CancellationToken ct = default) =>
            Task.FromResult(new Money(10m));
    }

    private sealed class StubIdentityVerificationService : IIdentityVerificationService
    {
        public Task<IdVerificationDetails> VerifyAsync(
            DocumentType documentType,
            string documentNumber,
            CancellationToken ct = default) =>
            Task.FromResult(IdVerificationDetails.FromDocumentNumber(documentType, documentNumber, true));
    }

    private sealed class StubEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct = default) => Task.CompletedTask;
    }
}
