using ApexPost.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApexPostApi();

var app = builder.Build();

app.UseExceptionHandler();

// Errors without a body, such as 404 for an unknown route or 405, also get ProblemDetails.
app.UseStatusCodePages();
app.UseHttpsRedirection();

app.MapControllers();

app.Run();

public partial class Program;
