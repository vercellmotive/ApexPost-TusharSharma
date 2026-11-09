namespace ApexPost.Courier.Offline.Storage;

/// <summary>One SQL++ statement and how Couchbase Lite runs it.</summary>
/// <param name="AccessPath">For example <c>SEARCH _ USING INDEX idx_syncStatus (&lt;expr&gt;=?)</c>, or <c>SCAN _</c> when no index applies.</param>
public sealed record QueryPlan(string Name, string Sql, string AccessPath);
