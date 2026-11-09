using System.Reflection;
using ApexPost.Courier.Core.Api;
using ApexPost.Courier.Core.Auth;
using ApexPost.Courier.Core.Resilience;
using ApexPost.Courier.Core.Sync;
using ApexPost.Courier.Core.ViewModels;
using ApexPost.Courier.Offline.Domain;
using ApexPost.Courier.Offline.Storage;
using ApexPost.Courier.Pages;
using ApexPost.Courier.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>().ConfigureFonts(fonts =>
        {
            fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
        });

        using (var settings = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApexPost.Courier.appsettings.json")!)
        {
            builder.Configuration.AddJsonStream(settings);
        }

        // Device services.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<SessionEvents>();
        builder.Services.AddSingleton<IConnectivityMonitor, MauiConnectivityMonitor>();
        builder.Services.AddSingleton<IUiDispatcher, MainThreadDispatcher>();
        builder.Services.AddSingleton<INavigator, ShellNavigator>();

        // The typed API client from IHttpClientFactory: the auth handler adds the token from SecureStorage, and the
        // resilience handler retries, breaks the circuit and times out. No HttpClient is ever created by hand.
        builder.Services.AddTransient<AuthHeaderHandler>();
        var api = builder.Services.AddHttpClient<ApexApiClient>(client => client.BaseAddress = BaseUrl(builder.Configuration))
            .AddHttpMessageHandler<AuthHeaderHandler>()
            .AddApexResilience();
#if DEBUG
        // Trust the development certificate for local hosts only, and only in a Debug build. See SECURITY.md.
        api.ConfigurePrimaryHttpMessageHandler(DevelopmentCertificate.CreateHandler);
#endif

        // The Part A offline store, and sync with the real API instead of the fake uploader.
        builder.Services.AddSingleton(services =>
        {
            var listenerLog = services.GetRequiredService<ILoggerFactory>().CreateLogger("ApexPost.Courier.Listener");
            return new CourierDatabase(new CourierDatabaseOptions
            {
                Directory = Path.Combine(FileSystem.AppDataDirectory, "courier"),
                Log = line => listenerLog.LogInformation("{ListenerLine}", line),
            });
        });
        builder.Services.AddSingleton<IScanRepository>(services => new CouchbaseScanRepository(services.GetRequiredService<CourierDatabase>()));
        builder.Services.AddTransient<ApiShipmentUploader>();
        builder.Services.AddSingleton<Func<ApiShipmentUploader>>(services => services.GetRequiredService<ApiShipmentUploader>);
        builder.Services.AddSingleton<ScanSyncService>();

        // Screens.
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<NewScanViewModel>();
        builder.Services.AddSingleton<PendingScansViewModel>();
        builder.Services.AddSingleton<SyncStatusViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<NewScanPage>();
        builder.Services.AddTransient<PendingScansPage>();
        builder.Services.AddTransient<SyncStatusPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }

    /// <summary>The API address for this platform: the Android emulator reaches the host as 10.0.2.2.</summary>
    private static Uri BaseUrl(IConfiguration configuration)
    {
        var platform = DeviceInfo.Current.Platform == DevicePlatform.Android ? "Android"
            : DeviceInfo.Current.Platform == DevicePlatform.WinUI ? "Windows"
            : "Default";
        var url = configuration[$"ApexApi:BaseUrls:{platform}"] ?? configuration["ApexApi:BaseUrls:Default"]
            ?? throw new InvalidOperationException("ApexApi:BaseUrls is missing from appsettings.json.");
        return new Uri(url.EndsWith('/') ? url : url + "/");
    }
}
