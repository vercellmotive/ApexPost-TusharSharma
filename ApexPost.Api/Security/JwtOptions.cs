using System.ComponentModel.DataAnnotations;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>
/// How bearer tokens are validated, from the <c>Jwt</c> configuration section. The issuer and audience are not
/// secret and live in appsettings.json. The signing key is secret: set it with dotnet user-secrets, never in a file.
/// </summary>
public sealed class JwtOptions : IValidatableObject
{
    /// <summary>The configuration section.</summary>
    public const string Section = "Jwt";

    /// <summary>The fewest bytes an HMAC-SHA256 signing key may have.</summary>
    public const int MinimumKeyBytes = 32;

    /// <summary>The token issuer every accepted token must name.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>The audience every accepted token must name.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>The base64 HMAC-SHA256 signing key, at least 32 bytes. Kept in user secrets.</summary>
    [Required]
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>How long a development token from /dev/token lasts.</summary>
    [Range(1, 24 * 60)]
    public int DevelopmentTokenMinutes { get; set; } = 60;

    /// <summary>The key that signs and checks tokens.</summary>
    public SymmetricSecurityKey CreateSigningKey() => new(Convert.FromBase64String(SigningKey));

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            key = [];
        }

        if (key.Length < MinimumKeyBytes)
        {
            yield return new ValidationResult(
                $"Jwt:SigningKey must be a base64 key of at least {MinimumKeyBytes} bytes. Set it with dotnet user-secrets; see SECURITY.md.",
                [nameof(SigningKey)]);
        }
    }
}
