using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ApexPost.Testing.CounterPlatform;

/// <summary>
/// The fictional demo staff seeded by Week 3. Their sign-in pattern is test configuration (<c>testsettings.json</c>),
/// not code, so no password is written in a test.
/// </summary>
public static class DemoStaff
{
    private static readonly string PasswordFormat = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("testsettings.json", optional: false)
        .Build()["DemoStaff:PasswordFormat"]
        ?? throw new InvalidOperationException("testsettings.json has no DemoStaff:PasswordFormat.");

    /// <summary>The seeded password for a demo account such as ben@apexpost.com.</summary>
    public static string PasswordFor(string email)
    {
        var name = email[..email.IndexOf('@', StringComparison.Ordinal)];
        return string.Format(CultureInfo.InvariantCulture, PasswordFormat, char.ToUpperInvariant(name[0]) + name[1..]);
    }
}

/// <summary>Values the Week 3 web app is configured with in tests. The partner key is generated for each run.</summary>
public static class CounterTestSettings
{
    public static readonly string IntegrationKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}

/// <summary>
/// A client address for one test. Rate limits are counted per address, so each test needs its own. It is derived from
/// the test's name: the same on every run, different for each test, and with no counter shared between tests.
/// </summary>
public static class TestClientAddress
{
    public static string For(string testName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(testName));
        return $"127.{1 + (hash[0] % 250)}.{hash[1]}.{1 + (hash[2] % 250)}";
    }
}
