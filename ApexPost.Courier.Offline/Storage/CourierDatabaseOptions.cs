namespace ApexPost.Courier.Offline.Storage;

/// <summary>How to open the courier's local database.</summary>
public sealed record CourierDatabaseOptions
{
    /// <summary>The database name. The brief fixes it as apexpost_courier.</summary>
    public string Name { get; init; } = CourierDatabase.DefaultName;

    /// <summary>The folder that holds the database. Null means Couchbase Lite's default location for the platform.</summary>
    public string? Directory { get; init; }

    /// <summary>Receives one line for every change notification, naming the documents that changed.</summary>
    public Action<string>? Log { get; init; }
}
