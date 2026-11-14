using ApexPost.Domain.Enums;
using ApexPost.Infrastructure.Services;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class IdentityVerificationTests
{
    private readonly SimulatedIdentityVerificationService _verification = new();

    [Fact]
    public async Task Verify_ValidFormat_ReturnsSuccessWithOnlyMaskedNumber()
    {
        // Act
        var result = await _verification.VerifyAsync(DocumentType.Passport, "A123456789");

        // Assert
        Assert.True(result.IsVerified);
        Assert.Equal("******6789", result.MaskedDocNumber);
        Assert.DoesNotContain("A123456789", result.MaskedDocNumber, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verify_ShortDocument_RecordsUnverified()
    {
        // Act
        var result = await _verification.VerifyAsync(DocumentType.DriversLicense, "AB123");

        // Assert
        Assert.False(result.IsVerified);
        Assert.Equal("******B123", result.MaskedDocNumber);
    }

    [Fact]
    public async Task Verify_InvalidCharacters_IsRejected()
    {
        // Act and assert
        await Assert.ThrowsAsync<ApexPost.Domain.Common.DomainValidationException>(() =>
            _verification.VerifyAsync(DocumentType.Passport, "AB-123456"));
    }

    [Fact]
    public async Task Verify_CallerCancels_ThrowsCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();

        // Act
        await cancellation.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _verification.VerifyAsync(DocumentType.Passport, "A123456789", cancellation.Token));
    }
}
