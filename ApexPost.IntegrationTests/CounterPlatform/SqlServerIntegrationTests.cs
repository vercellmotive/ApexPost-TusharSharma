extern alias web;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ApexPost.Application.Interfaces;
using ApexPost.Application.Queries;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.IntegrationTests.CounterPlatform;

// Set APEXPOST_TEST_SQLSERVER to a server connection string to run these. They create and drop a disposable
// database, and never touch ApexPostDb. Without the variable they are reported as skipped, not passed.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string Variable = "APEXPOST_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"{Variable} is not set, so no SQL Server is available.";
        }
    }
}

public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(SqlServerFactAttribute.Variable);
        if (string.IsNullOrWhiteSpace(server))
        {
            return;
        }

        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = $"ApexPostTest_{Guid.NewGuid():N}" }.ConnectionString;
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        await new DatabaseSeeder(db).SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (ConnectionString.Length > 0)
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    public ApexPostDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(interceptors)
            .Options);
}

public sealed class SqlServerIntegrationTests(SqlServerTestDatabase database) : IClassFixture<SqlServerTestDatabase>
{
    private static readonly BranchCode B001 = new("B001");
    private static readonly Sku Sku1 = new("SKU-001");

    [SqlServerFact]
    public async Task Seed_FreshDatabase_MeetsTheRequiredVolumes()
    {
        // Arrange
        await using var db = database.CreateContext();

        // Act
        var branches = await db.Branches.CountAsync();
        var staff = await db.Staff.CountAsync();
        var products = await db.Products.CountAsync();
        var zones = await db.ShippingZones.CountAsync();
        var transactions = await db.CounterTransactions.CountAsync();

        // Assert
        Assert.True(branches >= 5);
        Assert.True(staff >= 20);
        Assert.True(products >= 30);
        Assert.True(zones >= 10);
        Assert.True(transactions >= 200);
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_TransactionWithEveryLineType_RoundTripsThroughEveryTable()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        await using (var db = database.CreateContext())
        {
            var transaction = new Transaction { Id = id, BranchCode = B001, CounterId = "C01", ClerkStaffId = "STF002", BusinessDate = new DateOnly(2026, 10, 1) };
            transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Tape", LineItemType.RetailProduct, 1, new Money(10m), TaxCategory.StandardGst, Sku1));
            transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Shipping", LineItemType.ParcelShipping, 1, new Money(5m), TaxCategory.Exempt, Shipping: new ShippingDetails(true, Weight.FromKilograms(2m), new Dimensions(3, 4, 5), "Zone 3", ServiceLevel.Express, new Address("1 Test St", "Sydney", "NSW", "2000", "Australia"))));
            transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "ID", LineItemType.IdentityVerification, 1, Money.Zero, TaxCategory.Exempt, IdVerification: IdVerificationDetails.FromDocumentNumber(DocumentType.Passport, "A123456789", true)));
            transaction.Complete("Card", new Money(1000m));
            new CounterTransactionRepository(db).Add(transaction);
            await new UnitOfWork(db).SaveChangesAsync();
        }

        // Assert
        await using (var db = database.CreateContext())
        {
            var restored = await new CounterTransactionRepository(db).GetByIdAsync(id);

            Assert.NotNull(restored);
            Assert.Equal(TransactionStatus.Paid, restored.Status);
            Assert.Equal("Card", restored.PaymentMethod);
            Assert.Equal("Zone 3", restored.LineItems[1].Shipping!.DestinationZone);
            Assert.Equal("Sydney", restored.LineItems[1].Shipping!.Destination!.Value.City);
            Assert.Equal("******6789", restored.LineItems[2].IdVerification!.MaskedDocNumber);
        }
    }

    [SqlServerFact]
    public async Task Shipment_WithOrWithoutDestination_LoadsAddressOnlyWhenPresent()
    {
        // Arrange
        var id = Guid.NewGuid();
        var address = new Address("1 Test St", "Sydney", "NSW", "2000", "Australia");

        // Act
        await using (var db = database.CreateContext())
        {
            var transaction = new Transaction { Id = id, BranchCode = B001, CounterId = "C01", ClerkStaffId = "STF002", BusinessDate = new DateOnly(2026, 10, 2) };
            transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "No address", LineItemType.ParcelShipping, 1, new Money(5m), TaxCategory.Exempt, Shipping: new ShippingDetails(true, Weight.FromKilograms(2m), new Dimensions(3, 4, 5), "Zone 3", ServiceLevel.Express)));
            transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Address", LineItemType.ParcelShipping, 1, new Money(5m), TaxCategory.Exempt, Shipping: new ShippingDetails(true, Weight.FromKilograms(2m), new Dimensions(3, 4, 5), "Zone 3", ServiceLevel.Express, address)));
            transaction.Complete("Card", new Money(1000m));
            new CounterTransactionRepository(db).Add(transaction);
            await new UnitOfWork(db).SaveChangesAsync();
        }

        // Assert
        await using (var db = database.CreateContext())
        {
            var restored = await new CounterTransactionRepository(db).GetByIdAsync(id);

            Assert.Null(restored!.LineItems[0].Shipping!.Destination);
            Assert.Equal(address, restored.LineItems[1].Shipping!.Destination);
        }
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_SaleFailsInTheDatabase_RollsBackStockAndTransactionTogether()
    {
        // Arrange
        await using var db = database.CreateContext();
        var stock = (await new StockRepository(db).GetManyAsync(B001, [Sku1])).Single();
        var before = stock!.QuantityOnHand;
        stock.AddStock(1);
        var transaction = new Transaction { Id = Guid.NewGuid(), BranchCode = B001, CounterId = "NOT-A-COUNTER", ClerkStaffId = "STF002", BusinessDate = new DateOnly(2026, 10, 1) };
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Tape", LineItemType.RetailProduct, 1, new Money(10m), TaxCategory.Exempt, Sku1));
        transaction.Complete("Cash", new Money(1000m));
        new CounterTransactionRepository(db).Add(transaction);

        // Act
        var exception = await Record.ExceptionAsync(() => new UnitOfWork(db).SaveChangesAsync());

        // Assert
        Assert.IsType<DbUpdateException>(exception);
        await using var verify = database.CreateContext();
        Assert.Equal(before, (await verify.BranchStock.AsNoTracking().SingleAsync(row => row.BranchCode == B001 && row.ProductSku == Sku1)).QuantityOnHand);
        Assert.False(await verify.CounterTransactions.AnyAsync(row => row.Id == transaction.Id));
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_TwoClerksSellTheSameUnit_SecondThrowsConcurrencyConflict()
    {
        // Arrange
        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstStock = (await new StockRepository(first).GetManyAsync(B001, [new Sku("SKU-002")])).Single();
        var secondStock = (await new StockRepository(second).GetManyAsync(B001, [new Sku("SKU-002")])).Single();

        firstStock!.AddStock(1);
        await new UnitOfWork(first).SaveChangesAsync();
        secondStock!.AddStock(1);

        // Act
        Func<Task> act = () => new UnitOfWork(second).SaveChangesAsync();

        // Assert
        await Assert.ThrowsAsync<ConcurrencyConflictException>(act);
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_DuplicateStaffEmail_ThrowsDuplicateValue()
    {
        // Arrange
        await using var db = database.CreateContext();
        db.Staff.Add(new Staff { Id = "STF900", Name = "Copy", Email = "alice@apexpost.com", Role = StaffRole.CounterClerk, HomeBranchCode = B001 });

        // Act
        Func<Task> act = () => new UnitOfWork(db).SaveChangesAsync();

        // Assert
        await Assert.ThrowsAsync<DuplicateValueException>(act);
    }

    [SqlServerFact]
    public async Task UniqueConstraints_DuplicateValues_AreRejectedButAStaffEmailCanBeCorrected()
    {
        // Arrange
        await using var db = database.CreateContext();
        var staff = new StaffRepository(db);
        try
        {
            staff.Add(new Staff { Id = "STF901", Name = "Temp", Email = "temp.stf901@apexpost.example", Role = StaffRole.CounterClerk, HomeBranchCode = B001 });
            await new UnitOfWork(db).SaveChangesAsync();
            var current = (await staff.GetByIdAsync("STF901"))!;

            // Act
            var constraints = await db.Database.SqlQuery<string>($"SELECT name AS [Value] FROM sys.key_constraints WHERE type = 'UQ'").ToListAsync();
            var uniqueEmailIndexes = await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'UQ_Staff_Email' AND is_unique = 1").SingleAsync();
            staff.Update(new Staff { Id = current.Id, Name = current.Name, Email = "corrected.stf901@apexpost.example", Role = current.Role, HomeBranchCode = current.HomeBranchCode });
            await new UnitOfWork(db).SaveChangesAsync();
            var savedEmail = await db.Staff.AsNoTracking().Where(item => item.Id == "STF901").Select(item => item.Email).SingleAsync();
            staff.Update(new Staff { Id = current.Id, Name = current.Name, Email = "alice@apexpost.com", Role = current.Role, HomeBranchCode = current.HomeBranchCode });
            var duplicate = await Record.ExceptionAsync(() => new UnitOfWork(db).SaveChangesAsync());

            // Assert
            Assert.Contains("UQ_Branches_BranchCode", constraints);
            Assert.Contains("UQ_Products_Sku", constraints);
            Assert.Equal(1, uniqueEmailIndexes);
            Assert.Equal("corrected.stf901@apexpost.example", savedEmail);
            Assert.IsType<DuplicateValueException>(duplicate);
        }
        finally
        {
            await using var cleanup = database.CreateContext();
            await cleanup.Staff.Where(item => item.Id == "STF901").ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_NegativeStockOrUnmaskedDocument_IsRejectedByCheckConstraints()
    {
        // Arrange
        await using var db = database.CreateContext();
        var verificationsExist = await db.IdentityVerifications.AnyAsync();

        // Act
        var negativeStock = await Record.ExceptionAsync(() =>
            db.Database.ExecuteSqlAsync($"UPDATE dbo.BranchStock SET QuantityOnHand = -1 WHERE BranchCode = {"B001"} AND Sku = {"SKU-001"}"));
        var unmaskedDocument = await Record.ExceptionAsync(() =>
            db.Database.ExecuteSqlAsync($"UPDATE dbo.IdentityVerifications SET MaskedDocumentNumber = {"A123456789"}"));

        // Assert
        Assert.IsType<SqlException>(negativeStock);
        Assert.True(verificationsExist);
        Assert.IsType<SqlException>(unmaskedDocument);
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_NegativeShipmentDimensions_IsRejectedByCheckConstraint()
    {
        // Arrange
        await using var db = database.CreateContext();
        var shipmentsExist = await db.Shipments.AnyAsync();
        FormattableString[] updates =
        [
            $"UPDATE dbo.Shipments SET LengthCm = {-1m}",
            $"UPDATE dbo.Shipments SET WidthCm = {-1m}",
            $"UPDATE dbo.Shipments SET HeightCm = {-1m}"
        ];

        // Act
        var failures = new List<Exception?>();
        foreach (var update in updates)
        {
            failures.Add(await Record.ExceptionAsync(() => db.Database.ExecuteSqlAsync(update)));
        }

        // Assert
        Assert.True(shipmentsExist);
        Assert.All(failures, failure =>
        {
            var sqlError = Assert.IsType<SqlException>(failure);
            Assert.Contains("CK_Shipments_Dimensions", sqlError.Message);
        });
    }

    [SqlServerFact]
    public async Task AdjustAsync_ValidAdjustment_UpdatesStockAndWritesHistory()
    {
        // Arrange
        await using var db = database.CreateContext();
        var before = (await db.BranchStock.AsNoTracking().SingleAsync(row => row.BranchCode == B001 && row.ProductSku == new Sku("SKU-003"))).QuantityOnHand;

        // Act
        var after = await new StockAdjustmentCommand(db).AdjustAsync(B001, new Sku("SKU-003"), 2, StockAdjustmentReason.Replenishment, "STF001");

        // Assert
        Assert.Equal(before + 2, after);
        Assert.True(await db.StockAdjustments.AnyAsync(row => row.BranchCode == B001 && row.Sku == new Sku("SKU-003") && row.Delta == 2 && row.QuantityAfter == after
            && EF.Property<string>(row, AuditColumns.CreatedBy) == "STF001"));
    }

    [SqlServerFact]
    public async Task AdjustAsync_OverdrawOrUnknownStock_ThrowsAndChangesNothing()
    {
        // Arrange
        await using var db = database.CreateContext();
        var command = new StockAdjustmentCommand(db);
        var history = await db.StockAdjustments.CountAsync();

        // Act
        var overdraw = await Record.ExceptionAsync(() => command.AdjustAsync(B001, Sku1, -1000000, StockAdjustmentReason.Damage, "STF001"));
        var unknownStock = await Record.ExceptionAsync(() => command.AdjustAsync(B001, new Sku("SKU-404"), 1, StockAdjustmentReason.Damage, "STF001"));

        // Assert
        Assert.IsType<DomainValidationException>(overdraw);
        Assert.IsType<KeyNotFoundException>(unknownStock);
        Assert.Equal(history, await db.StockAdjustments.CountAsync());
    }

    [SqlServerFact]
    public async Task AdjustAsync_BlankOrUnknownStaffId_ThrowsAndChangesNothing()
    {
        // Arrange
        await using var db = database.CreateContext();
        var command = new StockAdjustmentCommand(db);
        var quantity = (await db.BranchStock.AsNoTracking().SingleAsync(row => row.BranchCode == B001 && row.ProductSku == Sku1)).QuantityOnHand;
        var history = await db.StockAdjustments.CountAsync();

        // Act
        var blankStaff = await Record.ExceptionAsync(() => command.AdjustAsync(B001, Sku1, 1, StockAdjustmentReason.Damage, ""));
        var unknownStaff = await Record.ExceptionAsync(() => command.AdjustAsync(B001, Sku1, 1, StockAdjustmentReason.Damage, "STF999"));

        // Assert
        Assert.All([blankStaff, unknownStaff], failure =>
        {
            var validation = Assert.IsType<DomainValidationException>(failure);
            Assert.Contains("not a known staff member", validation.Message);
        });
        Assert.Equal(quantity, (await db.BranchStock.AsNoTracking().SingleAsync(row => row.BranchCode == B001 && row.ProductSku == Sku1)).QuantityOnHand);
        Assert.Equal(history, await db.StockAdjustments.CountAsync());
    }

    [SqlServerFact]
    public async Task ReportProcedures_LatestSalesDate_MatchTotalsCalculatedFromTheTables()
    {
        // Arrange
        await using var db = database.CreateContext();
        var reports = new ReportQueries(db);

        // Act
        var date = (await reports.GetLatestSalesDateAsync("B001"))!.Value;
        var daily = await reports.GetDailyBranchSalesAsync("B001", date);
        var lowStock = await reports.GetLowStockAsync("B001", 5);
        var allStock = await reports.GetLowStockAsync(null, 1000000);

        // Assert
        var row = Assert.Single(daily);
        var expectedRetail = await db.TransactionLines
            .Where(line => line.LineType == LineItemType.RetailProduct
                && line.Transaction!.BranchCode == B001
                && line.Transaction.BusinessDate == date
                && line.Transaction.Status == TransactionStatus.Paid)
            .SumAsync(line => line.LineTotal);
        Assert.Equal(expectedRetail, row.RetailTotal);
        Assert.All(lowStock, item => Assert.True(item.QuantityOnHand <= 5));
        Assert.All(allStock, item => Assert.False(string.IsNullOrEmpty(item.ProductName)));
    }

    [SqlServerFact]
    public async Task AppRole_ExecutingAsTheAppUser_CanRunTheAppButCannotDeleteOrChangeSchema()
    {
        // Arrange
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "ApexPost.slnx")))
        {
            directory = directory.Parent;
        }

        var script = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "sql", "security", "least_privilege_role.sql"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline).Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await RunAsync(connection, batch);
        }

        await RunAsync(connection, "IF DATABASE_PRINCIPAL_ID('apexpost_test') IS NULL CREATE USER apexpost_test WITHOUT LOGIN; ALTER ROLE apexpost_app ADD MEMBER apexpost_test;");
        string[] allowed =
        [
            "SELECT TOP (1) * FROM dbo.Branches;",
            "EXEC dbo.usp_LowStockReport @BranchCode = 'B001', @Threshold = 5;",
            "EXEC dbo.usp_DailyBranchSales @BranchCode = 'B001', @Date = '20260101';",
            "BEGIN TRAN; UPDATE dbo.BranchStock SET QuantityOnHand = QuantityOnHand WHERE BranchCode = 'B001'; INSERT dbo.AuditEntries (Id, CorrelationId, StaffId, Role, BranchCode, Controller, Action, Parameters, StatusCode, DurationMs) VALUES (NEWID(), 'c', 's', 'r', 'b', 'c', 'a', '{}', 200, 1); ROLLBACK;",
        ];
        string[] forbidden =
        [
            "DELETE FROM dbo.AuditEntries;",
            "UPDATE dbo.AuditEntries SET StatusCode = 0;",
            "DELETE FROM dbo.Products;",
            "ALTER TABLE dbo.Products ADD Hacked int NULL;",
            "CREATE TABLE dbo.Hacked (Id int);",
        ];

        await RunAsync(connection, "EXECUTE AS USER = 'apexpost_test';");
        try
        {
            // Act
            var allowedErrors = await RunEachAsync(connection, allowed);
            var forbiddenErrors = await RunEachAsync(connection, forbidden);

            // Assert
            Assert.All(allowedErrors, Assert.Null);
            Assert.All(forbiddenErrors, error => Assert.Contains(Assert.IsType<SqlException>(error).Number, new[] { 229, 262, 1088, 15151, 3701 }));
        }
        finally
        {
            await RunAsync(connection, "REVERT;");
        }
    }

    [SqlServerFact]
    public async Task ReportingRole_ExecutingAsTheReportUser_CanRunReportsButCannotReadTablesOrChangeStock()
    {
        // Arrange
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory!.FullName, "ApexPost.slnx")))
        {
            directory = directory.Parent;
        }

        var script = await File.ReadAllTextAsync(Path.Combine(directory.FullName, "sql", "security", "reporting_role.sql"));
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline).Where(batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await RunAsync(connection, batch);
        }

        await RunAsync(connection, "IF DATABASE_PRINCIPAL_ID('apexpost_report_test') IS NULL CREATE USER apexpost_report_test WITHOUT LOGIN; ALTER ROLE apexpost_reports ADD MEMBER apexpost_report_test;");
        string[] allowed =
        [
            "EXEC dbo.usp_LowStockReport @BranchCode = 'B001', @Threshold = 5;",
            "EXEC dbo.usp_DailyBranchSales @BranchCode = 'B001', @Date = '20260101';",
        ];
        string[] forbidden =
        [
            "SELECT TOP (1) Sender FROM dbo.MoneyOrders;",
            "SELECT TOP (1) Email FROM dbo.Staff;",
            "SELECT TOP (1) MaskedDocumentNumber FROM dbo.IdentityVerifications;",
            "SELECT TOP (1) LineTotal FROM dbo.TransactionLines;",
            "EXEC dbo.usp_AdjustStock @BranchCode = 'B001', @Sku = 'SKU-001', @Delta = 1, @Reason = 'Damage', @ChangedBy = 'STF001';",
        ];

        await RunAsync(connection, "EXECUTE AS USER = 'apexpost_report_test';");
        try
        {
            // Act
            var allowedErrors = await RunEachAsync(connection, allowed);
            var forbiddenErrors = await RunEachAsync(connection, forbidden);

            // Assert
            Assert.All(allowedErrors, Assert.Null);
            Assert.All(forbiddenErrors, error => Assert.Contains(Assert.IsType<SqlException>(error).Number, new[] { 229, 230 }));
        }
        finally
        {
            await RunAsync(connection, "REVERT;");
        }
    }

    [SqlServerFact]
    public async Task ExecuteNonQueryAsync_OppositeUpdateOrder_DeadlocksWithARetryableRolledBackVictim()
    {
        // Arrange
        const string first = "UPDATE dbo.BranchStock SET QuantityOnHand = QuantityOnHand + 1 WHERE BranchCode = 'B001' AND Sku = 'SKU-001'";
        const string second = "UPDATE dbo.BranchStock SET QuantityOnHand = QuantityOnHand + 1 WHERE BranchCode = 'B001' AND Sku = 'SKU-002'";
        await using var survivor = new SqlConnection(database.ConnectionString);
        await using var victim = new SqlConnection(database.ConnectionString);
        await survivor.OpenAsync();
        await victim.OpenAsync();
        await RunAsync(victim, "SET DEADLOCK_PRIORITY LOW");
        await using var before = database.CreateContext();
        var quantityBefore = await before.BranchStock.AsNoTracking()
            .Where(stock => stock.BranchCode == B001 && stock.ProductSku == new Sku("SKU-002"))
            .Select(stock => stock.QuantityOnHand)
            .SingleAsync();

        await using var survivorTransaction = (SqlTransaction)await survivor.BeginTransactionAsync();
        await using var victimTransaction = (SqlTransaction)await victim.BeginTransactionAsync();
        await new SqlCommand(first, survivor, survivorTransaction).ExecuteNonQueryAsync();
        await new SqlCommand(second, victim, victimTransaction).ExecuteNonQueryAsync();
        var survivorWaits = new SqlCommand(second, survivor, survivorTransaction).ExecuteNonQueryAsync();

        // Act
        // The victim now waits for the survivor's row while the survivor waits for the victim's: a deadlock.
        var exception = await Record.ExceptionAsync(() => new SqlCommand(first, victim, victimTransaction).ExecuteNonQueryAsync());
        await survivorWaits;
        await survivorTransaction.RollbackAsync();

        // Assert
        var failure = Assert.IsType<SqlException>(exception);
        Assert.Equal(1205, failure.Number);
        await using var after = database.CreateContext();
        Assert.True(new RetryProbe(after).Retries(failure));
        Assert.Equal(quantityBefore, await after.BranchStock.AsNoTracking()
            .Where(stock => stock.BranchCode == B001 && stock.ProductSku == new Sku("SKU-002"))
            .Select(stock => stock.QuantityOnHand)
            .SingleAsync());
    }

    // The same strategy EnableRetryOnFailure installs, with its retry decision made visible to the test.
    private sealed class RetryProbe(DbContext context) : SqlServerRetryingExecutionStrategy(context)
    {
        public bool Retries(Exception exception) => ShouldRetryOn(exception);
    }

    private static async Task RunAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    // Runs each statement in turn and records what it threw, or null, so the test can assert on every outcome.
    private static async Task<List<Exception?>> RunEachAsync(SqlConnection connection, IEnumerable<string> statements)
    {
        var errors = new List<Exception?>();
        foreach (var sql in statements)
        {
            errors.Add(await Record.ExceptionAsync(() => RunAsync(connection, sql)));
        }

        return errors;
    }

    [SqlServerFact]
    public async Task ReviseByCategoryAsync_OneCategoryOnSqlServer_UsesOneSetBasedStatement()
    {
        // Arrange
        var counter = new CommandCounter();
        await using var db = database.CreateContext(counter);
        var before = await db.Products.AsNoTracking().Where(product => product.CategoryId == 3).Select(product => product.UnitPrice).ToListAsync();
        counter.Reset();

        // Act
        var revised = await new PriceRevisionCommand(db).ReviseByCategoryAsync(ProductCategories.OfficeSupplies, 10m);

        // Assert
        Assert.Equal(before.Count, revised);
        Assert.Equal(1, counter.Commands);
    }

    [SqlServerFact]
    public async Task PriceRevision_ThatRoundsAPriceToZero_IsRejectedAndChangesNothing()
    {
        // Arrange
        await using var db = database.CreateContext();
        var before = await db.Products.AsNoTracking().OrderBy(product => product.Id).Select(product => product.UnitPrice).ToListAsync();

        // Act
        var exception = await Record.ExceptionAsync(() =>
            new PriceRevisionCommand(db).ReviseByCategoryAsync(ProductCategories.OfficeSupplies, -99.99m));

        // Assert
        Assert.IsType<DomainValidationException>(exception);
        Assert.Equal(before, await db.Products.AsNoTracking().OrderBy(product => product.Id).Select(product => product.UnitPrice).ToListAsync());
    }

    [SqlServerFact]
    public async Task SaveChangesAsync_FreeIdentityCheck_RecordsNoPaymentAndRejectsAZeroPayment()
    {
        // Arrange
        var id = Guid.NewGuid();
        var transaction = new Transaction { Id = id, BranchCode = B001, CounterId = "C01", ClerkStaffId = "STF002", BusinessDate = new DateOnly(2026, 10, 2) };
        transaction.AddIdentityVerification(IdVerificationDetails.FromDocumentNumber(DocumentType.Passport, "PA1234567", true));
        transaction.Complete("Cash", new Money(1000m));
        await using var saleContext = database.CreateContext();
        await using var db = database.CreateContext();

        // Act
        // Save the free identity check, void it in a fresh context, then try to record a zero payment against it.
        new CounterTransactionRepository(saleContext).Add(transaction);
        await new UnitOfWork(saleContext).SaveChangesAsync();
        var paymentsAfterSale = await db.Payments.CountAsync(payment => payment.TransactionId == id);
        var restored = (await new CounterTransactionRepository(db).GetByIdAsync(id))!;
        var statusAfterSale = restored.Status;
        restored.Void((await new StaffRepository(db).GetByIdAsync("STF001"))!, "Test void");
        new CounterTransactionRepository(db).Update(restored);
        await new UnitOfWork(db).SaveChangesAsync();
        var paymentsAfterVoid = await db.Payments.CountAsync(payment => payment.TransactionId == id);
        var statusAfterVoid = (await db.CounterTransactions.AsNoTracking().SingleAsync(row => row.Id == id)).Status;
        var zeroPayment = await Record.ExceptionAsync(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO dbo.Payments (Id, TransactionId, Kind, Method, Amount) VALUES ({Guid.NewGuid()}, {id}, {"Sale"}, {"Cash"}, {0m})"));

        // Assert
        Assert.Equal(0, paymentsAfterSale);
        Assert.Equal(TransactionStatus.Paid, statusAfterSale);
        Assert.Equal(0, paymentsAfterVoid);
        Assert.Equal(TransactionStatus.Voided, statusAfterVoid);
        var failure = Assert.IsType<SqlException>(zeroPayment);
        Assert.Contains("CK_Payments_Amount", failure.Message);
    }

    [SqlServerFact]
    public async Task GetBranchTransactionsAsync_TwoHundredRows_DoesNotIssueOneQueryPerRow()
    {
        // Arrange
        var counter = new CommandCounter();
        await using var db = database.CreateContext(counter);
        var queries = new ScreenQueries(db);

        // Act
        // Each screen query is counted on its own.
        counter.Reset();
        var transactions = await queries.GetBranchTransactionsAsync("B001", 200);
        var transactionCommands = counter.Commands;
        counter.Reset();
        var catalogue = await queries.GetBranchCatalogueAsync("B001");
        var catalogueCommands = counter.Commands;
        counter.Reset();
        await queries.GetBranchesAsync();
        var branchCommands = counter.Commands;

        // Assert
        Assert.True(transactions.Count > 1);
        Assert.Equal(1, transactionCommands);
        Assert.True(catalogue.Count >= 30);
        Assert.Equal(1, catalogueCommands);
        Assert.True(branchCommands <= 2, $"Expected at most 2 commands but saw {branchCommands}.");
    }

    [SqlServerFact]
    public async Task AppendAsync_NewEntry_IsWrittenToAuditEntries()
    {
        // Arrange
        using var factory = new SqlServerWebFactory(database.ConnectionString);
        var store = (IAuditStore)factory.Services.GetService(typeof(IAuditStore))!;
        var id = Guid.NewGuid();

        // Act
        await store.AppendAsync(new AuditEntry(id, "corr", "STF001", "BranchManager", "B001", "CounterController", "Void", "{}", 302, 5, new DateTimeOffset(2026, 11, 10, 3, 0, 0, TimeSpan.Zero)));

        // Assert
        Assert.Contains(await store.GetAllAsync(), entry => entry.Id == id);
    }

    [SqlServerFact]
    public async Task CounterCreateAndVoid_OnSqlServer_PersistsSaleNeedsManagerAndReturnsProblemDetails()
    {
        // Arrange
        using var factory = new SqlServerWebFactory(database.ConnectionString);
        await using (var db = database.CreateContext())
        {
            await db.Branches.ExecuteUpdateAsync(setters => setters
                .SetProperty(branch => branch.TimeZoneId, "UTC")
                .SetProperty(branch => branch.OpenTime, TimeSpan.Zero)
                .SetProperty(branch => branch.CloseTime, new TimeSpan(23, 59, 59)));
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.3.1");
        await LoginAsync(client, "ben@apexpost.com", DemoStaff.PasswordFor("ben@apexpost.com"));
        var token = await TokenAsync(client, "/Counter");

        // Act
        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["RetailItems[0].ProductSku"] = "SKU-004",
            ["RetailItems[0].Quantity"] = "1",
            ["PaymentMethod"] = "Card",
            ["__RequestVerificationToken"] = token
        }));
        await using var verify = database.CreateContext();
        var saved = await verify.CounterTransactions.AsNoTracking().OrderByDescending(row => row.OccurredAtUtc).FirstAsync();
        var denied = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["transactionId"] = saved.Id.ToString(),
            ["reason"] = "Clerk attempt",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        Assert.Equal(TransactionStatus.Paid, saved.Status);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains(await factory.ReadAuditAsync(), entry => entry.Action == "Create" && entry.StatusCode == 302);
    }

    [SqlServerFact]
    public async Task Mvc_SaleThatLosesEveryRowVersionRace_Returns409AndChangesNothing()
    {
        // Arrange
        var sku = new Sku("SKU-007");
        var rival = new RivalWriter(database.ConnectionString, "[BranchStock] SET", "UPDATE dbo.BranchStock SET ModifiedAtUtc = SYSUTCDATETIME() WHERE BranchCode = 'B001' AND Sku = 'SKU-007'");
        using var factory = new SqlServerWebFactory(database.ConnectionString, rival);
        await OpenAllBranchesAsync();
        var quantityBefore = await StockQuantityAsync(sku);
        var salesBefore = await SalesCountAsync();
        var (client, token) = await SignInManagerAsync(factory, "127.0.0.61");

        // Act
        var sale = await client.PostAsync("/Counter/Create", SaleForm(sku, token));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, sale.StatusCode);
        Assert.Equal("application/problem+json", sale.Content.Headers.ContentType?.MediaType);
        Assert.Equal(5, rival.Writes);
        Assert.Equal(quantityBefore, await StockQuantityAsync(sku));
        Assert.Equal(salesBefore, await SalesCountAsync());
    }

    [SqlServerFact]
    public async Task Mvc_NotFoundAndProcedureErrors_AreProblemDetailsAndChangeNothing()
    {
        // Arrange
        var sku = new Sku("SKU-008");
        using var factory = new SqlServerWebFactory(database.ConnectionString);
        await OpenAllBranchesAsync();
        var quantityBefore = await StockQuantityAsync(sku);
        var (client, token) = await SignInManagerAsync(factory, "127.0.3.5");

        // Act
        var missingSale = await client.PostAsync("/Counter/Void", VoidForm(Guid.NewGuid(), token));
        var overdraw = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = sku.Value,
            ["quantityDelta"] = "-1000000",
            ["reasonCode"] = "CountCorrection",
            ["__RequestVerificationToken"] = token
        }));
        var unknownProduct = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-404",
            ["quantityDelta"] = "1",
            ["reasonCode"] = "CountCorrection",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, missingSale.StatusCode);
        Assert.Equal("application/problem+json", missingSale.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, overdraw.StatusCode);
        Assert.Equal("application/problem+json", overdraw.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, unknownProduct.StatusCode);
        Assert.Equal("application/problem+json", unknownProduct.Content.Headers.ContentType?.MediaType);

        Assert.Equal(quantityBefore, await StockQuantityAsync(sku));
    }

    [SqlServerFact]
    public async Task Mvc_SaleOfSeveralProducts_ReadsStockInOneQuery()
    {
        // Arrange
        var counter = new CommandCounter();
        using var factory = new SqlServerWebFactory(database.ConnectionString, counter);
        await OpenAllBranchesAsync();
        var skus = new[] { new Sku("SKU-010"), new Sku("SKU-011"), new Sku("SKU-012") };
        var before = new List<int>();
        foreach (var sku in skus)
        {
            before.Add(await StockQuantityAsync(sku));
        }

        var (client, token) = await SignInManagerAsync(factory, "127.0.3.6");
        var form = new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["PaymentMethod"] = "Card",
            ["__RequestVerificationToken"] = token
        };
        for (var line = 0; line < skus.Length; line++)
        {
            form[$"RetailItems[{line}].ProductSku"] = skus[line].Value;
            form[$"RetailItems[{line}].Quantity"] = "1";
        }

        counter.Reset();

        // Act
        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(form));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        Assert.Equal(1, counter.StockReads);
        for (var line = 0; line < skus.Length; line++)
        {
            Assert.Equal(before[line] - 1, await StockQuantityAsync(skus[line]));
        }
    }

    [SqlServerFact]
    public async Task Mvc_Reports_ShowTheTotalsHeldInTheTables()
    {
        // Arrange
        var lowSku = new Sku("SKU-002");
        using var factory = new SqlServerWebFactory(database.ConnectionString);
        await OpenAllBranchesAsync();
        var quantityBefore = await StockQuantityAsync(lowSku);
        await using var db = database.CreateContext();

        // Act
        await db.BranchStock
            .Where(row => row.BranchCode == B001 && row.ProductSku == lowSku)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.QuantityOnHand, 3));

        // Assert
        try
        {
            var (client, token) = await SignInManagerAsync(factory, "127.0.3.2");
            var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["CounterId"] = "C01",
                ["RetailItems[0].ProductSku"] = "SKU-004",
                ["RetailItems[0].Quantity"] = "1",
                ["MoneyOrders[0].Amount"] = "100",
                ["MoneyOrders[0].Sender"] = "Report Sender",
                ["MoneyOrders[0].Recipient"] = "Report Recipient",
                ["PaymentMethod"] = "Card",
                ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);

            var date = (await db.CounterTransactions.AsNoTracking()
                .OrderByDescending(row => row.OccurredAtUtc)
                .FirstAsync(row => row.BranchCode == B001)).BusinessDate;
            var lines = await db.TransactionLines.AsNoTracking()
                .Include(line => line.MoneyOrder)
                .Where(line => line.Transaction!.BranchCode == B001
                    && line.Transaction.BusinessDate == date
                    && line.Transaction.Status == TransactionStatus.Paid)
                .ToListAsync();
            var retail = lines.Where(line => line.LineType == LineItemType.RetailProduct).Sum(line => line.LineTotal);
            var services = lines.Where(line => line.LineType != LineItemType.RetailProduct)
                .Sum(line => line.MoneyOrder?.Fee.Amount ?? line.LineTotal);
            Assert.True(retail > 0);
            Assert.InRange(services, 0.01m, 99.99m);

            var report = WebUtility.HtmlDecode(await (await client.GetAsync($"/Reports?date={date:yyyy-MM-dd}")).Content.ReadAsStringAsync());
            var culture = CultureInfo.GetCultureInfo("en-AU");
            Assert.Contains($"<li>B001: retail {retail.ToString("C", culture)}, services {services.ToString("C", culture)}</li>", report);

            var productName = (await db.Products.AsNoTracking().SingleAsync(product => product.Sku == lowSku)).Name;
            Assert.Contains($"<li>B001 / {lowSku.Value} ({productName}): 3</li>", report);

            var shipping = await db.Shipments.AsNoTracking()
                .Where(shipment => shipment.Line!.Transaction!.BranchCode == B001 && shipment.Line.Transaction.Status == TransactionStatus.Paid)
                .GroupBy(shipment => new { shipment.Rate!.ZoneId, shipment.Rate.ServiceLevel })
                .Select(group => new { group.Key.ZoneId, group.Key.ServiceLevel, Shipments = group.Count() })
                .ToListAsync();
            Assert.NotEmpty(shipping);
            Assert.All(shipping, row => Assert.Contains($"<li>{row.ZoneId} / {row.ServiceLevel}: {row.Shipments}</li>", report));

            var moneyOrders = await db.MoneyOrders.CountAsync(order => order.Line!.Transaction!.BranchCode == B001);
            var identityChecks = await db.IdentityVerifications.CountAsync(check => check.Line!.Transaction!.BranchCode == B001);
            var auditor = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            auditor.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.3.3");
            await LoginAsync(auditor, "frank@apexpost.com", DemoStaff.PasswordFor("frank@apexpost.com"));
            var audit = await (await auditor.GetAsync("/Reports/Audit")).Content.ReadAsStringAsync();
            Assert.Matches($@"<td>B001</td>\s*<td>{moneyOrders}</td>\s*<td>{identityChecks}</td>", audit);
        }
        finally
        {
            await db.BranchStock
                .Where(row => row.BranchCode == B001 && row.ProductSku == lowSku)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.QuantityOnHand, quantityBefore));
        }
    }

    [SqlServerFact]
    public async Task Mvc_Void_RestocksThroughTheProcedureAndWritesHistory()
    {
        // Arrange
        var sku = new Sku("SKU-008");
        using var factory = new SqlServerWebFactory(database.ConnectionString);
        await OpenAllBranchesAsync();
        var (client, token) = await SignInManagerAsync(factory, "127.0.0.40");
        var quantityBefore = await StockQuantityAsync(sku);
        var sale = await client.PostAsync("/Counter/Create", SaleForm(sku, token));
        var saleId = await LatestSaleIdAsync(sku);
        var historyBefore = await ReturnHistoryCountAsync(sku);

        // Act
        var reversal = await client.PostAsync("/Counter/Void", VoidForm(saleId, token));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, reversal.StatusCode);
        Assert.Equal(quantityBefore, await StockQuantityAsync(sku));
        Assert.Equal(historyBefore + 1, await ReturnHistoryCountAsync(sku));
        await using var verify = database.CreateContext();
        var saved = await verify.CounterTransactions.AsNoTracking().SingleAsync(row => row.Id == saleId);
        Assert.Equal(TransactionStatus.Voided, saved.Status);
        Assert.True(saved.InventoryRestocked);
    }

    [SqlServerFact]
    public async Task Mvc_VoidThatLosesItsRowVersionRace_RollsBackTheProcedureToo()
    {
        // Arrange
        var sku = new Sku("SKU-009");
        var rival = new RivalWriter(database.ConnectionString, "[CounterTransactions] SET", "UPDATE dbo.CounterTransactions SET ModifiedAtUtc = SYSUTCDATETIME() WHERE Status = 'Paid'");
        using var factory = new SqlServerWebFactory(database.ConnectionString, rival);
        await OpenAllBranchesAsync();
        var (client, token) = await SignInManagerAsync(factory, "127.0.0.30");
        var sale = await client.PostAsync("/Counter/Create", SaleForm(sku, token));
        var saleId = await LatestSaleIdAsync(sku);
        var quantityAfterSale = await StockQuantityAsync(sku);
        var historyBefore = await ReturnHistoryCountAsync(sku);

        // Act
        var reversal = await client.PostAsync("/Counter/Void", VoidForm(saleId, token));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, reversal.StatusCode);
        Assert.Equal(1, rival.Writes);
        Assert.Equal(quantityAfterSale, await StockQuantityAsync(sku));
        Assert.Equal(historyBefore, await ReturnHistoryCountAsync(sku));
        await using var verify = database.CreateContext();
        Assert.Equal(TransactionStatus.Paid, (await verify.CounterTransactions.AsNoTracking().SingleAsync(row => row.Id == saleId)).Status);
    }

    private async Task OpenAllBranchesAsync()
    {
        await using var db = database.CreateContext();
        await db.Branches.ExecuteUpdateAsync(setters => setters
            .SetProperty(branch => branch.TimeZoneId, "UTC")
            .SetProperty(branch => branch.OpenTime, TimeSpan.Zero)
            .SetProperty(branch => branch.CloseTime, new TimeSpan(23, 59, 59)));
    }

    private async Task<int> StockQuantityAsync(Sku sku)
    {
        await using var db = database.CreateContext();
        return await db.BranchStock.AsNoTracking()
            .Where(stock => stock.BranchCode == B001 && stock.ProductSku == sku)
            .Select(stock => stock.QuantityOnHand)
            .SingleAsync();
    }

    private async Task<int> SalesCountAsync()
    {
        await using var db = database.CreateContext();
        return await db.CounterTransactions.CountAsync();
    }

    private async Task<int> ReturnHistoryCountAsync(Sku sku)
    {
        await using var db = database.CreateContext();
        return await db.StockAdjustments.CountAsync(row => row.BranchCode == B001 && row.Sku == sku && row.Reason == nameof(StockAdjustmentReason.CustomerReturn));
    }

    private async Task<Guid> LatestSaleIdAsync(Sku sku)
    {
        await using var db = database.CreateContext();
        return await db.TransactionLines.AsNoTracking()
            .Where(line => line.Sku == sku && line.Transaction!.BranchCode == B001)
            .OrderByDescending(line => line.Transaction!.OccurredAtUtc)
            .Select(line => line.TransactionId)
            .FirstAsync();
    }

    private static async Task<(HttpClient Client, string Token)> SignInManagerAsync(SqlServerWebFactory factory, string address)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
        await LoginAsync(client, "alice@apexpost.com", DemoStaff.PasswordFor("alice@apexpost.com"));
        return (client, await TokenAsync(client, "/Counter"));
    }

    private static FormUrlEncodedContent SaleForm(Sku sku, string token) => new(new Dictionary<string, string>
    {
        ["CounterId"] = "C01",
        ["RetailItems[0].ProductSku"] = sku.Value,
        ["RetailItems[0].Quantity"] = "1",
        ["PaymentMethod"] = "Card",
        ["__RequestVerificationToken"] = token
    });

    private static FormUrlEncodedContent VoidForm(Guid transactionId, string token) => new(new Dictionary<string, string>
    {
        ["transactionId"] = transactionId.ToString(),
        ["reason"] = "Customer changed their mind",
        ["__RequestVerificationToken"] = token
    });

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var token = await TokenAsync(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Signing in as {email} returned {response.StatusCode}, not a redirect.");
        }
    }

    private static async Task<string> TokenAsync(HttpClient client, string path) =>
        Regex.Match(await (await client.GetAsync(path)).Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private sealed class SqlServerWebFactory(string connectionString, IInterceptor? interceptor = null) : WebApplicationFactory<web::Program>
    {
        public Task<IReadOnlyList<AuditEntry>> ReadAuditAsync() =>
            ((IAuditStore)Services.GetService(typeof(IAuditStore))!).GetAllAsync();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ApexPostDb"] = connectionString,
                ["Database:SeedOnStartup"] = "false",
                ["Security:AllowedIps:0"] = "127.0.0.1",
                ["Security:AllowedIps:1"] = "::1",
                ["Security:AllowedIps:2"] = "127.0.0.30",
                ["Security:AllowedIps:3"] = "127.0.0.40",
                ["Security:AllowedIps:4"] = "127.0.0.61"
            }));
            if (interceptor is not null)
            {
                builder.ConfigureTestServices(services =>
                    services.ConfigureDbContext<ApexPostDbContext>(options => options.AddInterceptors(interceptor)));
            }
        }
    }

    // Stands in for another clerk: commits a change to the row just before EF Core's guarded UPDATE reaches it.
    private sealed class RivalWriter(string connectionString, string targetUpdate, string rivalSql) : DbCommandInterceptor
    {
        private int _writes;

        public int Writes => Volatile.Read(ref _writes);

        private async Task WriteIfTargetedAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!command.CommandText.Contains(targetUpdate, StringComparison.Ordinal))
            {
                return;
            }

            Interlocked.Increment(ref _writes);
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await RunAsync(connection, rivalSql);
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await WriteIfTargetedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await WriteIfTargetedAsync(command, cancellationToken);
            return result;
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private int _commands;
        private int _stockReads;

        public int Commands => Volatile.Read(ref _commands);
        public int StockReads => Volatile.Read(ref _stockReads);

        public void Reset()
        {
            Interlocked.Exchange(ref _commands, 0);
            Interlocked.Exchange(ref _stockReads, 0);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            if (command.CommandText.Contains("FROM [BranchStock]", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _stockReads);
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commands);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
