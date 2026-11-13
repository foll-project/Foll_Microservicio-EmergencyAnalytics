using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.Shared.Domain.Repositories;

namespace foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;

public interface IFallTypeRepository : IBaseRepository<FallType>
{
    Task<FallType?> FindByIdAsync(short id);
    Task<FallType?> FindByNameAsync(string name);
}
