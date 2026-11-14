using System.Text.Json;
using ApexPost.Application.Interfaces;
using ApexPost.Application.Queries;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Testing.CounterPlatform;

// A test double, not a database: it stands in for the persistence interfaces so middleware, MVC and service
// tests can run anywhere. Tests in SqlServerIntegrationTests cover real translation, constraints and rowversion.
public sealed class InMemoryDatabase
{
    private readonly object _gate = new();
    internal readonly Dictionary<BranchCode, Branch> Branches = [];
    internal readonly Dictionary<string, Staff> Staff = new(StringComparer.Ordinal);
    internal readonly Dictionary<Sku, Product> Products = [];
    internal readonly Dictionary<(BranchCode, Sku), StockRow> Stock = [];
    internal readonly Dictionary<Guid, TransactionRow> Transactions = [];
    internal readonly List<ShippingZone> Zones = [];

    internal sealed record StockRow(string Json, long Version);
    internal sealed record TransactionRow(string Json, long Version);

    public static InMemoryDatabase Empty() => new();

    public static InMemoryDatabase FromSeed()
    {
        var database = new InMemoryDatabase();
        foreach (var branch in ReadSeed<Branch>("branch.json"))
        {
            database.AddBranch(new Branch
            {
                Id = branch.Id,
                Name = branch.Name,
                Region = branch.Region,
                TimeZoneId = "UTC",
                OpenTime = TimeSpan.Zero,
                CloseTime = new TimeSpan(23, 59, 59),
                IsActive = branch.IsActive,
                CounterTerminals = branch.CounterTerminals
            });
        }

        foreach (var person in ReadSeed<Staff>("staff.json"))
        {
            database.AddStaff(person);
        }

        foreach (var product in ReadSeed<Product>("product.json"))
        {
            database.AddProduct(product);
        }

        foreach (var stock in ReadSeed<BranchStock>("branchstock.json"))
        {
            database.AddStock(stock);
        }

        foreach (var transaction in ReadSeed<Transaction>("transaction.json"))
        {
            database.Transactions[transaction.Id] = new TransactionRow(Serialize(transaction), 1);
        }

        database.Zones.AddRange(ReadSeed<ShippingZone>("shippingzone.json"));
        return database;
    }

    public void AddBranch(Branch branch) => Branches[branch.Id] = Clone(branch);
    public void AddStaff(Staff person) => Staff[person.Id] = Clone(person);
    public void AddProduct(Product product) => Products[product.Id] = Clone(product);
    public void AddStock(BranchStock stock) => Stock[(stock.BranchCode, stock.ProductSku)] = new StockRow(Serialize(stock), 1);

    public int StockOf(string branchCode, string sku)
    {
        lock (_gate)
        {
            return Deserialize<BranchStock>(Stock[(new BranchCode(branchCode), new Sku(sku))].Json).QuantityOnHand;
        }
    }

    public Transaction TransactionById(Guid id)
    {
        lock (_gate)
        {
            return Deserialize<Transaction>(Transactions[id].Json);
        }
    }

    public IReadOnlyList<Transaction> AllTransactions()
    {
        lock (_gate)
        {
            return Transactions.Values.Select(row => Deserialize<Transaction>(row.Json)).ToList();
        }
    }

    public IReadOnlyList<Staff> AllStaff()
    {
        lock (_gate)
        {
            return Staff.Values.Select(Clone).ToList();
        }
    }

    public Staff StaffById(string id)
    {
        lock (_gate)
        {
            return Clone(Staff[id]);
        }
    }

    public Branch BranchByCode(string code)
    {
        lock (_gate)
        {
            return Clone(Branches[new BranchCode(code)]);
        }
    }

    public Product ProductBySku(string sku)
    {
        lock (_gate)
        {
            return Clone(Products[new Sku(sku)]);
        }
    }

    // Lets a test change a stock row behind a scope's back, as a second clerk would.
    public void SimulateConcurrentStockChange(string branchCode, string sku, int newQuantity)
    {
        lock (_gate)
        {
            var key = (new BranchCode(branchCode), new Sku(sku));
            var stock = Deserialize<BranchStock>(Stock[key].Json);
            ApplyDelta(stock, newQuantity - stock.QuantityOnHand);
            Stock[key] = new StockRow(Serialize(stock), Stock[key].Version + 1);
        }
    }

    internal T Locked<T>(Func<T> read)
    {
        lock (_gate)
        {
            return read();
        }
    }

    internal void Locked(Action write)
    {
        lock (_gate)
        {
            write();
        }
    }

    internal static T Clone<T>(T value) => Deserialize<T>(Serialize(value));
    // Stands in for usp_AdjustStock, which holds this rule in production.
    internal static void ApplyDelta(BranchStock stock, int delta)
    {
        if (delta == 0)
        {
            throw new DomainValidationException("A stock adjustment must change inventory.");
        }

        if (delta > 0)
        {
            stock.AddStock(delta);
        }
        else
        {
            stock.ReduceStock(-delta);
        }
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    internal static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json)!;

    private static List<T> ReadSeed<T>(string fileName) =>
        JsonSerializer.Deserialize<List<T>>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", fileName)),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}

// Implements every persistence interface with the same staging rules as EF Core: reads are copies,
// nothing is visible to others until SaveChangesAsync, and a stale version raises a concurrency conflict.
public sealed class InMemoryPersistence(InMemoryDatabase database) :
    IUnitOfWork,
    IBranchRepository,
    IStaffRepository,
    IProductRepository,
    IStockRepository,
    ICounterTransactionRepository,
    IShippingZoneRepository,
    IStockAdjustmentCommand,
    IPriceRevisionCommand,
    IDashboardQueries,
    IReportQueries,
    IScreenQueries
{
    private readonly Dictionary<(BranchCode, Sku), (BranchStock Stock, long Version)> _trackedStock = [];
    private readonly Dictionary<Guid, (Transaction Transaction, long Version)> _trackedTransactions = [];
    private readonly List<Action> _staged = [];

    public int CommitCount { get; private set; }
    public int ResetCount { get; private set; }
    public Exception? FailNextSaveWith { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (FailNextSaveWith is { } failure)
        {
            FailNextSaveWith = null;
            throw failure;
        }

        database.Locked(() =>
        {
            foreach (var (key, tracked) in _trackedStock)
            {
                var current = database.Stock.GetValueOrDefault(key);
                if (current is null || current.Version != tracked.Version)
                {
                    throw new ConcurrencyConflictException("This stock record was changed by someone else at the same time.");
                }
            }

            foreach (var (id, tracked) in _trackedTransactions)
            {
                var current = database.Transactions.GetValueOrDefault(id);
                if (current is null || current.Version != tracked.Version)
                {
                    throw new ConcurrencyConflictException("This transaction was changed by someone else at the same time.");
                }
            }

            foreach (var (key, tracked) in _trackedStock.ToList())
            {
                database.Stock[key] = new InMemoryDatabase.StockRow(InMemoryDatabase.Serialize(tracked.Stock), tracked.Version + 1);
                _trackedStock[key] = (tracked.Stock, tracked.Version + 1);
            }

            foreach (var (id, tracked) in _trackedTransactions.ToList())
            {
                database.Transactions[id] = new InMemoryDatabase.TransactionRow(InMemoryDatabase.Serialize(tracked.Transaction), tracked.Version + 1);
                _trackedTransactions[id] = (tracked.Transaction, tracked.Version + 1);
            }

            foreach (var stage in _staged)
            {
                stage();
            }

            _staged.Clear();
        });
        CommitCount++;
        return Task.FromResult(1);
    }

    public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) => work(ct);

    public void DiscardPendingChanges()
    {
        _trackedStock.Clear();
        _trackedTransactions.Clear();
        _staged.Clear();
        ResetCount++;
    }

    Task<Branch?> IBranchRepository.GetByCodeAsync(BranchCode code, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Branches.TryGetValue(code, out var branch) ? InMemoryDatabase.Clone(branch) : null));

    Task<IReadOnlyList<Branch>> IBranchRepository.GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Branch>>(database.Locked(() => database.Branches.Values.Select(InMemoryDatabase.Clone).ToList()));

    void IBranchRepository.Add(Branch branch) => _staged.Add(() => database.Branches[branch.Id] = InMemoryDatabase.Clone(branch));
    void IBranchRepository.Update(Branch branch) => _staged.Add(() => database.Branches[branch.Id] = InMemoryDatabase.Clone(branch));

    Task<Staff?> IStaffRepository.GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Staff.TryGetValue(id, out var staff) ? InMemoryDatabase.Clone(staff) : null));

    Task<Staff?> IStaffRepository.GetByEmailAsync(string email, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Staff.Values
            .Where(staff => staff.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
            .Select(InMemoryDatabase.Clone)
            .FirstOrDefault()));

    Task<bool> IStaffRepository.EmailExistsAsync(string email, string? exceptStaffId, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Staff.Values.Any(staff =>
            staff.Email.Equals(email, StringComparison.OrdinalIgnoreCase) && staff.Id != exceptStaffId)));

    Task<IReadOnlyList<string>> IStaffRepository.GetAllIdsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(database.Locked(() => database.Staff.Keys.ToList()));

    void IStaffRepository.Add(Staff staff) => _staged.Add(() =>
    {
        if (database.Staff.ContainsKey(staff.Id) || database.Staff.Values.Any(person => person.Email.Equals(staff.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateValueException("A record with the same unique value already exists.");
        }

        database.Staff[staff.Id] = InMemoryDatabase.Clone(staff);
    });

    void IStaffRepository.Update(Staff staff) => _staged.Add(() => database.Staff[staff.Id] = InMemoryDatabase.Clone(staff));

    Task<Product?> IProductRepository.GetBySkuAsync(Sku sku, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Products.TryGetValue(sku, out var product) ? InMemoryDatabase.Clone(product) : null));

    Task<IReadOnlyList<Product>> IProductRepository.GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Product>>(database.Locked(() => database.Products.Values.Select(InMemoryDatabase.Clone).ToList()));

    void IProductRepository.Add(Product product) => _staged.Add(() => database.Products[product.Id] = InMemoryDatabase.Clone(product));
    void IProductRepository.Update(Product product) => _staged.Add(() => database.Products[product.Id] = InMemoryDatabase.Clone(product));

    async Task<IReadOnlyList<BranchStock>> IStockRepository.GetManyAsync(BranchCode branchCode, IReadOnlyCollection<Sku> skus, CancellationToken ct)
    {
        var stocks = new List<BranchStock>();
        foreach (var sku in skus)
        {
            if (await GetStockAsync(branchCode, sku) is { } stock)
            {
                stocks.Add(stock);
            }
        }

        return stocks;
    }

    private Task<BranchStock?> GetStockAsync(BranchCode branchCode, Sku sku)
    {
        var key = (branchCode, sku);
        if (_trackedStock.TryGetValue(key, out var tracked))
        {
            return Task.FromResult<BranchStock?>(tracked.Stock);
        }

        return Task.FromResult(database.Locked(() =>
        {
            if (!database.Stock.TryGetValue(key, out var row))
            {
                return (BranchStock?)null;
            }

            var stock = InMemoryDatabase.Deserialize<BranchStock>(row.Json);
            _trackedStock[key] = (stock, row.Version);
            return stock;
        }));
    }

    void IStockRepository.Add(BranchStock stock) => _staged.Add(() => database.AddStock(stock));

    Task<Transaction?> ICounterTransactionRepository.GetByIdAsync(Guid id, CancellationToken ct)
    {
        if (_trackedTransactions.TryGetValue(id, out var tracked))
        {
            return Task.FromResult<Transaction?>(tracked.Transaction);
        }

        return Task.FromResult(database.Locked(() =>
        {
            if (!database.Transactions.TryGetValue(id, out var row))
            {
                return (Transaction?)null;
            }

            var transaction = InMemoryDatabase.Deserialize<Transaction>(row.Json);
            _trackedTransactions[id] = (transaction, row.Version);
            return transaction;
        }));
    }

    void ICounterTransactionRepository.Add(Transaction transaction) =>
        _staged.Add(() => database.Transactions[transaction.Id] = new InMemoryDatabase.TransactionRow(InMemoryDatabase.Serialize(transaction), 1));

    void ICounterTransactionRepository.Update(Transaction transaction)
    {
    }

    Task<IReadOnlyList<ShippingZone>> IShippingZoneRepository.GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ShippingZone>>(database.Locked(() => database.Zones.ToList()));

    Task<int> IStockAdjustmentCommand.AdjustAsync(BranchCode branchCode, Sku sku, int delta, StockAdjustmentReason reason, string changedBy, CancellationToken ct) =>
        Task.FromResult(database.Locked(() =>
        {
            var key = (branchCode, sku);
            if (!database.Stock.TryGetValue(key, out var row))
            {
                throw new KeyNotFoundException("This product is not stocked at this branch.");
            }

            var stock = InMemoryDatabase.Deserialize<BranchStock>(row.Json);
            InMemoryDatabase.ApplyDelta(stock, delta);
            database.Stock[key] = new InMemoryDatabase.StockRow(InMemoryDatabase.Serialize(stock), row.Version + 1);
            return stock.QuantityOnHand;
        }));

    Task<int> IPriceRevisionCommand.ReviseByCategoryAsync(string category, decimal percentChange, CancellationToken ct) =>
        Task.FromResult(database.Locked(() =>
        {
            var matches = database.Products.Values.Where(product => product.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var product in matches)
            {
                database.Products[product.Id] = new Product
                {
                    Id = product.Id,
                    Name = product.Name,
                    Category = product.Category,
                    TaxCategory = product.TaxCategory,
                    UnitPrice = new Money(product.UnitPrice.Amount * (1m + (percentChange / 100m)))
                };
            }

            return matches.Count;
        }));

    Task<DashboardCountsDto> IDashboardQueries.GetCountsAsync(string? branchCode, int lowStockThreshold, CancellationToken ct) =>
        Task.FromResult(database.Locked(() =>
        {
            var branches = database.Branches.Values.Where(branch => branchCode is null || branch.Id.Value == branchCode).OrderBy(branch => branch.Id.Value).ToList();
            return new DashboardCountsDto(
                branches.Select(branch => branch.Id.Value).ToList(),
                database.Products.Count,
                AllTransactions().Count(transaction => branchCode is null || transaction.BranchCode.Value == branchCode),
                branches.Where(branch => !branch.IsActive).Select(branch => branch.Id.Value).ToList(),
                LowStock(branchCode, lowStockThreshold));
        }));

    Task<IReadOnlyList<DailySalesRowDto>> IReportQueries.GetDailyBranchSalesAsync(string? branchCode, DateOnly date, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DailySalesRowDto>>(database.Locked(() => AllTransactions()
            .Where(transaction => transaction.Status == TransactionStatus.Paid
                && (branchCode is null || transaction.BranchCode.Value == branchCode)
                && BusinessDate(transaction) == date)
            .GroupBy(transaction => transaction.BranchCode.Value)
            .OrderBy(group => group.Key)
            .Select(group => new DailySalesRowDto(
                group.Key,
                group.Sum(transaction => transaction.RetailTotal.Amount),
                group.Sum(transaction => transaction.ServicesTotal.Amount)))
            .ToList()));

    Task<DateOnly?> IReportQueries.GetLatestSalesDateAsync(string? branchCode, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => AllTransactions()
            .Where(transaction => transaction.Status == TransactionStatus.Paid && (branchCode is null || transaction.BranchCode.Value == branchCode))
            .Select(transaction => (DateOnly?)BusinessDate(transaction))
            .Max()));

    Task<IReadOnlyList<LowStockRowDto>> IReportQueries.GetLowStockAsync(string? branchCode, int threshold, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LowStockRowDto>>(database.Locked(() => LowStock(branchCode, threshold)));

    Task<IReadOnlyList<ShippingVolumeRowDto>> IReportQueries.GetShippingVolumeAsync(string? branchCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ShippingVolumeRowDto>>(database.Locked(() => AllTransactions()
            .Where(transaction => transaction.Status == TransactionStatus.Paid && (branchCode is null || transaction.BranchCode.Value == branchCode))
            .SelectMany(transaction => transaction.LineItems)
            .Where(line => line.Shipping is not null)
            .GroupBy(line => (line.Shipping!.DestinationZone, line.Shipping.ServiceLevel))
            .OrderBy(group => group.Key.DestinationZone)
            .ThenBy(group => group.Key.ServiceLevel)
            .Select(group => new ShippingVolumeRowDto(group.Key.DestinationZone, group.Key.ServiceLevel, group.Sum(line => line.Quantity)))
            .ToList()));

    Task<IReadOnlyList<MoneyOrderAuditRowDto>> IReportQueries.GetMoneyOrderAuditAsync(string? branchCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MoneyOrderAuditRowDto>>(database.Locked(() => AllTransactions()
            .Where(transaction => branchCode is null || transaction.BranchCode.Value == branchCode)
            .GroupBy(transaction => transaction.BranchCode.Value)
            .OrderBy(group => group.Key)
            .Select(group => new MoneyOrderAuditRowDto(
                group.Key,
                group.SelectMany(transaction => transaction.LineItems).Count(line => line.ItemType == LineItemType.MoneyOrder),
                group.SelectMany(transaction => transaction.LineItems).Count(line => line.ItemType == LineItemType.IdentityVerification)))
            .ToList()));

    Task<IReadOnlyList<CatalogueRowDto>> IScreenQueries.GetBranchCatalogueAsync(string branchCode, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<CatalogueRowDto>>(database.Locked(() => database.Products.Values
            .OrderBy(product => product.Id.Value)
            .Select(product => new CatalogueRowDto(product.Id.Value, product.Name, product.Category, product.UnitPrice.Amount, QuantityOf(branchCode, product.Id)))
            .ToList()));

    Task<IReadOnlyList<ProductRowDto>> IScreenQueries.GetProductsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProductRowDto>>(database.Locked(() => database.Products.Values.OrderBy(product => product.Id.Value).Select(ToRow).ToList()));

    Task<ProductRowDto?> IScreenQueries.GetProductAsync(string sku, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Products.TryGetValue(new Sku(sku), out var product) ? ToRow(product) : null));

    Task<IReadOnlyList<BranchRowDto>> IScreenQueries.GetBranchesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BranchRowDto>>(database.Locked(() => database.Branches.Values.OrderBy(branch => branch.Id.Value).Select(ToRow).ToList()));

    Task<BranchRowDto?> IScreenQueries.GetBranchAsync(string code, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Branches.TryGetValue(new BranchCode(code), out var branch) ? ToRow(branch) : null));

    Task<IReadOnlyList<StaffRowDto>> IScreenQueries.GetStaffAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StaffRowDto>>(database.Locked(() => database.Staff.Values.OrderBy(staff => staff.HomeBranchCode.Value).ThenBy(staff => staff.Name).Select(ToRow).ToList()));

    Task<StaffRowDto?> IScreenQueries.GetStaffMemberAsync(string id, CancellationToken ct) =>
        Task.FromResult(database.Locked(() => database.Staff.TryGetValue(id, out var staff) ? ToRow(staff) : null));

    Task<IReadOnlyList<TransactionRowDto>> IScreenQueries.GetBranchTransactionsAsync(string branchCode, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TransactionRowDto>>(database.Locked(() => AllTransactions()
            .Where(transaction => transaction.BranchCode.Value == branchCode)
            .OrderByDescending(transaction => transaction.CreatedAt)
            .Take(take)
            .Select(transaction => new TransactionRowDto(
                transaction.Id,
                transaction.CreatedAt,
                transaction.CounterId,
                transaction.ClerkStaffId,
                transaction.LineItems.Count,
                transaction.GrandTotal.Amount,
                transaction.PaymentMethod,
                transaction.Status,
                transaction.VoidReason))
            .ToList()));

    Task<CheckoutOptionsDto?> IScreenQueries.GetCheckoutOptionsAsync(string branchCode, CancellationToken ct) =>
        Task.FromResult(database.Locked(() =>
        {
            if (!database.Branches.TryGetValue(new BranchCode(branchCode), out var branch))
            {
                return (CheckoutOptionsDto?)null;
            }

            return new CheckoutOptionsDto(
                branchCode,
                branch.CounterTerminals,
                database.Products.Values
                    .OrderBy(product => product.Id.Value)
                    .Select(product => new CheckoutStockRowDto(product.Id.Value, product.Name, product.UnitPrice.Amount, QuantityOf(branchCode, product.Id)))
                    .ToList());
        }));

    Task<IReadOnlyList<string>> IScreenQueries.GetCategoriesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(ProductCategories.All);

    private List<Transaction> AllTransactions() =>
        database.Transactions.Values.Select(row => InMemoryDatabase.Deserialize<Transaction>(row.Json)).ToList();

    private static DateOnly BusinessDate(Transaction transaction) =>
        transaction.BusinessDate != default ? transaction.BusinessDate : DateOnly.FromDateTime(transaction.CreatedAt.UtcDateTime);

    private List<LowStockRowDto> LowStock(string? branchCode, int threshold) =>
        database.Stock.Values
            .Select(row => InMemoryDatabase.Deserialize<BranchStock>(row.Json))
            .Where(stock => stock.QuantityOnHand <= threshold && (branchCode is null || stock.BranchCode.Value == branchCode))
            .OrderBy(stock => stock.BranchCode.Value)
            .ThenBy(stock => stock.QuantityOnHand)
            .Select(stock => new LowStockRowDto(stock.BranchCode.Value, stock.ProductSku.Value, database.Products[stock.ProductSku].Name, stock.QuantityOnHand))
            .ToList();

    private int QuantityOf(string branchCode, Sku sku) =>
        database.Stock.TryGetValue((new BranchCode(branchCode), sku), out var row)
            ? InMemoryDatabase.Deserialize<BranchStock>(row.Json).QuantityOnHand
            : 0;

    private static ProductRowDto ToRow(Product product) => new(product.Id.Value, product.Name, product.Category, product.UnitPrice.Amount, product.TaxCategory);

    private static BranchRowDto ToRow(Branch branch) => new(branch.Id.Value, branch.Name, branch.Region, branch.OpenTime, branch.CloseTime, branch.TimeZoneId, branch.IsActive, branch.CounterTerminals);

    private static StaffRowDto ToRow(Staff staff) => new(staff.Id, staff.Name, staff.Email, staff.Role, staff.HomeBranchCode.Value, staff.IsActive);
}

public sealed class InMemoryAuditStore : IAuditStore
{
    private readonly List<AuditEntry> _entries = [];

    public Task AppendAsync(AuditEntry entry, CancellationToken ct = default)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetAllAsync(CancellationToken ct = default)
    {
        lock (_entries)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>(_entries.ToList());
        }
    }
}
