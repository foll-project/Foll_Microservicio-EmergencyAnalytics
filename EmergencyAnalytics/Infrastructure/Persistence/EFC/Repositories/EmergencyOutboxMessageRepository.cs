using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Repositories;
using Microsoft.EntityFrameworkCore;

namespace foll_emergency_analytics.EmergencyAnalytics.Infrastructure.Persistence.EFC.Repositories;

public class EmergencyOutboxMessageRepository : BaseRepository<EmergencyOutboxMessage>, IEmergencyOutboxMessageRepository
{
    public EmergencyOutboxMessageRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<IReadOnlyCollection<EmergencyOutboxMessage>> ListPendingAsync(int batchSize)
    {
        return await Context.Set<EmergencyOutboxMessage>()
            .Where(m => m.ProcessedOn == null)
            .OrderBy(m => m.OccurredOn)
            .Take(batchSize)
            .ToListAsync();
    }
}
