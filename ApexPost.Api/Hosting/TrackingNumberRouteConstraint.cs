using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Hosting;

/// <summary>
/// The <c>{trackingNumber:trackingNumber}</c> route constraint. A value that is not APX- followed by eight digits
/// does not match the route, so it gets 404 like any other unknown tracking number.
/// </summary>
internal sealed class TrackingNumberRouteConstraint : IRouteConstraint
{
    public const string Name = "trackingNumber";

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out var value) && TrackingNumber.IsValid(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
}
