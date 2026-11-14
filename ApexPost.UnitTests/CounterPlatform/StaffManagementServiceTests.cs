using ApexPost.Application.Interfaces;
using ApexPost.Application.Services;
using ApexPost.Domain.Common;
using ApexPost.Domain.Entities;
using ApexPost.Domain.Enums;
using ApexPost.Domain.ValueObjects;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class StaffManagementServiceTests
{
    [Fact]
    public async Task Create_ValidStaff_NormalisesValuesAndAssignsNextId()
    {
        // Arrange
        var existing = CreateStaff("STF009", "existing@example.test");
        var staff = CreateDatabase([existing]);
        var service = CreateService(staff);

        // Act
        var created = await service.CreateAsync(new StaffRecordInput(
            "  New Clerk ",
            " new@example.test ",
            StaffRole.CounterClerk,
            " B001 "));

        // Assert
        Assert.Equal("STF010", created.Id);
        Assert.Equal("New Clerk", created.Name);
        Assert.Equal("new@example.test", created.Email);
        Assert.Equal(new BranchCode("B001"), created.HomeBranchCode);
    }

    [Fact]
    public async Task Create_DuplicateEmailInOtherCase_IsRejected()
    {
        // Arrange
        var service = CreateService(CreateDatabase(
            [CreateStaff("STF001", "clerk@example.test")]));

        // Act and assert
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CreateAsync(new StaffRecordInput(
                "Another Clerk",
                "CLERK@example.test",
                StaffRole.CounterClerk,
                "B001")));
    }

    [Fact]
    public async Task Create_TwoStaff_CommitsOnceEachWithDistinctIds()
    {
        // Arrange
        var staff = CreateDatabase([CreateStaff("STF001", "existing@example.test")]);
        var persistence = new InMemoryPersistence(staff);
        var service = new StaffManagementService(persistence, persistence, persistence, new FakePasswordHasher());

        // Act
        var created = await service.CreateAsync(new StaffRecordInput("New Clerk", "new@example.test", StaffRole.CounterClerk, "B001"));

        // Assert
        Assert.Equal(1, persistence.CommitCount);
        Assert.Equal(2, staff.AllStaff().Select(person => person.Id).Distinct().Count());
        Assert.Equal(created.Id, staff.StaffById(created.Id).Id);
    }

    [Fact]
    public async Task UpdateAndDeactivate_InactiveOrUnknownStaff_PreservesStatusAndRejectsUnknown()
    {
        // Arrange
        var existing = CreateStaff("STF001", "old@example.test");
        existing.Deactivate();
        var staff = CreateDatabase([existing]);
        var service = CreateService(staff);

        // Act
        var updated = await service.UpdateAsync(
            "STF001",
            new StaffRecordInput("Updated Clerk", "updated@example.test", StaffRole.Auditor, "B001"));

        // Assert
        Assert.False(updated.IsActive);
        Assert.Equal("Updated Clerk", updated.Name);
        Assert.Equal(StaffRole.Auditor, updated.Role);

        // Act
        await service.DeactivateAsync("STF001");

        // Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.DeactivateAsync("STF404"));
    }

    [Fact]
    public async Task Create_UnknownBranchOrUndefinedRole_IsRejected()
    {
        // Arrange
        var service = CreateService(CreateDatabase());

        // Act and assert
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CreateAsync(new StaffRecordInput(
                "Clerk",
                "clerk@example.test",
                StaffRole.CounterClerk,
                "B404")));
        await Assert.ThrowsAsync<DomainValidationException>(() =>
            service.CreateAsync(new StaffRecordInput(
                "Clerk",
                "clerk@example.test",
                (StaffRole)999,
                "B001")));
    }

    private static StaffManagementService CreateService(InMemoryDatabase staff)
    {
        var persistence = new InMemoryPersistence(staff);
        return new StaffManagementService(persistence, persistence, persistence, new FakePasswordHasher());
    }

    private static InMemoryDatabase CreateDatabase(IEnumerable<Staff>? staff = null)
    {
        var database = InMemoryDatabase.Empty();
        database.AddBranch(new Branch
        {
            Id = new BranchCode("B001"),
            Name = "Test Branch",
            Region = "NSW",
            OpenTime = TimeSpan.Zero,
            CloseTime = TimeSpan.FromHours(23)
        });
        foreach (var person in staff ?? [])
        {
            database.AddStaff(person);
        }

        return database;
    }

    private static Staff CreateStaff(string id, string email) => new()
    {
        Id = id,
        Name = "Existing Staff",
        Email = email,
        Role = StaffRole.CounterClerk,
        HomeBranchCode = new BranchCode("B001")
    };

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";

        public bool Verify(string password, string? passwordHash) => passwordHash == Hash(password);
    }
}
