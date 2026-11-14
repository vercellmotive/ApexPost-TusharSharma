using ApexPost.Application.Interfaces;
using ApexPost.Application.Queries;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.Events;
using ApexPost.Domain.ValueObjects;

namespace ApexPost.Application.Services;

public sealed record DashboardSnapshot(
    int BranchCount,
    int ProductCount,
    int TransactionCount,
    IReadOnlyList<string> Alerts);

public sealed record TransactionRules(
    Money HighValueMoneyOrderThreshold,
    Money MaximumMoneyOrderAmount,
    decimal MoneyOrderFee = 10m);
public sealed record RetailItemRequest(Sku Sku, int Quantity);
public sealed record MoneyOrderRequest(decimal Amount, string? Sender, string? Recipient);
public sealed record CounterCheckoutRequest(
    string CounterId,
    IReadOnlyCollection<RetailItemRequest> RetailItems,
    IReadOnlyCollection<ShippingDetails> Shipments,
    IReadOnlyCollection<MoneyOrderRequest> MoneyOrders,
    bool IncludeIdentityVerification,
    DocumentType DocumentType,
    string? DocumentNumber,
    string PaymentMethod);

public sealed record StaffRecordInput(
    string Name,
    string Email,
    StaffRole Role,
    string HomeBranchCode,
    string? Password = null);

public sealed class StaffManagementService
{
    private readonly IStaffRepository _staff;
    private readonly IBranchRepository _branches;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;

    public StaffManagementService(
        IStaffRepository staff,
        IBranchRepository branches,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher)
    {
        _staff = staff;
        _branches = branches;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
    }

    public async Task<Staff> CreateAsync(StaffRecordInput input, CancellationToken ct = default)
    {
        var normalized = NormalizeAndValidate(input);
        await EnsureEmailIsUniqueAsync(normalized.Email, exceptId: null, ct);
        await EnsureBranchExistsAsync(normalized.HomeBranchCode, ct);

        var nextId = (await _staff.GetAllIdsAsync(ct))
            .Select(id => id.StartsWith("STF", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(id.AsSpan(3), out var number) ? number : 0)
            .DefaultIfEmpty()
            .Max() + 1;
        var created = new Staff
        {
            Id = $"STF{nextId:D3}",
            Name = normalized.Name,
            Email = normalized.Email,
            Role = normalized.Role,
            HomeBranchCode = new BranchCode(normalized.HomeBranchCode),
            PasswordHash = HashPassword(input.Password)
        };
        _staff.Add(created);
        await _unitOfWork.SaveChangesAsync(ct);
        return created;
    }

    public async Task<Staff> UpdateAsync(string id, StaffRecordInput input, CancellationToken ct = default)
    {
        var existing = await _staff.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Staff member {id} was not found.");
        var normalized = NormalizeAndValidate(input);
        await EnsureEmailIsUniqueAsync(normalized.Email, id, ct);
        await EnsureBranchExistsAsync(normalized.HomeBranchCode, ct);

        var updated = new Staff
        {
            Id = existing.Id,
            Name = normalized.Name,
            Email = normalized.Email,
            Role = normalized.Role,
            HomeBranchCode = new BranchCode(normalized.HomeBranchCode),
            IsActive = existing.IsActive,
            PasswordHash = HashPassword(input.Password) ?? existing.PasswordHash
        };
        _staff.Update(updated);
        await _unitOfWork.SaveChangesAsync(ct);
        return updated;
    }

    public async Task DeactivateAsync(string id, CancellationToken ct = default)
    {
        var existing = await _staff.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Staff member {id} was not found.");
        existing.Deactivate();
        _staff.Update(existing);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private string? HashPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        if (password.Length < 12)
        {
            throw new DomainValidationException("Passwords must be at least 12 characters long.");
        }

        return _passwordHasher.Hash(password);
    }

    private async Task EnsureBranchExistsAsync(string branchCode, CancellationToken ct)
    {
        if (await _branches.GetByCodeAsync(new BranchCode(branchCode), ct) is null)
        {
            throw new DomainValidationException("Select an existing branch.");
        }
    }

    private async Task EnsureEmailIsUniqueAsync(string email, string? exceptId, CancellationToken ct)
    {
        if (await _staff.EmailExistsAsync(email, exceptId, ct))
        {
            throw new DomainValidationException("A staff member with this email already exists.");
        }
    }

    private static (string Name, string Email, StaffRole Role, string HomeBranchCode) NormalizeAndValidate(
        StaffRecordInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = input.Name.Trim();
        var email = input.Email.Trim();
        var branchCode = input.HomeBranchCode.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(branchCode))
        {
            throw new DomainValidationException("Name, email, and home branch are required.");
        }

        if (!Enum.IsDefined(input.Role))
        {
            throw new DomainValidationException("Select a valid staff role.");
        }

        return (name, email, input.Role, branchCode);
    }
}

public sealed class StaffAuthenticationService
{
    private readonly IStaffRepository _staff;
    private readonly IPasswordHasher _passwordHasher;

    public StaffAuthenticationService(IStaffRepository staff, IPasswordHasher passwordHasher)
    {
        _staff = staff;
        _passwordHasher = passwordHasher;
    }

    public async Task<Staff?> AuthenticateAsync(string email, string password, CancellationToken ct = default)
    {
        var staff = await _staff.GetByEmailAsync(email.Trim(), ct);

        // Verify even for an unknown email so response time does not reveal which accounts exist.
        var passwordMatches = _passwordHasher.Verify(password, staff?.PasswordHash);
        return passwordMatches && staff is { IsActive: true } ? staff : null;
    }
}

public sealed class InventoryService
{
    private readonly IStaffRepository _staff;
    private readonly IStockAdjustmentCommand _adjustStock;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly LowStockRule _lowStockRule;

    public InventoryService(
        IStaffRepository staff,
        IStockAdjustmentCommand adjustStock,
        IDomainEventDispatcher eventDispatcher,
        LowStockRule? lowStockRule = null)
    {
        _staff = staff;
        _adjustStock = adjustStock;
        _eventDispatcher = eventDispatcher;
        _lowStockRule = lowStockRule ?? LowStockRule.Default;
    }

    public async Task<int> AdjustStockAsync(
        string managerId,
        BranchCode branchCode,
        Sku sku,
        int quantityDelta,
        StockAdjustmentReason reason,
        CancellationToken ct = default)
    {
        if (quantityDelta == 0 || quantityDelta == int.MinValue)
        {
            throw new DomainValidationException("The stock adjustment quantity must be non-zero and in range.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new DomainValidationException("A valid stock adjustment reason is required.");
        }

        var manager = await _staff.GetByIdAsync(managerId, ct)
            ?? throw new UnauthorizedDomainActionException("The signed-in staff member is not available.");
        manager.EnsureManages(branchCode);

        var quantityOnHand = await _adjustStock.AdjustAsync(branchCode, sku, quantityDelta, reason, managerId, ct);
        if (_lowStockRule.IsLow(quantityOnHand))
        {
            await _eventDispatcher.DispatchAsync(new StockLowEvent(branchCode, sku, quantityOnHand), ct);
        }

        return quantityOnHand;
    }
}

public sealed class DashboardService
{
    private readonly IDashboardQueries _queries;
    private readonly INotificationService _notifications;
    private readonly LowStockRule _lowStockRule;

    public DashboardService(
        IDashboardQueries queries,
        INotificationService notifications,
        LowStockRule? lowStockRule = null)
    {
        _queries = queries;
        _notifications = notifications;
        _lowStockRule = lowStockRule ?? LowStockRule.Default;
    }

    public async Task<DashboardSnapshot> BuildAsync(string? branchCode = null, CancellationToken ct = default)
    {
        var counts = await _queries.GetCountsAsync(branchCode, _lowStockRule.Threshold, ct);

        // Notifications are in memory, so these lookups can overlap; the DbContext reads above cannot.
        var recentNotifications = await Task.WhenAll(
            counts.BranchCodes.Select(code => _notifications.GetRecentAlertsAsync(new BranchCode(code), ct)));
        var alerts = recentNotifications.SelectMany(items => items).Reverse().ToList();
        alerts.AddRange(counts.InactiveBranches.Select(code => $"Branch {code} is inactive."));
        alerts.AddRange(counts.LowStock.Select(row => $"Low stock: {row.Sku} in {row.BranchCode}."));
        return new DashboardSnapshot(counts.BranchCodes.Count, counts.ProductCount, counts.TransactionCount, alerts);
    }
}

public sealed class TransactionService
{
    private const int MaximumServiceLinesPerKind = 10;
    private const int MaximumSaleAttempts = 5;

    private readonly ICounterTransactionRepository _transactions;
    private readonly IBranchRepository _branches;
    private readonly IStaffRepository _staff;
    private readonly IStockRepository _stocks;
    private readonly IStockAdjustmentCommand _adjustStock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICourierRateService _courierRates;
    private readonly ICatalogueService _catalogue;
    private readonly IIdentityVerificationService _identityVerification;
    private readonly IDomainEventDispatcher _eventDispatcher;
    private readonly TransactionRules _rules;
    private readonly LowStockRule _lowStockRule;
    private readonly TimeProvider _clock;
    public Money HighValueMoneyOrderThreshold => _rules.HighValueMoneyOrderThreshold;

    public TransactionService(
        ICounterTransactionRepository transactions,
        IBranchRepository branches,
        IStaffRepository staff,
        IStockRepository stocks,
        IStockAdjustmentCommand adjustStock,
        IUnitOfWork unitOfWork,
        ICourierRateService courierRates,
        ICatalogueService catalogue,
        IIdentityVerificationService identityVerification,
        IDomainEventDispatcher eventDispatcher,
        TransactionRules rules,
        LowStockRule? lowStockRule = null,
        TimeProvider? clock = null)
    {
        _transactions = transactions;
        _branches = branches;
        _staff = staff;
        _stocks = stocks;
        _adjustStock = adjustStock;
        _unitOfWork = unitOfWork;
        _courierRates = courierRates;
        _catalogue = catalogue;
        _identityVerification = identityVerification;
        _eventDispatcher = eventDispatcher;
        _rules = rules;
        _lowStockRule = lowStockRule ?? LowStockRule.Default;

        // The business date and the opening-hours check come from this clock, so tests can fix the time.
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<Transaction> CheckoutAsync(
        string branchCode,
        string clerkId,
        CounterCheckoutRequest request,
        Guid transactionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RetailItems.Count > 20)
        {
            throw new DomainValidationException("A basket can contain at most 20 distinct retail items.");
        }

        if (request.Shipments.Count > MaximumServiceLinesPerKind || request.MoneyOrders.Count > MaximumServiceLinesPerKind)
        {
            throw new DomainValidationException(
                $"A basket can contain at most {MaximumServiceLinesPerKind} shipments and {MaximumServiceLinesPerKind} money orders.");
        }

        foreach (var moneyOrder in request.MoneyOrders)
        {
            if (moneyOrder.Amount <= 0 || moneyOrder.Amount > _rules.MaximumMoneyOrderAmount.Amount)
            {
                throw new DomainValidationException(
                    $"Money order amount must be above zero and at most {_rules.MaximumMoneyOrderAmount.Amount}.");
            }

            if (string.IsNullOrWhiteSpace(moneyOrder.Sender) || string.IsNullOrWhiteSpace(moneyOrder.Recipient))
            {
                throw new DomainValidationException("Sender and recipient are required for a money order.");
            }
        }

        if (request.MoneyOrders.Any(moneyOrder => moneyOrder.Amount > _rules.HighValueMoneyOrderThreshold.Amount)
            && !request.IncludeIdentityVerification)
        {
            throw new DomainValidationException(
                $"Money orders above {_rules.HighValueMoneyOrderThreshold.Amount:C} require identity verification in this transaction.");
        }

        if (request.IncludeIdentityVerification && string.IsNullOrWhiteSpace(request.DocumentNumber))
        {
            throw new DomainValidationException("A document number is required to perform identity verification.");
        }

        if (request.RetailItems.Count == 0
            && request.Shipments.Count == 0
            && request.MoneyOrders.Count == 0
            && !request.IncludeIdentityVerification)
        {
            throw new DomainValidationException("Add at least one retail item or service to the transaction.");
        }

        // Losing a rowversion race does not mean the stock is gone, so the sale is re-run on fresh data.
        // Every attempt re-checks stock, so a retry can never oversell; the last conflict still reaches the clerk.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await CheckoutOnceAsync(branchCode, clerkId, request, transactionId, ct);
            }
            catch (ConcurrencyConflictException) when (attempt < MaximumSaleAttempts)
            {
                _unitOfWork.DiscardPendingChanges();
                await Task.Delay(Random.Shared.Next(10, 50 * attempt), ct);
            }
        }
    }

    private async Task<Transaction> CheckoutOnceAsync(
        string branchCode,
        string clerkId,
        CounterCheckoutRequest request,
        Guid transactionId,
        CancellationToken ct)
    {
        var transaction = await CreateAsync(branchCode, request.CounterId, clerkId, ct, transactionId);

        // One DbContext per request cannot run overlapping queries, so rate lookups run in sequence.
        foreach (var shipment in request.Shipments)
        {
            await AddShippingAsync(transaction, shipment, ct);
        }

        foreach (var moneyOrder in request.MoneyOrders)
        {
            AddMoneyOrder(transaction, new Money(moneyOrder.Amount), moneyOrder.Sender!, moneyOrder.Recipient!);
        }

        if (request.IncludeIdentityVerification)
        {
            var verification = await _identityVerification.VerifyAsync(
                request.DocumentType,
                request.DocumentNumber!,
                ct);
            AddIdentityVerification(transaction, verification);
        }

        await CompleteWithRetailItemsAsync(transaction, request.RetailItems, request.PaymentMethod, ct);
        return transaction;
    }

    public async Task<Transaction> CreateAsync(
        string branchCode,
        string counterId,
        string clerkId,
        CancellationToken ct = default,
        Guid? transactionId = null)
    {
        var selectedBranch = await _branches.GetByCodeAsync(new BranchCode(branchCode), ct)
            ?? throw new KeyNotFoundException("Branch not found.");
        var clerk = await _staff.GetByIdAsync(clerkId, ct)
            ?? throw new UnauthorizedDomainActionException("Staff not found.");

        clerk.EnsureCanTransact(selectedBranch.Id);
        var now = _clock.GetUtcNow();
        selectedBranch.EnsureAcceptsTransaction(now);
        selectedBranch.EnsureHasCounter(counterId);

        return new Transaction
        {
            Id = transactionId ?? Guid.NewGuid(),
            BranchCode = selectedBranch.Id,
            CounterId = counterId,
            ClerkStaffId = clerkId,
            CreatedAt = now,
            BusinessDate = selectedBranch.LocalDate(now)
        };
    }

    // The stock decrement, transaction, lines and payment all commit in the one SaveChangesAsync below.
    public async Task CompleteWithRetailItemsAsync(
        Transaction transaction,
        IReadOnlyCollection<RetailItemRequest> retailItems,
        string paymentMethod,
        CancellationToken ct = default)
    {
        if (transaction.Status != TransactionStatus.Open)
        {
            throw new DomainValidationException("Cannot complete a finalized transaction.");
        }

        ArgumentNullException.ThrowIfNull(retailItems);
        var requestedItems = retailItems
            .GroupBy(item => item.Sku)
            .Select(group => new RetailItemRequest(group.Key, group.Sum(item => item.Quantity)))
            .ToList();
        if (requestedItems.Any(item => item.Quantity <= 0))
        {
            throw new DomainValidationException("Retail item quantities must be greater than zero.");
        }

        var branch = await _branches.GetByCodeAsync(transaction.BranchCode, ct)
            ?? throw new KeyNotFoundException("Branch not found.");
        var clerk = await _staff.GetByIdAsync(transaction.ClerkStaffId, ct)
            ?? throw new UnauthorizedDomainActionException("Staff not found.");
        clerk.EnsureCanTransact(branch.Id);
        branch.EnsureAcceptsTransaction(_clock.GetUtcNow());
        branch.EnsureHasCounter(transaction.CounterId);

        // One query for the whole basket, however many lines it has.
        var stocks = (await _stocks.GetManyAsync(transaction.BranchCode, requestedItems.Select(item => item.Sku).ToList(), ct))
            .ToDictionary(stock => stock.ProductSku);

        foreach (var requestedItem in requestedItems)
        {
            var product = await _catalogue.GetProductBySkuAsync(requestedItem.Sku, ct)
                ?? throw new KeyNotFoundException($"Product {requestedItem.Sku.Value} was not found.");
            product.EnsureSellable();

            var stock = stocks.GetValueOrDefault(requestedItem.Sku)
                ?? throw new DomainValidationException($"Product {requestedItem.Sku.Value} is not stocked at this branch.");
            stock.ReduceStock(requestedItem.Quantity);
            transaction.AddRetailProduct(product, requestedItem.Quantity);
            if (_lowStockRule.IsLow(stock.QuantityOnHand))
            {
                transaction.AddDomainEvent(new StockLowEvent(transaction.BranchCode, requestedItem.Sku, stock.QuantityOnHand));
            }
        }

        transaction.Complete(paymentMethod, _rules.HighValueMoneyOrderThreshold);
        _transactions.Add(transaction);
        await _unitOfWork.SaveChangesAsync(ct);
        await DispatchDomainEventsAsync(transaction, ct);
    }

    public async Task AddShippingAsync(Transaction transaction, ShippingDetails shippingDetails, CancellationToken ct = default)
    {
        var rate = await _courierRates.GetEstimatedCourierRateAsync(shippingDetails, ct);
        transaction.AddShipping(shippingDetails, rate);
    }

    public void AddMoneyOrder(Transaction transaction, Money amount, string sender, string recipient) =>
        transaction.AddMoneyOrder(amount, sender, recipient, new Money(_rules.MoneyOrderFee, amount.Currency));

    public void AddIdentityVerification(Transaction transaction, IdVerificationDetails verification) =>
        transaction.AddIdentityVerification(verification);

    public Task VoidAsync(Guid transactionId, string managerId, string reason, CancellationToken ct = default) =>
        ReverseTransactionAsync(transactionId, managerId, reason, refund: false, ct);

    public Task RefundAsync(Guid transactionId, string managerId, string reason, CancellationToken ct = default) =>
        ReverseTransactionAsync(transactionId, managerId, reason, refund: true, ct);

    // Restocking runs usp_AdjustStock, so every reversal leaves stock history. The procedure and the status change
    // commit together, and rowversion stops a concurrent reversal, which also undoes the restock.
    private async Task ReverseTransactionAsync(
        Guid transactionId,
        string managerId,
        string reason,
        bool refund,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainValidationException("A reason is required for a transaction reversal.");
        }

        Transaction? transaction = null;
        await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // The execution strategy can repeat this whole unit, so each run starts from fresh data.
            _unitOfWork.DiscardPendingChanges();
            transaction = await _transactions.GetByIdAsync(transactionId, token)
                ?? throw new KeyNotFoundException("Transaction not found.");
            var manager = await _staff.GetByIdAsync(managerId, token)
                ?? throw new UnauthorizedDomainActionException("The signed-in staff member is not available.");

            if (refund)
            {
                transaction.Refund(manager, reason);
            }
            else
            {
                transaction.Void(manager, reason);
            }

            // BranchStock first and in SKU order, the same lock order as sales and stock adjustments.
            foreach (var (sku, quantity) in transaction.RetailQuantities().OrderBy(item => item.Sku.Value, StringComparer.Ordinal))
            {
                await _adjustStock.AdjustAsync(transaction.BranchCode, sku, quantity, StockAdjustmentReason.CustomerReturn, managerId, token);
            }

            transaction.MarkReversalInventoryRestocked();
            _transactions.Update(transaction);
            await _unitOfWork.SaveChangesAsync(token);
        }, ct);

        await DispatchDomainEventsAsync(transaction!, ct);
    }

    private async Task DispatchDomainEventsAsync(Transaction transaction, CancellationToken ct)
    {
        foreach (var domainEvent in transaction.DomainEvents)
        {
            await _eventDispatcher.DispatchAsync(domainEvent, ct);
        }

        transaction.ClearDomainEvents();
    }
}
