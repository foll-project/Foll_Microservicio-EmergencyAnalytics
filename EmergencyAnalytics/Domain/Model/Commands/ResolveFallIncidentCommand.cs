namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;

public record ResolveFallIncidentCommand(
    long IncidentId,
    long ActorUserId,
    string? Observation,
    DateTime ResolvedAtUtc,
    string? ActorName = null);
