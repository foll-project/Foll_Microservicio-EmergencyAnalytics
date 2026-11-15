using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Foll.EmergencyAnalytics.Tests;

public class ArchitectureAndTransitionTests
{
    [Fact]
    public void ModelOwnsOnlyEmergencyTablesAndHasDistinctOpenIndex()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(
            "Host=localhost;Database=not_used;Username=test;Password=test").UseSnakeCaseNamingConvention().Options);
        Assert.Equal(4, db.Model.GetEntityTypes().Count());
        Assert.All(db.Model.GetEntityTypes(), entity => Assert.Equal("emergency", entity.GetSchema()));
        var entity = db.Model.FindEntityType(typeof(EmergencyIncident))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique && index.GetFilter() == "\"status\" = 'Open'");
        Assert.Contains(entity.GetIndexes(), index => !index.IsUnique && index.Properties.SingleOrDefault()?.Name == "DeviceId");
        Assert.DoesNotContain(typeof(EmergencyIncident).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "foll-backend" || assembly.Name == "Foll-Care");
    }

}
