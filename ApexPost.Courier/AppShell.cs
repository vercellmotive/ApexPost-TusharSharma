using ApexPost.Courier.Pages;

namespace ApexPost.Courier;

/// <summary>The Login page, and the three tabs a signed-in courier uses.</summary>
public sealed class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        Items.Add(new ShellContent { Route = "login", Title = "Sign in", ContentTemplate = new DataTemplate(services.GetRequiredService<LoginPage>) });
        Items.Add(new TabBar
        {
            Items =
            {
                new ShellContent { Route = "newscan", Title = "New Scan", ContentTemplate = new DataTemplate(services.GetRequiredService<NewScanPage>) },
                new ShellContent { Route = "pending", Title = "Pending", ContentTemplate = new DataTemplate(services.GetRequiredService<PendingScansPage>) },
                new ShellContent { Route = "status", Title = "Sync Status", ContentTemplate = new DataTemplate(services.GetRequiredService<SyncStatusPage>) },
            },
        });
    }
}
