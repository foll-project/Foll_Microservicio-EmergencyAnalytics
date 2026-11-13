using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.Shared.Domain.Repositories;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;

public interface IEmergencyIncidentRepository : IBaseRepository<EmergencyIncident>
{
    Task<EmergencyIncident?> FindLatestOpenByDeviceIdAsync(long deviceId);
    Task<EmergencyIncident?> FindActiveByPatientIdAsync(long patientId);
    Task<EmergencyIncident?> FindByIdWithFallTypeAsync(long incidentId);
    Task<EmergencyIncident?> FindByIncidentKeyWithFallTypeAsync(Guid incidentKey);
    Task<IReadOnlyCollection<EmergencyIncident>> ListByPatientIdAsync(long patientId);
    Task DeleteByPatientIdsAsync(IEnumerable<long> patientIds);
}
