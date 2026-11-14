using ApexPost.Domain.Enums;
using ApexPost.Infrastructure.Services;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class IdentityVerificationTests
{
    private readonly SimulatedIdentityVerificationService _verification = new();

    [Fact]
    public async Task ValidFormatReturnsSuccessfulResultAndOnlyMaskedNumber()
    {
        var result = await _verification.VerifyAsync(DocumentType.Passport, "A123456789");

        Assert.True(result.IsVerified);
        Assert.Equal("******6789", result.MaskedDocNumber);
        Assert.DoesNotContain("A123456789", result.MaskedDocNumber, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShortDocumentFormatIsRecordedAsUnverified()
    {
        var result = await _verification.VerifyAsync(DocumentType.DriversLicense, "AB123");

        Assert.False(result.IsVerified);
        Assert.Equal("******B123", result.MaskedDocNumber);
    }

    [Fact]
    public async Task InvalidDocumentCharactersAreRejected()
    {
        await Assert.ThrowsAsync<ApexPost.Domain.Common.DomainValidationException>(() =>
            _verification.VerifyAsync(DocumentType.Passport, "AB-123456"));
    }

    [Fact]
    public async Task CallerCancellationIsPreserved()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _verification.VerifyAsync(DocumentType.Passport, "A123456789", cancellation.Token));
    }
}
