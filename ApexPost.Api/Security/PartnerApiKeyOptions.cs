namespace ApexPost.Api.Security;

/// <summary>
/// The partner API keys, by partner name, from the <c>PartnerApiKeys</c> configuration section. Keys come from user
/// secrets or the deployment's secret store. None are in source control, so with no configuration every key fails.
/// </summary>
public sealed class PartnerApiKeyOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "PartnerApiKeys";

    /// <summary>The fewest characters a key may have. Shorter configured keys are ignored.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>Partner name to API key.</summary>
    public Dictionary<string, string> Keys { get; set; } = [];
}
