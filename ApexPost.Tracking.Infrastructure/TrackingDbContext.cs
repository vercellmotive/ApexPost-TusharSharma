using ApexPost.Tracking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ApexPost.Tracking.Infrastructure;

/// <summary>
/// The shipment tracking tables. They live in the same ApexPostDb database as Week 3, in their own <c>tracking</c>
/// schema with their own migrations, so the Week 3 context and its migrations stay exactly as they were.
/// </summary>
public sealed class TrackingDbContext(DbContextOptions<TrackingDbContext> options) : DbContext(options)
{
    public const string Schema = "tracking";
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";
    public const string RowVersion = "RowVersion";

    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<TrackingScan> Scans => Set<TrackingScan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Shipment>(Configure);
        modelBuilder.Entity<TrackingScan>(Configure);
    }

    private static void Configure(EntityTypeBuilder<Shipment> shipment)
    {
        shipment.ToTable("Shipments", table =>
        {
            table.HasCheckConstraint("CK_Shipments_TrackingNumber", "[TrackingNumber] LIKE 'APX-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]'");
            table.HasCheckConstraint("CK_Shipments_Weight", FormattableString.Invariant($"[WeightKg] BETWEEN {Shipment.MinimumWeightKg} AND {Shipment.MaximumWeightKg}"));
            table.HasCheckConstraint("CK_Shipments_Status", InList("Status", Enum.GetNames<ShipmentStatus>()));
        });
        shipment.HasKey(item => item.Id);
        shipment.Property(item => item.TrackingNumber).HasMaxLength(12).IsUnicode(false);
        shipment.HasIndex(item => item.TrackingNumber).IsUnique();
        shipment.Property(item => item.OriginBranchCode).HasMaxLength(10).IsUnicode(false);
        shipment.Property(item => item.RecipientName).HasMaxLength(100);
        shipment.Property(item => item.RecipientPhone).HasMaxLength(20).IsUnicode(false);
        shipment.Property(item => item.RecipientAddress).HasMaxLength(200);
        shipment.Property(item => item.City).HasMaxLength(100);
        shipment.Property(item => item.WeightKg).HasPrecision(6, 3);
        shipment.Property(item => item.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        shipment.Property<byte[]>(RowVersion).IsRowVersion();
        shipment.Ignore(item => item.Recipient);

        // The list endpoint filters by status and city and pages newest first.
        shipment.HasIndex(item => new { item.Status, item.CreatedAtUtc }).HasDatabaseName("IX_Shipments_Status_Created");
        shipment.HasIndex(item => new { item.City, item.CreatedAtUtc }).HasDatabaseName("IX_Shipments_City_Created");
        shipment.HasIndex(item => item.CreatedAtUtc).HasDatabaseName("IX_Shipments_Created");
    }

    private static void Configure(EntityTypeBuilder<TrackingScan> scan)
    {
        scan.ToTable("Scans", table =>
        {
            table.HasCheckConstraint("CK_Scans_ScanType", InList("ScanType", Enum.GetNames<ScanType>()));
            table.HasCheckConstraint(
                "CK_Scans_DeliveredSignature",
                $"[ScanType] <> '{ScanType.Delivered}' OR [RecipientSignatureName] IS NOT NULL");
        });

        // The scan id from the courier device is the key, so the database itself refuses a second copy of a scan.
        scan.HasKey(item => item.ScanId);
        scan.Property(item => item.ScanId).ValueGeneratedNever();
        scan.Property(item => item.ScanType).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        scan.Property(item => item.CourierId).HasMaxLength(32).IsUnicode(false);
        scan.Property(item => item.Location).HasMaxLength(200);
        scan.Property(item => item.Notes).HasMaxLength(500);
        scan.Property(item => item.RecipientSignatureName).HasMaxLength(100);
        scan.Ignore(item => item.DocumentId);
        scan.HasOne<Shipment>().WithMany().HasForeignKey(item => item.ShipmentId).OnDelete(DeleteBehavior.Cascade);

        // Scan history and the latest-scan lookup both read one shipment's scans in time order.
        scan.HasIndex(item => new { item.ShipmentId, item.ScannedAtUtc }).HasDatabaseName("IX_Scans_Shipment_ScannedAt");
    }

    private static string InList(string column, IEnumerable<string> values) =>
        $"[{column}] IN ({string.Join(", ", values.Select(value => $"'{value}'"))})";
}
