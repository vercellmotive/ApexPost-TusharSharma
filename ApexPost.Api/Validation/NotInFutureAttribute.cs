using System.ComponentModel.DataAnnotations;
using ApexPost.Tracking.Application;

namespace ApexPost.Api.Validation;

/// <summary>
/// A custom rule: the time cannot be in the future. A few minutes of device clock drift are allowed
/// (<see cref="ShipmentService.ScanClockTolerance"/>). The clock comes from DI, so tests can fix it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class NotInFutureAttribute() : ValidationAttribute("The {0} field cannot be in the future.")
{
    /// <inheritdoc />
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not DateTimeOffset time)
        {
            return ValidationResult.Success;
        }

        var clock = validationContext.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
        return time <= clock.GetUtcNow() + ShipmentService.ScanClockTolerance
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), MemberName(validationContext));
    }

    private static string[]? MemberName(ValidationContext context) => context.MemberName is { } name ? [name] : null;
}
