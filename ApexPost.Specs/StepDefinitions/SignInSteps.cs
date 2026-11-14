using ApexPost.Api.Security;
using ApexPost.Specs.Support;
using ApexPost.Testing.Api;
using ApexPost.Testing.Tracking;

namespace ApexPost.Specs.StepDefinitions;

/// <summary>
/// Sign-in steps shared by every API feature. They live in one class because Reqnroll binds a step text to exactly
/// one method: the Copilot draft of AccessControlSteps defined "I am signed in as an administrator" a second time,
/// which made every scenario using it fail as ambiguous (COPILOT-LOG.md, prompt 2).
/// </summary>
[Reqnroll.Binding]
public sealed class SignInSteps(ApiScenarioContext context)
{
    [Reqnroll.Given("I am signed in as an administrator")]
    public void GivenIAmSignedInAsAnAdministrator() => SignIn(context.Api.CreateClient(ApexPostRoles.Admin));

    [Reqnroll.Given("I am signed in as a courier")]
    public void GivenIAmSignedInAsACourier() => SignIn(context.Api.CreateClient(ApexPostRoles.Courier));

    [Reqnroll.Given("I am signed in as {string}")]
    public void GivenIAmSignedInAs(string role) => SignIn(context.Api.CreateClient(role switch
    {
        "an administrator" or "Admin" => ApexPostRoles.Admin,
        "a courier" or "Courier" => ApexPostRoles.Courier,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Use Admin or Courier."),
    }));

    [Reqnroll.Given("I am not signed in")]
    public void GivenIAmNotSignedIn() => SignIn(context.Api.CreateDefaultClient());

    [Reqnroll.Given("my sign-in has expired")]
    public void GivenMySignInHasExpired()
    {
        var client = context.Api.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token([ApexPostRoles.Courier], Make.CourierId, expires: ApiFactory.ExpiredAt));
        SignIn(client);
    }

    private void SignIn(HttpClient client)
    {
        context.Client?.Dispose();
        context.Client = client;
    }
}
