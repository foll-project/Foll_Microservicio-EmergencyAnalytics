namespace foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;

public interface IDeviceIncidentAssignmentService
{
    Task<long?> FindAssignedPatientIdAsync(long deviceId);
}
