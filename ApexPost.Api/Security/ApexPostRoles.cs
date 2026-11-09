namespace ApexPost.Api.Security;

/// <summary>The roles and claims in an ApexPost access token.</summary>
public static class ApexPostRoles
{
    /// <summary>A courier, who uploads scans and moves shipments along.</summary>
    public const string Courier = "Courier";

    /// <summary>Back-office staff, who create, edit and delete shipments.</summary>
    public const string Admin = "Admin";

    /// <summary>The claim holding the courier id a courier may upload scans under.</summary>
    public const string CourierIdClaim = "courierId";

    /// <summary>The claim the roles are read from.</summary>
    public const string RoleClaim = "role";

    internal static readonly string[] All = [Courier, Admin];
}
