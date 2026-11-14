using System.Reflection;
using ApexPost.Application.Interfaces;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using ApexPost.Tracking.Application;
using ApexPost.Tracking.Domain;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.Testing.Tracking;

/// <summary>
/// The fixed time every test starts from. Tests read time only from a <see cref="FakeTimeProvider"/>, never from the
/// real clock, so they give the same result on every run.
/// </summary>
public static class TestTime
{
    public static readonly DateTimeOffset Start = new(2026, 11, 9, 1, 0, 0, TimeSpan.Zero);

    /// <summary>An hour after the default shipment was created, so default scans (30 minutes after creation) are in the past.</summary>
    public static FakeTimeProvider NewClock() => new(Start.AddHours(1));
}

/// <summary>The Week 3 branch repository with three branches: B001 and B002 open, B003 closed.</summary>
public sealed class FakeBranches : IBranchRepository
{
    private readonly Dictionary<string, Branch> _branches = new[]
    {
        MakeBranch("B001", isActive: true),
        MakeBranch("B002", isActive: true),
        MakeBranch("B003", isActive: false),
    }.ToDictionary(branch => branch.Id.Value);

    public Task<Branch?> GetByCodeAsync(BranchCode code, CancellationToken ct = default) =>
        Task.FromResult(_branches.GetValueOrDefault(code.Value));

    public Task<IReadOnlyList<Branch>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Branch>>(_branches.Values.ToList());

    public void Add(Branch branch) => throw new NotSupportedException();

    public void Update(Branch branch) => throw new NotSupportedException();

    private static Branch MakeBranch(string code, bool isActive) => new()
    {
        Id = new BranchCode(code),
        Name = $"Branch {code}",
        Region = "NSW",
        OpenTime = TimeSpan.FromHours(9),
        CloseTime = TimeSpan.FromHours(17),
        IsActive = isActive,
    };
}

/// <summary>
/// An in-memory <see cref="IShipmentStore"/>. Like EF Core it hands out the same tracked instance to every caller,
/// so changes are visible at once. Tests can make the next save fail the way SQL Server would.
/// </summary>
public sealed class InMemoryShipmentStore : IShipmentStore
{
    private static readonly PropertyInfo ShipmentId = typeof(Shipment).GetProperty(nameof(Shipment.Id))!;
    private readonly Lock _gate = new();
    private readonly List<Shipment> _shipments = [];
    private readonly Dictionary<Guid, TrackingScan> _scans = [];
    private readonly List<Shipment> _added = [];
    private readonly List<Shipment> _removed = [];
    private int _nextId = 1;

    public Exception? FailNextSave { get; set; }

    /// <summary>Scans another request stores just before the next <see cref="AddScansAsync"/>, to imitate a race.</summary>
    public List<TrackingScan> RaceScans { get; } = [];

    public int SaveCount { get; private set; }

    public IReadOnlyCollection<TrackingScan> AllScans
    {
        get
        {
            lock (_gate)
            {
                return _scans.Values.ToList();
            }
        }
    }

    public Shipment Seed(Shipment shipment)
    {
        lock (_gate)
        {
            ShipmentId.SetValue(shipment, _nextId++);
            _shipments.Add(shipment);
            return shipment;
        }
    }

    public void SeedScan(TrackingScan scan)
    {
        lock (_gate)
        {
            _scans.Add(scan.ScanId, scan);
        }
    }

    public Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var matches = _shipments
                .Where(shipment => query.Status is null || shipment.Status == query.Status)
                .Where(shipment => string.IsNullOrWhiteSpace(query.City) || string.Equals(shipment.City, query.City.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(shipment => shipment.CreatedAtUtc)
                .ThenBy(shipment => shipment.TrackingNumber, StringComparer.Ordinal)
                .ToList();
            var page = matches.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(View).ToList();
            return Task.FromResult(new PagedResult<ShipmentView>(page, query.Page, query.PageSize, matches.Count));
        }
    }

    public Task<ShipmentView?> GetViewAsync(string trackingNumber, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var shipment = Find(trackingNumber);
            return Task.FromResult(shipment is null ? null : View(shipment));
        }
    }

    public Task<Shipment?> FindAsync(string trackingNumber, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Find(trackingNumber));
        }
    }

    public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(Find(trackingNumber) is not null);
        }
    }

    public void Add(Shipment shipment)
    {
        lock (_gate)
        {
            _added.Add(shipment);
        }
    }

    public void Remove(Shipment shipment)
    {
        lock (_gate)
        {
            _removed.Add(shipment);
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (FailNextSave is { } failure)
            {
                FailNextSave = null;
                _added.Clear();
                _removed.Clear();
                throw failure;
            }

            foreach (var shipment in _added)
            {
                if (Find(shipment.TrackingNumber) is not null)
                {
                    throw new DuplicateValueException("Duplicate tracking number.");
                }

                ShipmentId.SetValue(shipment, _nextId++);
                _shipments.Add(shipment);
            }

            foreach (var shipment in _removed)
            {
                _shipments.Remove(shipment);
                foreach (var scan in _scans.Values.Where(scan => scan.ShipmentId == shipment.Id).ToList())
                {
                    _scans.Remove(scan.ScanId);
                }
            }

            _added.Clear();
            _removed.Clear();
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<TrackingScan>> GetScansAsync(int shipmentId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<TrackingScan>>(_scans.Values
                .Where(scan => scan.ShipmentId == shipmentId)
                .OrderBy(scan => scan.ScannedAtUtc)
                .ThenBy(scan => scan.ReceivedAtUtc)
                .ToList());
        }
    }

    public Task<IReadOnlyDictionary<Guid, int>> FindScanShipmentsAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, int>>(scanIds
                .Where(_scans.ContainsKey)
                .ToDictionary(id => id, id => _scans[id].ShipmentId));
        }
    }

    public Task<IReadOnlySet<Guid>> AddScansAsync(IReadOnlyList<TrackingScan> scans, CancellationToken ct = default)
    {
        lock (_gate)
        {
            foreach (var scan in RaceScans)
            {
                _scans.TryAdd(scan.ScanId, scan);
            }

            RaceScans.Clear();
            var stored = new HashSet<Guid>();
            foreach (var scan in scans)
            {
                if (_scans.TryAdd(scan.ScanId, scan))
                {
                    stored.Add(scan.ScanId);
                }
            }

            return Task.FromResult<IReadOnlySet<Guid>>(stored);
        }
    }

    private Shipment? Find(string trackingNumber) =>
        _shipments.FirstOrDefault(shipment => shipment.TrackingNumber == trackingNumber);

    private ShipmentView View(Shipment shipment)
    {
        var latest = _scans.Values
            .Where(scan => scan.ShipmentId == shipment.Id)
            .OrderByDescending(scan => scan.ScannedAtUtc)
            .ThenByDescending(scan => scan.ReceivedAtUtc)
            .Select(scan => new ScanSummary(scan.ScanType, scan.Location, scan.ScannedAtUtc))
            .FirstOrDefault();
        return ShipmentView.From(shipment, latest);
    }
}

public static class Make
{
    public static Recipient Recipient(string city = "Sydney") => new("Mia Fictional", "+61 2 5550 1234", "12 Example Lane", city);

    public static Shipment Shipment(string trackingNumber = "APX-10000001", DateTimeOffset? createdAt = null, string city = "Sydney") =>
        ApexPost.Tracking.Domain.Shipment.Create(trackingNumber, "B001", Recipient(city), 2.5m, createdAt ?? TestTime.Start);

    public static Shipment ShipmentIn(ShipmentStatus status, string trackingNumber = "APX-10000001")
    {
        var shipment = Shipment(trackingNumber);
        var path = new[] { ShipmentStatus.PickedUp, ShipmentStatus.InTransit, ShipmentStatus.OutForDelivery };
        foreach (var step in path.TakeWhile(step => step <= status && status != ShipmentStatus.Created))
        {
            shipment.MoveTo(step, TestTime.Start);
        }

        if (status is ShipmentStatus.Delivered or ShipmentStatus.Failed)
        {
            shipment.MoveTo(status, TestTime.Start);
        }

        return shipment;
    }

    public const string CourierId = "CR-1001";

    public static string NewScanId() => ScanId.Format(Guid.NewGuid());

    public static IncomingScan Scan(
        string? scanId = null,
        ScanType type = ScanType.Pickup,
        DateTimeOffset? at = null,
        string? signature = null) =>
        new(scanId ?? NewScanId(), type, CourierId, "Sydney depot", at ?? TestTime.Start.AddMinutes(30), "Fictional scan", signature);
}
