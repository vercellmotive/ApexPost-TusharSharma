using System.Text.Json;
using ApexPost.Tracking.Application;
using Microsoft.AspNetCore.Mvc;

namespace ApexPost.Api.Errors;

/// <summary>Turns a service error into the matching ProblemDetails response.</summary>
internal static class ServiceErrorResults
{
    public static ActionResult ToProblem(this ControllerBase controller, ServiceError error)
    {
        switch (error.Kind)
        {
            case ServiceErrorKind.Validation:
                var field = error.Field is null ? string.Empty : JsonNamingPolicy.CamelCase.ConvertName(error.Field);
                controller.ModelState.AddModelError(field, error.Detail);
                return controller.ValidationProblem(title: error.Title, modelStateDictionary: controller.ModelState);
            case ServiceErrorKind.NotFound:
                return controller.Problem(detail: error.Detail, statusCode: StatusCodes.Status404NotFound, title: error.Title);
            case ServiceErrorKind.Conflict:
                return controller.Problem(detail: error.Detail, statusCode: StatusCodes.Status409Conflict, title: error.Title);
            case ServiceErrorKind.Forbidden:
                return controller.Problem(detail: error.Detail, statusCode: StatusCodes.Status403Forbidden, title: error.Title);
            default:
                throw new ArgumentOutOfRangeException(nameof(error), error.Kind, "Unknown service error kind.");
        }
    }
}
