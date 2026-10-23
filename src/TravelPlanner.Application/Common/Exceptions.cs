namespace TravelPlanner.Application.Common;

/// <param name="resourceKey">Localization key of the resource name, e.g. "resource.tripPlan".</param>
public sealed class NotFoundException(string resourceKey, object key)
    : Exception(T.Get("api.notFound", T.Get(resourceKey), key));

public sealed class ConflictException(string message) : Exception(message);

/// <summary>A planning failure whose message is safe (and localized) to show to users.</summary>
public sealed class PlanningException(string message) : Exception(message);
