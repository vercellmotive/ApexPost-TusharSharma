using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ApexPost.Api.Validation;

/// <summary>
/// A custom rule for phone numbers: an optional leading +, then digits that may be grouped with spaces, hyphens or
/// brackets, holding 8 to 15 digits in all (the E.164 limit). The built-in [Phone] accepts almost any text.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed partial class PhoneNumberAttribute()
    : ValidationAttribute("The {0} field must be a phone number of 8 to 15 digits, such as +61 2 5550 1234.")
{
    /// <summary>The fewest digits a phone number may have.</summary>
    public const int MinimumDigits = 8;

    /// <summary>The most digits a phone number may have, the E.164 limit.</summary>
    public const int MaximumDigits = 15;

    /// <summary>The longest text checked. A longer value is refused without running the pattern.</summary>
    public const int MaximumLength = 20;

    // NonBacktracking matches in time proportional to the text, so a long string of digits ending in a bad
    // character cannot make the pattern take exponential time.
    [GeneratedRegex(@"^\+?(?:[0-9]+|\([0-9]+\))(?:[ -]?(?:[0-9]+|\([0-9]+\)))*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Shape();

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
        {
            return true;
        }

        if (value is not string { Length: <= MaximumLength } phone || !Shape().IsMatch(phone))
        {
            return false;
        }

        var digits = phone.Count(char.IsAsciiDigit);
        return digits is >= MinimumDigits and <= MaximumDigits;
    }
}
