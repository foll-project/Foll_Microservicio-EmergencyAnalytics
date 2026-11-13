using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using Microsoft.EntityFrameworkCore;

namespace foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<EmergencyIncident> EmergencyIncidents => Set<EmergencyIncident>();
    public DbSet<FallType> FallTypes => Set<FallType>();
    public DbSet<EmergencyOutboxMessage> EmergencyOutboxMessages => Set<EmergencyOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder builder) =>
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
