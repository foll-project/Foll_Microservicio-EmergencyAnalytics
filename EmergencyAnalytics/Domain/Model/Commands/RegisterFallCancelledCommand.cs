using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Enums;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;

public record RegisterFallCancelledCommand(
    long DeviceId,
    DateTime ReportedAtUtc,
    EmergencyCancellationReason Reason,
    string? RawPayload);
