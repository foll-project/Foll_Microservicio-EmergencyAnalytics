using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.Shared.Domain.Repositories;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;

public interface IEmergencyOutboxMessageRepository : IBaseRepository<EmergencyOutboxMessage>
{
    Task<IReadOnlyCollection<EmergencyOutboxMessage>> ListPendingAsync(int batchSize);
}
