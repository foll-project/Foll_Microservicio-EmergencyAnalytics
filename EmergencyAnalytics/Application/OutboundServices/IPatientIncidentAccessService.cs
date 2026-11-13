namespace foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;

public interface IPatientIncidentAccessService
{
    Task<bool> CanAccessIncidentAsync(long actorUserId, long patientId);
}
