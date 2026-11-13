namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;

public record DeleteEmergenciesByAccountCommand(IEnumerable<long> PatientIds);
