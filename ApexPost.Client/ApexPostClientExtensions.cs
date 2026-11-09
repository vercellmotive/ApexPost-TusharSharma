using System.Net;

namespace ApexPost.Client;

/// <summary>Conveniences on top of the generated client. They only call its methods.</summary>
public static class ApexPostClientExtensions
{
    /// <summary>Gets a shipment, or null when the API answers 404 for the tracking number.</summary>
    public static async Task<ShipmentResponse?> FindShipmentAsync(
        this IApexPostClient client,
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            return await client.GetShipmentAsync(trackingNumber, cancellationToken).ConfigureAwait(false);
        }
        catch (ApexPostApiException<ProblemDetails> exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>A one-line description of an API error, from its ProblemDetails when it has one.</summary>
    public static string Describe(this ApexPostApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ApexPostApiException<ValidationProblemDetails> { Result: var problem } =>
                $"{exception.StatusCode} {problem.Title}: {string.Join("; ", problem.Errors?.SelectMany(error => error.Value.Select(message => $"{error.Key} {message}")) ?? [])}",
            ApexPostApiException<ProblemDetails> { Result: var problem } =>
                $"{exception.StatusCode} {problem.Title}: {problem.Detail}",
            _ => $"{exception.StatusCode} {exception.Message}",
        };
    }
}
