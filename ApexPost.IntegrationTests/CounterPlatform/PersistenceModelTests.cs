using System.Data.Common;
using System.Text;
using System.Text.Json;
using ApexPost.Application.Interfaces;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.IntegrationTests.CounterPlatform;

// None of these open a connection: EF Core builds the model and translates LINQ to SQL purely in memory.
public sealed class PersistenceModelTests
{
    private static ApexPostDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer("Server=model-only;Database=ApexPostDb;Integrated Security=true")
            .Options);

    [Fact]
    public void Model_ComparedWithTheLastMigration_HasNoPendingChanges()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var pendingChanges = db.Database.HasPendingModelChanges();
        var migrations = db.Database.GetMigrations().Select(id => id[(id.IndexOf('_') + 1)..]).ToList();

        // Assert
        Assert.False(pendingChanges);
        Assert.Equal(
            ["InitialCreate", "AddWorkloadIndexes", "AddStoredProcedures", "AddRemainingAuditColumns", "ReplaceLowStockIndexWithFiltered", "AddShipmentDestinationAddress", "StampCreatedByInAdjustStock", "AddShipmentDimensionsCheck", "ValidateChangedByInAdjustStock", "TightenPaymentAndRoleColumns", "DropRedundantShippingRateIndex"],
            migrations);
    }

    [Fact]
    public void Model_UniqueKeys_BranchCodeAndSkuAreConstraintsAndStaffEmailIsAnIndex()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var branchKeys = db.Model.FindEntityType(typeof(BranchRecord))!.GetKeys();
        var productKeys = db.Model.FindEntityType(typeof(ProductRecord))!.GetKeys();
        // Email is an index rather than a key, because EF Core makes a key value read-only once it is saved.
        var staff = db.Model.FindEntityType(typeof(Staff))!;

        // Assert
        Assert.Contains(branchKeys, key => key.GetName() == "UQ_Branches_BranchCode");
        Assert.Contains(productKeys, key => key.GetName() == "UQ_Products_Sku");
        Assert.Contains(staff.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UQ_Staff_Email");
        Assert.DoesNotContain(staff.GetKeys(), key => key.Properties.Any(property => property.Name == nameof(Staff.Email)));
    }

    [Fact]
    public void Model_NameColumns_AreUnicode()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var nameColumns = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.Name == "Name" && property.ClrType == typeof(string))
            .ToList();

        // Assert
        Assert.NotEmpty(nameColumns);
        Assert.All(nameColumns, property => Assert.StartsWith("nvarchar", property.GetColumnType()));
    }

    [Theory]
    [InlineData("Branches")]
    [InlineData("Counters")]
    [InlineData("Staff")]
    [InlineData("Roles")]
    [InlineData("Products")]
    [InlineData("ProductCategories")]
    [InlineData("BranchStock")]
    [InlineData("StockAdjustments")]
    [InlineData("ShippingZones")]
    [InlineData("ShippingRates")]
    [InlineData("Shipments")]
    [InlineData("MoneyOrders")]
    [InlineData("IdentityVerifications")]
    [InlineData("CounterTransactions")]
    [InlineData("TransactionLines")]
    [InlineData("Payments")]
    [InlineData("AuditEntries")]
    public void Model_RequiredTables_AreAllMapped(string table)
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var tables = db.Model.GetEntityTypes().Select(entity => entity.GetTableName()).ToList();

        // Assert
        Assert.Contains(table, tables);
    }

    [Theory]
    [InlineData(typeof(BranchStock))]
    [InlineData(typeof(CounterTransactionRecord))]
    public void Model_ConcurrencyTable_HasARowVersionShadowProperty(Type entityType)
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var rowVersion = db.Model.FindEntityType(entityType)!.FindProperty(AuditColumns.RowVersion)!;

        // Assert
        Assert.True(rowVersion.IsShadowProperty());
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
    }

    [Fact]
    public void Model_IndexesDeclaredByAttribute_CarryTheirFluentOptions()
    {
        // Arrange
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;

        // Act
        var moneyOrders = model.FindEntityType(typeof(MoneyOrderRecord))!.GetIndexes()
            .Single(index => index.Name == IndexNames.HighValueMoneyOrder);
        var sales = model.FindEntityType(typeof(CounterTransactionRecord))!.GetIndexes()
            .Single(index => index.Name == IndexNames.TransactionByBranchAndDate);

        // Assert
        Assert.Equal("[Amount] > 1000", moneyOrders.GetFilter());
        Assert.Equal(["Fee", "TransactionLineId"], moneyOrders.GetIncludeProperties()!);
        Assert.Equal(["BranchCode", "BusinessDate"], sales.Properties.Select(property => property.Name));
        Assert.Equal(["Status"], sales.GetIncludeProperties()!);
    }

    [Fact]
    public void Model_MaskedDocumentColumn_CannotHoldAFullDocumentNumber()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdentityVerificationRecord))!;

        // Assert
        Assert.Equal("varchar(10)", entity.FindProperty(nameof(IdentityVerificationRecord.MaskedDocumentNumber))!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_IdentityVerifications_Masked");
    }

    [Fact]
    public void GenerateScript_Idempotent_PutsEachProcedureInItsOwnBatch()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        // Assert
        foreach (var procedure in new[] { "usp_DailyBranchSales", "usp_LowStockReport", "usp_AdjustStock" })
        {
            Assert.Contains($"CREATE OR ALTER PROCEDURE dbo.{procedure}", script);
        }

        Assert.DoesNotContain("BEGIN\n    CREATE OR ALTER", script.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ToQueryString_ReadQueries_TranslateWithoutTracking()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var captures = new Dictionary<string, string>
        {
            ["Low stock (screen and dashboard)"] = QueryShapes.LowStock(db, "B001", 5).ToQueryString(),
            ["Shipping volume by zone and service level"] = QueryShapes.ShippingVolume(db, "B001").ToQueryString(),
            ["Money orders and identity checks per branch"] = QueryShapes.MoneyOrderAudit(db, null).ToQueryString(),
            ["Branch transactions screen"] = QueryShapes.BranchTransactions(db, "B001", 200).ToQueryString(),
            ["Branch catalogue screen"] = QueryShapes.BranchCatalogue(db, "B001").ToQueryString(),
            ["Latest sales date"] = QueryShapes.LatestSalesDate(db, "B001").ToQueryString(),
            ["Staff list screen"] = QueryShapes.Staff(db, null).ToQueryString(),
            ["Staff detail screen"] = QueryShapes.Staff(db, "STF001").ToQueryString(),
            ["Product detail screen"] = QueryShapes.Products(db, "SKU-001").ToQueryString()
        };

        // Assert
        Assert.All(captures.Values, sql => Assert.Contains("SELECT", sql));
        Assert.DoesNotContain("@__", captures["Low stock (screen and dashboard)"].Split('\n')[0]);
        Assert.Contains("[LineTotal]", captures["Branch transactions screen"]);

        // Writes the real generated SQL for PERFORMANCE.md when asked: nothing is hand-written.
        if (Environment.GetEnvironmentVariable("APEXPOST_SQL_CAPTURE_PATH") is { Length: > 0 } path)
        {
            var output = new StringBuilder();
            foreach (var (name, sql) in captures)
            {
                output.Append("### ").AppendLine(name).AppendLine().AppendLine("```sql").AppendLine(sql).AppendLine("```").AppendLine();
            }

            File.WriteAllText(path, output.ToString());
        }
    }

    [Fact]
    public void ToQueryString_BranchQueryAsSplitQuery_Translates()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var sql = QueryShapes.Branches(db, "B001").AsSplitQuery().ToQueryString();

        // Assert
        Assert.Contains("[BranchCode]", sql);
    }

    [Fact]
    public void ToQueryString_RepositoryAndDashboardQueries_TranslateToSql()
    {
        // Arrange
        using var db = CreateContext();
        var branch = new BranchCode("B001");
        var sku = new Sku("SKU-001");

        // Act
        var trackedBranch = QueryShapes.TrackedBranch(db, branch).ToQueryString();
        var trackedProduct = QueryShapes.TrackedProduct(db, sku).ToQueryString();
        var trackedStock = QueryShapes.TrackedStock(db, branch, [sku]).ToQueryString();
        var trackedTransaction = QueryShapes.TrackedTransaction(db, Guid.NewGuid()).ToQueryString();
        var dashboardBranches = QueryShapes.DashboardBranches(db, null).ToQueryString();
        var branchTransactionCount = QueryShapes.BranchTransactionCount(db, "B001").ToQueryString();

        // Assert
        Assert.Contains("[Counters]", trackedBranch);
        Assert.Contains("[ProductCategories]", trackedProduct);
        Assert.Contains("[RowVersion]", trackedStock);
        Assert.Contains("[CounterTransactions]", trackedTransaction);
        Assert.Contains("[IsActive]", dashboardBranches);
        Assert.Contains("[BranchCode]", branchTransactionCount);
    }

    [Fact]
    public async Task ReviseByCategoryAsync_OneCategory_RunsOneSetBasedUpdate()
    {
        // Arrange
        var capture = new CaptureNonQuery();
        await using var db = new ApexPostDbContext(new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer("Server=model-only;Database=ApexPostDb;Integrated Security=true")
            .AddInterceptors(capture)
            .Options);

        // Act
        await new PriceRevisionCommand(db).ReviseByCategoryAsync(ProductCategories.Stationery, 10m);

        // Assert
        var sql = Assert.Single(capture.Commands);
        Assert.Contains("UPDATE", sql);
        Assert.Contains("ROUND", sql);
        Assert.Contains("[ModifiedAtUtc]", sql);
    }

    [Fact]
    public void Model_EveryTable_HasAuditColumns()
    {
        // Arrange
        using var db = CreateContext();

        // Act
        var tables = db.GetService<IDesignTimeModel>().Model.GetEntityTypes().Where(type => !type.IsOwned()).ToList();

        // Assert
        foreach (var entity in tables)
        {
            foreach (var column in new[] { AuditColumns.CreatedAtUtc, AuditColumns.CreatedBy, AuditColumns.ModifiedAtUtc })
            {
                var property = entity.FindProperty(column);
                Assert.True(property?.IsShadowProperty() == true, $"{entity.GetTableName()} is missing {column}.");
            }
        }
    }

    [Fact]
    public void Add_InactiveStaff_SendsIsActiveExplicitly()
    {
        // Arrange
        using var db = CreateContext();
        var staff = new Staff { Id = "STF900", Name = "N", Email = "n@example.test", Role = StaffRole.CounterClerk, HomeBranchCode = new BranchCode("B001"), IsActive = false };

        // Act
        db.Staff.Add(staff);

        // Assert
        // With a sentinel of true, EF Core only falls back to the database default when the value is true.
        Assert.False(db.Entry(staff).Property(person => person.IsActive).CurrentValue);
        Assert.Equal(true, db.Model.FindEntityType(typeof(Staff))!.FindProperty(nameof(Staff.IsActive))!.Sentinel);
    }

    [Fact]
    public void ShippingRates_ReferenceData_MatchThePricingAndWeightRules()
    {
        // Act
        var rates = ReferenceData.ShippingRates().ToList();

        // Assert
        Assert.Equal(60, rates.Count);
        Assert.Equal(60, rates.Select(rate => rate.Id).Distinct().Count());
        Assert.All(rates, rate => Assert.Equal(rate.Id, ReferenceData.ShippingRateId(rate.ZoneId, rate.IsParcel, rate.ServiceLevel)));
        Assert.Equal(20m, rates.Single(rate => rate.ZoneId == "Zone 1" && rate.IsParcel && rate.ServiceLevel == ServiceLevel.Standard).MaxWeightKg);
        Assert.All(rates.Where(rate => !rate.IsParcel), rate => Assert.Equal(0.5m, rate.MaxWeightKg));
    }

    [Fact]
    public void ToDomain_RecordWithEveryLineType_RoundTrips()
    {
        // Arrange
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            BranchCode = new BranchCode("B001"),
            CounterId = "C01",
            ClerkStaffId = "STF002",
            BusinessDate = new DateOnly(2026, 10, 1)
        };
        var verification = IdVerificationDetails.FromDocumentNumber(DocumentType.Passport, "A123456789", true);
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Tape", LineItemType.RetailProduct, 2, new Money(10m), TaxCategory.StandardGst, new Sku("SKU-001")));
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Shipping", LineItemType.ParcelShipping, 1, new Money(5m), TaxCategory.Exempt, Shipping: new ShippingDetails(true, Weight.FromKilograms(2m), new Dimensions(3, 4, 5), "Zone 3", ServiceLevel.Express, new Address("1 Test St", "Sydney", "NSW", "2000", "Australia"))));
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Money order", LineItemType.MoneyOrder, 1, new Money(1510m), TaxCategory.Exempt, MoneyOrder: new MoneyOrderDetails(new Money(1500m), "A", "B", new Money(10m))));
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "ID", LineItemType.IdentityVerification, 1, Money.Zero, TaxCategory.Exempt, IdVerification: verification));
        transaction.Complete("Card", new Money(1000m));

        var record = PersistenceMapper.ToRecord(transaction);
        var rate = ReferenceData.ShippingRates().Single(item => item.Id == record.Lines[1].Shipment!.ShippingRateId);
        record.Lines[1].Shipment!.Rate = rate;

        // Act
        var restored = PersistenceMapper.ToDomain(record);

        // Assert
        Assert.Equal(transaction.GrandTotal, restored.GrandTotal);
        Assert.Equal(transaction.ServicesTotal, restored.ServicesTotal);
        Assert.Equal(TransactionStatus.Paid, restored.Status);
        Assert.Equal("Card", restored.PaymentMethod);
        Assert.Equal(new DateOnly(2026, 10, 1), restored.BusinessDate);
        Assert.Equal("Zone 3", restored.LineItems[1].Shipping!.DestinationZone);
        Assert.Equal(ServiceLevel.Express, restored.LineItems[1].Shipping!.ServiceLevel);
        Assert.Equal("Sydney", restored.LineItems[1].Shipping!.Destination!.Value.City);
        Assert.Equal("******6789", record.Lines[3].IdentityVerification!.MaskedDocumentNumber);
        Assert.Equal(verification, restored.LineItems[3].IdVerification);
        Assert.Equal(transaction.GrandTotal, Assert.Single(record.Payments).Amount);
    }

    [Fact]
    public void CopyStateTo_ReversedSale_AddsAPaymentAndKeepsTheSalePayment()
    {
        // Arrange
        var transaction = new Transaction { Id = Guid.NewGuid(), BranchCode = new BranchCode("B001"), CounterId = "C01", ClerkStaffId = "STF002" };
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Tape", LineItemType.RetailProduct, 1, new Money(10m), TaxCategory.Exempt, new Sku("SKU-001")));
        transaction.Complete("Cash", new Money(1000m));
        var record = PersistenceMapper.ToRecord(transaction);

        transaction.Refund(new Staff { Id = "STF001", Name = "M", Email = "m@example.test", Role = StaffRole.BranchManager, HomeBranchCode = new BranchCode("B001") }, "Returned");

        // Act
        PersistenceMapper.CopyStateTo(transaction, record);

        // Assert
        Assert.Equal(TransactionStatus.Refunded, record.Status);
        Assert.Equal([PaymentKinds.Sale, PaymentKinds.Refund], record.Payments.Select(payment => payment.Kind));
        Assert.All(record.Payments, payment => Assert.Equal("Cash", payment.Method));
    }

    [Fact]
    public void BuildHistory_SameInputsTwice_MeetsTheVolumeAndStaysValid()
    {
        // Arrange
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<T> Read<T>(string file) => JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", file)), options)!;
        var branches = Read<Branch>("branch.json");
        var staff = Read<Staff>("staff.json");
        var seeded = Read<Transaction>("transaction.json");

        var first = DatabaseSeeder.BuildHistory(branches, staff, Read<Product>("product.json"), seeded, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        // Act
        var second = DatabaseSeeder.BuildHistory(branches, staff, Read<Product>("product.json"), seeded, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

        // Assert
        Assert.True(first.Count >= 200);
        Assert.Equal(first.Sum(item => item.GrandTotal.Amount), second.Sum(item => item.GrandTotal.Amount));
        Assert.All(first, transaction =>
        {
            Assert.Contains(transaction.CounterId, branches.Single(branch => branch.Id == transaction.BranchCode).CounterTerminals);
            Assert.NotEqual(default, transaction.BusinessDate);
            Assert.NotEmpty(transaction.LineItems);
            Assert.All(transaction.LineItems, line =>
            {
                if (line.IdVerification is { } verification)
                {
                    Assert.Matches("^\\*{6}.{4}$", verification.MaskedDocNumber);
                }
            });
        });
        Assert.Contains(first, transaction => transaction.Status == TransactionStatus.Voided);
        Assert.Contains(first, transaction => transaction.Status == TransactionStatus.Refunded);
    }

    [Fact]
    public void AssemblyReferences_EveryLayer_PointInward()
    {
        // Arrange
        static IEnumerable<string> References<T>() =>
            typeof(T).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!);

        // Act
        var domain = References<Branch>().ToList();
        var application = References<IUnitOfWork>().ToList();

        // Assert
        Assert.DoesNotContain(domain, name => name.StartsWith("ApexPost", StringComparison.Ordinal) || name.Contains("EntityFramework", StringComparison.Ordinal));
        Assert.Contains("ApexPost.Domain", application);
        Assert.DoesNotContain(application, name => name.Contains("Infrastructure", StringComparison.Ordinal) || name.Contains("EntityFramework", StringComparison.Ordinal));
    }

    [Fact]
    public void RepositoryInterfaces_EveryMember_NeverExposesIQueryableOrCommit()
    {
        // Act
        var repositories = typeof(IUnitOfWork).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .ToList();

        // Assert
        Assert.True(repositories.Count >= 5);
        foreach (var method in repositories.SelectMany(type => type.GetMethods()))
        {
            Assert.False(method.Name.StartsWith("SaveChanges", StringComparison.Ordinal), method.Name);
            Assert.DoesNotContain("IQueryable", method.ReturnType.ToString());
        }
    }

    // Test projects may create a throwaway schema (the SQLite tests do); application code never may.
    private static bool IsTestCode(string file) =>
        new[] { "ApexPost.UnitTests", "ApexPost.IntegrationTests", "ApexPost.Specs", "ApexPost.Testing" }
            .Any(project => file.Contains($"{Path.DirectorySeparatorChar}{project}{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void SourceFiles_ApplicationCode_NeverCallEnsureCreated()
    {
        // Arrange
        var root = FindSolutionRoot();

        // Act
        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !IsTestCode(file))
            .Where(file => File.ReadAllText(file).Contains("EnsureCreated", StringComparison.Ordinal))
            .ToList();

        // Assert
        Assert.Empty(offenders);
    }

    private static string FindSolutionRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ApexPost.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Solution root was not found.");
    }

    // Suppresses the connection and command, so EF Core's real SQL can be read without a server.
    private sealed class CaptureNonQuery : DbCommandInterceptor, IDbConnectionInterceptor
    {
        public List<string> Commands { get; } = [];

        public ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        }
    }
}
