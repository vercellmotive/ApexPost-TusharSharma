using ApexPost.Client;
using ApexPost.Client.Consumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Calls ApexPost API v2 through the generated client. Run the API first:
//   dotnet run --project ApexPost.Api --launch-profile https
// Then: dotnet run --project ApexPost.Client.Consumer [-- --ApexPostApi:BaseUrl=https://localhost:7180]
// appsettings.json is read from beside the program, so it works from any folder.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
var baseUrl = builder.Configuration["ApexPostApi:BaseUrl"]
    ?? throw new InvalidOperationException("Set ApexPostApi:BaseUrl in appsettings.json or on the command line.");

// The generated client takes its HttpClient from IHttpClientFactory, which pools the handlers.
builder.Services.AddHttpClient<IApexPostClient, ApexPostClient>(client => client.BaseAddress = new Uri(baseUrl));
builder.Services.AddTransient<ConsumerDemo>();

using var host = builder.Build();
return await host.Services.GetRequiredService<ConsumerDemo>().RunAsync(Console.Out, CancellationToken.None);
