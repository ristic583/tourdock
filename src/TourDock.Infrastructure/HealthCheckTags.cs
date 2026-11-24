namespace TourDock.Infrastructure;

public static class HealthCheckTags
{
    /// <summary>Checks that must pass before the instance can receive traffic (e.g. database reachable).</summary>
    public const string Ready = "ready";
}
