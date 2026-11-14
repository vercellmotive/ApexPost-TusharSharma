using System.Security.Claims;
using ApexPost.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Xunit;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.UnitTests.CounterPlatform;

public sealed class AuthorizationTests
{
    [Fact]
    public async Task SameBranchPolicy_RejectsAnotherBranchRouteValue()
    {
        var requirement = new SameBranchRequirement();
        var identity = new ClaimsIdentity([new Claim("BranchCode", "B001")], "test");
        var context = new DefaultHttpContext();
        context.Request.RouteValues["branchCode"] = "B002";
        var authorizationContext = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(identity),
            context);

        await new SameBranchAuthorizationHandler().HandleAsync(authorizationContext);

        Assert.False(authorizationContext.HasSucceeded);
    }

    [Fact]
    public async Task SameBranchPolicy_AllowsAssignedBranch()
    {
        var requirement = new SameBranchRequirement();
        var identity = new ClaimsIdentity([new Claim("BranchCode", "B001")], "test");
        var context = new DefaultHttpContext();
        context.Request.RouteValues["branchCode"] = "B001";
        var authorizationContext = new Microsoft.AspNetCore.Authorization.AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(identity),
            context);

        await new SameBranchAuthorizationHandler().HandleAsync(authorizationContext);

        Assert.True(authorizationContext.HasSucceeded);
    }
}
