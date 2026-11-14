using ApexPost.Tracking.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ApexPost.Testing.Persistence;

/// <summary>
/// The real <see cref="TrackingDbContext"/> and its real model on SQLite in memory, one private database per
/// instance. Queries are translated to SQL and run by a real engine, with the primary keys, the unique tracking
/// number index and the cascading foreign key. The connection stays open for the instance's lifetime, because an
/// in-memory SQLite database disappears when its last connection closes.
/// </summary>
/// <remarks>
/// What this does not prove is listed in TESTING-STRATEGY.md section 5: SQL Server's collation, rowversion, its
/// check constraints and error numbers, and the migrations. Those run in the SQL Server tests.
/// </remarks>
public sealed class SqliteTrackingDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SqliteTrackingDatabase()
    {
        _connection.Open();
        using var db = CreateContext();

        // A throwaway schema for this test only; the application itself always uses migrations.
        db.Database.EnsureCreated();
    }

    public SqliteConnection Connection => _connection;

    public TrackingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TrackingDbContext>();
        UseSqlite(options, _connection);
        return new TrackingDbContext(options.Options);
    }

    /// <summary>Points a context at the SQLite connection, with the test-only model adjustments below.</summary>
    public static void UseSqlite(DbContextOptionsBuilder options, SqliteConnection connection) =>
        options.UseSqlite(connection).ReplaceService<IModelCustomizer, SqliteTrackingModelCustomizer>();

    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// Adapts the SQL Server model to SQLite, for tests only. Production code is unchanged; every adjustment here is a
/// difference between the two engines that the SQLite tests therefore cannot cover.
/// </summary>
public sealed class SqliteTrackingModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // The check constraints are written in SQL Server's dialect (LIKE with [0-9] ranges), which SQLite reads
            // differently. The SQL Server tests check them.
            foreach (var check in entity.GetCheckConstraints().ToList())
            {
                entity.RemoveCheckConstraint(check.ModelName);
            }

            foreach (var property in entity.GetProperties())
            {
                // SQLite cannot compare or sort DateTimeOffset values, so they are stored as numbers in time order.
                if (property.ClrType == typeof(DateTimeOffset) || property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
                }

                // rowversion is generated only by SQL Server. Here it stays empty, so lost updates are not detected.
                if (property.IsConcurrencyToken && property.ClrType == typeof(byte[]))
                {
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsNullable = true;
                }
            }
        }
    }
}
