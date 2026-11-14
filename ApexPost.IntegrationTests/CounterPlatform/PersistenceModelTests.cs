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
    public void Model_HasNoChangesMissingFromMigrations()
    {
        using var db = CreateContext();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal(
            ["InitialCreate", "AddWorkloadIndexes", "AddStoredProcedures", "AddRemainingAuditColumns", "ReplaceLowStockIndexWithFiltered", "AddShipmentDestinationAddress", "StampCreatedByInAdjustStock", "AddShipmentDimensionsCheck", "ValidateChangedByInAdjustStock", "TightenPaymentAndRoleColumns", "DropRedundantShippingRateIndex"],
            db.Database.GetMigrations().Select(id => id[(id.IndexOf('_') + 1)..]));
    }

    [Fact]
    public void BranchCodeAndSku_AreUniqueConstraints_AndStaffEmailIsAUniqueIndex()
    {
        using var db = CreateContext();

        Assert.Contains(db.Model.FindEntityType(typeof(BranchRecord))!.GetKeys(), key => key.GetName() == "UQ_Branches_BranchCode");
        Assert.Contains(db.Model.FindEntityType(typeof(ProductRecord))!.GetKeys(), key => key.GetName() == "UQ_Products_Sku");

        // Email is an index rather than a key, because EF Core makes a key value read-only once it is saved.
        var staff = db.Model.FindEntityType(typeof(Staff))!;
        Assert.Contains(staff.GetIndexes(), index => index.IsUnique && index.GetDatabaseName() == "UQ_Staff_Email");
        Assert.DoesNotContain(staff.GetKeys(), key => key.Properties.Any(property => property.Name == nameof(Staff.Email)));
    }

    [Fact]
    public void EveryNameColumn_IsUnicode()
    {
        using var db = CreateContext();

        var nameColumns = db.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.Name == "Name" && property.ClrType == typeof(string))
            .ToList();

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
    public void Schema_ContainsEveryRequiredTable(string table)
    {
        using var db = CreateContext();

        Assert.Contains(db.Model.GetEntityTypes(), entity => entity.GetTableName() == table);
    }

    [Theory]
    [InlineData(typeof(BranchStock))]
    [InlineData(typeof(CounterTransactionRecord))]
    public void ConcurrencyTables_HaveARowVersionShadowProperty(Type entityType)
    {
        using var db = CreateContext();

        var rowVersion = db.Model.FindEntityType(entityType)!.FindProperty(AuditColumns.RowVersion)!;
        Assert.True(rowVersion.IsShadowProperty());
        Assert.True(rowVersion.IsConcurrencyToken);
        Assert.Equal("rowversion", rowVersion.GetColumnType());
    }

    [Fact]
    public void WorkloadIndexes_DeclaredByAttributeAlsoCarryTheirFluentOptions()
    {
        using var db = CreateContext();
        var model = db.GetService<IDesignTimeModel>().Model;

        var moneyOrders = model.FindEntityType(typeof(MoneyOrderRecord))!.GetIndexes()
            .Single(index => index.Name == IndexNames.HighValueMoneyOrder);
        Assert.Equal("[Amount] > 1000", moneyOrders.GetFilter());
        Assert.Equal(["Fee", "TransactionLineId"], moneyOrders.GetIncludeProperties()!);

        var sales = model.FindEntityType(typeof(CounterTransactionRecord))!.GetIndexes()
            .Single(index => index.Name == IndexNames.TransactionByBranchAndDate);
        Assert.Equal(["BranchCode", "BusinessDate"], sales.Properties.Select(property => property.Name));
        Assert.Equal(["Status"], sales.GetIncludeProperties()!);
    }

    [Fact]
    public void MaskedDocumentColumn_CannotHoldAFullDocumentNumber()
    {
        using var db = CreateContext();

        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(IdentityVerificationRecord))!;
        Assert.Equal("varchar(10)", entity.FindProperty(nameof(IdentityVerificationRecord.MaskedDocumentNumber))!.GetColumnType());
        Assert.Contains(entity.GetCheckConstraints(), check => check.Name == "CK_IdentityVerifications_Masked");
    }

    [Fact]
    public void IdempotentScript_DeploysProceduresInTheirOwnBatches()
    {
        using var db = CreateContext();

        var script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        foreach (var procedure in new[] { "usp_DailyBranchSales", "usp_LowStockReport", "usp_AdjustStock" })
        {
            Assert.Contains($"CREATE OR ALTER PROCEDURE dbo.{procedure}", script);
        }

        Assert.DoesNotContain("BEGIN\n    CREATE OR ALTER", script.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ReadQueries_TranslateToSqlWithoutTracking()
    {
        using var db = CreateContext();
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
    public void BranchesAndDetailQueries_TranslateWithSplitQuery()
    {
        using var db = CreateContext();

        var sql = QueryShapes.Branches(db, "B001").AsSplitQuery().ToQueryString();

        Assert.Contains("[BranchCode]", sql);
    }

    [Fact]
    public void RepositoryAndDashboardQueries_TranslateToSql()
    {
        using var db = CreateContext();
        var branch = new BranchCode("B001");
        var sku = new Sku("SKU-001");

        Assert.Contains("[Counters]", QueryShapes.TrackedBranch(db, branch).ToQueryString());
        Assert.Contains("[ProductCategories]", QueryShapes.TrackedProduct(db, sku).ToQueryString());
        Assert.Contains("[RowVersion]", QueryShapes.TrackedStock(db, branch, [sku]).ToQueryString());
        Assert.Contains("[CounterTransactions]", QueryShapes.TrackedTransaction(db, Guid.NewGuid()).ToQueryString());
        Assert.Contains("[IsActive]", QueryShapes.DashboardBranches(db, null).ToQueryString());
        Assert.Contains("[BranchCode]", QueryShapes.BranchTransactionCount(db, "B001").ToQueryString());
    }

    [Fact]
    public async Task PriceRevision_TranslatesToOneSetBasedUpdate()
    {
        var capture = new CaptureNonQuery();
        await using var db = new ApexPostDbContext(new DbContextOptionsBuilder<ApexPostDbContext>()
            .UseSqlServer("Server=model-only;Database=ApexPostDb;Integrated Security=true")
            .AddInterceptors(capture)
            .Options);

        await new PriceRevisionCommand(db).ReviseByCategoryAsync(ProductCategories.Stationery, 10m);

        var sql = Assert.Single(capture.Commands);
        Assert.Contains("UPDATE", sql);
        Assert.Contains("ROUND", sql);
        Assert.Contains("[ModifiedAtUtc]", sql);
    }

    [Fact]
    public void EveryTable_HasAuditColumns()
    {
        using var db = CreateContext();

        foreach (var entity in db.GetService<IDesignTimeModel>().Model.GetEntityTypes().Where(type => !type.IsOwned()))
        {
            foreach (var column in new[] { AuditColumns.CreatedAtUtc, AuditColumns.CreatedBy, AuditColumns.ModifiedAtUtc })
            {
                var property = entity.FindProperty(column);
                Assert.True(property?.IsShadowProperty() == true, $"{entity.GetTableName()} is missing {column}.");
            }
        }
    }

    [Fact]
    public void InactiveFlags_AreSentExplicitlyOnInsert()
    {
        using var db = CreateContext();
        var staff = new Staff { Id = "STF900", Name = "N", Email = "n@example.test", Role = StaffRole.CounterClerk, HomeBranchCode = new BranchCode("B001"), IsActive = false };

        db.Staff.Add(staff);

        // With a sentinel of true, EF Core only falls back to the database default when the value is true.
        Assert.False(db.Entry(staff).Property(person => person.IsActive).CurrentValue);
        Assert.Equal(true, db.Model.FindEntityType(typeof(Staff))!.FindProperty(nameof(Staff.IsActive))!.Sentinel);
    }

    [Fact]
    public void ReferenceRates_MatchThePricingAndWeightRules()
    {
        var rates = ReferenceData.ShippingRates().ToList();

        Assert.Equal(60, rates.Count);
        Assert.Equal(60, rates.Select(rate => rate.Id).Distinct().Count());
        Assert.All(rates, rate => Assert.Equal(rate.Id, ReferenceData.ShippingRateId(rate.ZoneId, rate.IsParcel, rate.ServiceLevel)));
        Assert.Equal(20m, rates.Single(rate => rate.ZoneId == "Zone 1" && rate.IsParcel && rate.ServiceLevel == ServiceLevel.Standard).MaxWeightKg);
        Assert.All(rates.Where(rate => !rate.IsParcel), rate => Assert.Equal(0.5m, rate.MaxWeightKg));
    }

    [Fact]
    public void TransactionMapper_RoundTripsEveryLineType()
    {
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
        var restored = PersistenceMapper.ToDomain(record);

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
    public void Reversal_AddsAPaymentAndKeepsTheSalePayment()
    {
        var transaction = new Transaction { Id = Guid.NewGuid(), BranchCode = new BranchCode("B001"), CounterId = "C01", ClerkStaffId = "STF002" };
        transaction.AddLineItem(new TransactionLineItem(Guid.NewGuid(), "Tape", LineItemType.RetailProduct, 1, new Money(10m), TaxCategory.Exempt, new Sku("SKU-001")));
        transaction.Complete("Cash", new Money(1000m));
        var record = PersistenceMapper.ToRecord(transaction);

        transaction.Refund(new Staff { Id = "STF001", Name = "M", Email = "m@example.test", Role = StaffRole.BranchManager, HomeBranchCode = new BranchCode("B001") }, "Returned");
        PersistenceMapper.CopyStateTo(transaction, record);

        Assert.Equal(TransactionStatus.Refunded, record.Status);
        Assert.Equal([PaymentKinds.Sale, PaymentKinds.Refund], record.Payments.Select(payment => payment.Kind));
        Assert.All(record.Payments, payment => Assert.Equal("Cash", payment.Method));
    }

    [Fact]
    public void HistoryGenerator_MeetsTheVolumeAndStaysValid()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        List<T> Read<T>(string file) => JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", file)), options)!;
        var branches = Read<Branch>("branch.json");
        var staff = Read<Staff>("staff.json");
        var seeded = Read<Transaction>("transaction.json");

        var first = DatabaseSeeder.BuildHistory(branches, staff, Read<Product>("product.json"), seeded, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var second = DatabaseSeeder.BuildHistory(branches, staff, Read<Product>("product.json"), seeded, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));

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
    public void DependencyDirection_PointsInward()
    {
        static IEnumerable<string> References<T>() =>
            typeof(T).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!);

        Assert.DoesNotContain(References<Branch>(), name => name.StartsWith("ApexPost", StringComparison.Ordinal) || name.Contains("EntityFramework", StringComparison.Ordinal));
        var application = References<IUnitOfWork>().ToList();
        Assert.Contains("ApexPost.Domain", application);
        Assert.DoesNotContain(application, name => name.Contains("Infrastructure", StringComparison.Ordinal) || name.Contains("EntityFramework", StringComparison.Ordinal));
    }

    [Fact]
    public void RepositoryContracts_NeverExposeIQueryableOrCommit()
    {
        var repositories = typeof(IUnitOfWork).Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .ToList();

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
    public void ApplicationCode_NeverCallsEnsureCreated()
    {
        var root = FindSolutionRoot();
        var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !IsTestCode(file))
            .Where(file => File.ReadAllText(file).Contains("EnsureCreated", StringComparison.Ordinal))
            .ToList();

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
