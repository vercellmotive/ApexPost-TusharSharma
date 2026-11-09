using System.ComponentModel.DataAnnotations;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Validation;

/// <summary>Requires a tracking number that matches <c>^APX-\d{8}$</c>, with \d meaning only 0-9.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class TrackingNumberAttribute()
    : ValidationAttribute("The {0} field must be APX- followed by eight digits, such as APX-10000001.")
{
    public override bool IsValid(object? value) => value is null || TrackingNumber.IsValid(value as string);
}
