using System.Threading.RateLimiting;
using ApexPost.Application.Interfaces;
using ApexPost.Application.Pricing;
using ApexPost.Application.Services;
using ApexPost.Domain.Common;
using ApexPost.Domain.ValueObjects;
using ApexPost.Infrastructure.Services;
using ApexPost.Web.Authorization;
using ApexPost.Web.Filters;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ApexPost.Web.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddApexPostOptions(this IServiceCollection services)
    {
        services.AddValidatedOptions<SecurityOptions>(SecurityOptions.Section);
        services.AddValidatedOptions<MaintenanceOptions>(MaintenanceOptions.Section);
        services.AddValidatedOptions<PerformanceOptions>(PerformanceOptions.Section);
        services.AddValidatedOptions<IntegrationOptions>(IntegrationOptions.Section);
        services.AddValidatedOptions<PartnerCorsOptions>(PartnerCorsOptions.Section);
        services.AddValidatedOptions<RateLimitingOptions>(RateLimitingOptions.Section);
        services.AddValidatedOptions<TransactionOptions>(TransactionOptions.Section);
        services.AddValidatedOptions<InventoryOptions>(InventoryOptions.Section);
        services.AddValidatedOptions<ShippingPricingOptions>(ShippingPricingOptions.Section);
        services.AddValidatedOptions<CourierTrackingOptions>(CourierTrackingOptions.Section);
        services.AddValidatedOptions<CourierRatesOptions>(CourierRatesOptions.Section);
        return services;
    }

    public static IServiceCollection AddApexPostMvc(this IServiceCollection services)
    {
        services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            options.Filters.AddService<PerformanceActionFilter>();
            options.Filters.AddService<AuditActionFilter>();
            options.Filters.AddService<ValidationActionFilter>();
        });
        services.AddScoped<ValidationActionFilter>();
        services.AddScoped<PerformanceActionFilter>();
        services.AddScoped<AuditActionFilter>();
        services.AddHealthChecks();
        services.AddMemoryCache();
        services.AddResponseCompression();
        return services;
    }

    public static IServiceCollection AddApexPostSecurity(this IServiceCollection services)
    {
        services.AddAntiforgery();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    // A signed-in user without permission gets 403, not a redirect back to the login page.
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy("BranchManagerOnly", policy => policy.RequireClaim("Role", "BranchManager"));
            options.AddPolicy("AuditorOnly", policy => policy.RequireClaim("Role", "Auditor"));
            options.AddPolicy("AdministratorOnly", policy => policy.RequireClaim("Role", "Administrator"));
            options.AddPolicy("SameBranchOnly", policy => policy.AddRequirements(new SameBranchRequirement()));
        });
        services.AddSingleton<IAuthorizationHandler, SameBranchAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>();

        services.Configure<CookiePolicyOptions>(options =>
        {
            options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
            options.Secure = CookieSecurePolicy.Always;
            options.MinimumSameSitePolicy = SameSiteMode.Strict;
        });

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<PartnerCorsOptions>>((cors, partner) =>
            cors.AddPolicy("PartnerWidget", policy => policy
                .WithOrigins(partner.Value.PartnerTrackingWidgetOrigin)
                .WithHeaders("X-Api-Key")
                .WithMethods(HttpMethods.Get)));

        services.AddSingleton<PartnerApiKeyValidator>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        return services;
    }

    public static IServiceCollection AddApexPostRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("login", context => CreateFixedWindow(
                context,
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                limits => limits.Login));
            options.AddPolicy("partner", context =>
            {
                var suppliedKey = context.Request.Headers["X-Api-Key"].FirstOrDefault()
                    ?? context.Request.Headers["ApiKey"].FirstOrDefault();
                var partnerId = context.RequestServices.GetRequiredService<PartnerApiKeyValidator>().FindPartnerId(suppliedKey);
                return CreateFixedWindow(
                    context,
                    partnerId is not null ? $"partner:{partnerId}" : $"invalid:{context.Connection.RemoteIpAddress}",
                    limits => limits.Partner);
            });
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                await ProblemResponse.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "Request rate limit exceeded.");
            };
        });
        return services;
    }

    public static IServiceCollection AddApexPostApplication(this IServiceCollection services)
    {
        services.AddSingleton<INotificationService, SimulatedNotificationService>();
        services.AddSingleton<IIdentityVerificationService, SimulatedIdentityVerificationService>();
        services.AddSingleton<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TransactionOptions>>().Value;
            return new TransactionRules(
                new Money(options.HighValueMoneyOrderThreshold),
                new Money(options.MaximumMoneyOrderAmount),
                options.MoneyOrderFee);
        });
        services.AddSingleton(provider =>
            new LowStockRule(provider.GetRequiredService<IOptions<InventoryOptions>>().Value.LowStockThreshold));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddHostedService<DatabaseSeedingHostedService>();

        services.AddScoped<IShippingZoneLookup, CachedShippingZoneLookup>();
        services.AddSingleton(provider => new CourierRateGate(
            provider.GetRequiredService<IOptions<CourierRatesOptions>>().Value.MaxConcurrentRequests));
        services.AddScoped<ICourierRateService>(provider => new SimulatedCourierRateService(
            provider.GetRequiredService<IPricingStrategy>(),
            provider.GetRequiredService<IShippingZoneLookup>(),
            provider.GetRequiredService<CourierRateGate>(),
            TimeSpan.FromMilliseconds(provider.GetRequiredService<IOptions<CourierRatesOptions>>().Value.SimulatedDelayMs)));
        services.AddSingleton<ICourierTrackingService>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CourierTrackingOptions>>().Value;
            return new SimulatedCourierTrackingService(
                TimeSpan.FromMilliseconds(options.TimeoutMs),
                TimeSpan.FromMilliseconds(options.SimulatedDelayMs));
        });
        services.AddSingleton<IPricingStrategy>(provider =>
        {
            var surcharge = provider.GetRequiredService<IOptions<ShippingPricingOptions>>().Value.ParcelWeightSurchargePerKilogram;
            return new TieredShippingPricingStrategy((details, zoneMultiplier) =>
                TieredShippingPricingStrategy.CalculateDefaultPrice(details, zoneMultiplier, surcharge));
        });

        // A scoped DbContext sits behind every command service, so they must be scoped too.
        services.AddScoped<ProductCatalogueService>();
        services.AddScoped<ICatalogueService>(provider => provider.GetRequiredService<ProductCatalogueService>());
        services.AddScoped<ICatalogueCache>(provider => provider.GetRequiredService<ProductCatalogueService>());

        services.AddScoped<DashboardService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<TransactionService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<StaffAuthenticationService>();
        services.AddScoped<StaffManagementService>();
        services.AddScoped<BranchManagementService>();
        services.AddScoped<ProductManagementService>();
        return services;
    }

    private static void AddValidatedOptions<TOptions>(this IServiceCollection services, string section)
        where TOptions : class =>
        services.AddOptions<TOptions>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

    private static RateLimitPartition<string> CreateFixedWindow(
        HttpContext context,
        string partition,
        Func<RateLimitingOptions, FixedWindowPolicyOptions> selectPolicy)
    {
        var policy = selectPolicy(context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value);
        return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = policy.PermitLimit,
            Window = TimeSpan.FromSeconds(policy.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
