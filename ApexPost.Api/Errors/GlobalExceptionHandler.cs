using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Errors;

/// <summary>
/// The last line of error handling. Every exception that escapes an endpoint becomes a ProblemDetails response.
/// Outside Development the response says only that something failed and gives the trace id; the exception
/// message and stack trace stay in the server log.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client went away. There is nobody to answer.
            logger.LogInformation("Request {Method} {Path} was cancelled by the client.", httpContext.Request.Method, httpContext.Request.Path);
            httpContext.Response.StatusCode = StatusCodes.Status499ClientClosedRequest;
            return true;
        }

        ProblemDetails problem;
        if (exception is BadHttpRequestException badRequest)
        {
            // Kestrel and the framework use this for requests they cannot read, such as a body that is too large.
            logger.LogWarning("Request {Method} {Path} was rejected: {Reason}", httpContext.Request.Method, httpContext.Request.Path, badRequest.Message);
            problem = new ProblemDetails { Status = badRequest.StatusCode, Title = "The request could not be read.", Detail = badRequest.Message };
        }
        else
        {
            logger.LogError(exception, "Unhandled exception processing {Method} {Path}.", httpContext.Request.Method, httpContext.Request.Path);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "The server could not complete the request. Quote the traceId when you report it.",
            };

            if (environment.IsDevelopment())
            {
                // Only a developer's machine sees what went wrong.
                problem.Detail = exception.Message;
                problem.Extensions["exception"] = exception.ToString();
            }
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
