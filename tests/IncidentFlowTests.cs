using foll_emergency_analytics.EmergencyAnalytics.Application.Internal.CommandServices;
using foll_emergency_analytics.EmergencyAnalytics.Application.Internal.Notifications;
using foll_emergency_analytics.EmergencyAnalytics.Application.OutboundServices;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Commands;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Entities;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Model.Enums;
using foll_emergency_analytics.EmergencyAnalytics.Domain.Repositories;
using foll_emergency_analytics.Shared.Domain.Repositories;
using Xunit;

namespace Foll.EmergencyAnalytics.Tests;

public class IncidentFlowTests
{
    [Fact]
    public async Task RepeatedDetectionUpdatesOpenIncidentWithoutAnotherOpeningAlert()
    {
        var fixture = new Fixture();
        var now = DateTime.UtcNow;
        await fixture.Service.Handle(new RegisterFallDetectedCommand(1001, 1, null, now, .9m, -12, -77, "Lima", "{}"));
        await fixture.Service.Handle(new RegisterFallDetectedCommand(1001, 2, null, now.AddSeconds(10), .95m, -12, -77, "Nueva dirección", "{}"));
        Assert.Single(fixture.Incidents.Items);
        var alert = Assert.Single(fixture.Outbox.Items);
        Assert.Equal(EmergencyAnalyticsEventTypes.IncidentOpenedV1, alert.Type);
        Assert.Contains("Lima", alert.Payload);
        Assert.Equal("Nueva dirección", fixture.Incidents.Items[0].Address);
        Assert.Equal(2, fixture.Commits.Count);
    }

    [Fact]
    public async Task UnauthorizedResolutionDoesNotCloseOrQueueAnEvent()
    {
        var fixture = new Fixture();
        await fixture.Service.Handle(new RegisterFallDetectedCommand(1001, 1, null, DateTime.UtcNow, null, null, null, null, "{}"));
        fixture.Access.Allowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.Handle(new ResolveFallIncidentCommand(1, 99, "Atendido", DateTime.UtcNow)));
        Assert.Equal(EmergencyIncidentStatus.Open, fixture.Incidents.Items[0].Status);
        Assert.Single(fixture.Outbox.Items);
        Assert.Equal(1, fixture.Commits.Count);
    }

    [Fact]
    public async Task UnassignedDeviceDoesNotCreateIncidentOrOutbox()
    {
        var fixture = new Fixture(); fixture.Assignment.Patient = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.Handle(
            new RegisterFallDetectedCommand(1001, 1, null, DateTime.UtcNow, null, null, null, null, "{}")));
        Assert.Empty(fixture.Incidents.Items);
        Assert.Empty(fixture.Outbox.Items);
        Assert.Equal(0, fixture.Commits.Count);
    }

    [Fact]
    public async Task ManualResolutionQueuesClosureWithActorAndObservation()
    {
        var fixture = new Fixture();
        await fixture.Service.Handle(new RegisterFallDetectedCommand(1001, 1, null, DateTime.UtcNow, null, null, null, null, "{}"));
        await fixture.Service.Handle(new ResolveFallIncidentCommand(1, 7, "Atendido", DateTime.UtcNow, "Ana Perez"));
        Assert.Equal(EmergencyIncidentStatus.Resolved, fixture.Incidents.Items[0].Status);
        Assert.Equal(EmergencyAnalyticsEventTypes.IncidentClosedV1, fixture.Outbox.Items[1].Type);
        Assert.Contains("Atendido", fixture.Outbox.Items[1].Payload);
        Assert.Contains("\"ClosedByUserId\":7", fixture.Outbox.Items[1].Payload);
        Assert.Contains("\"ClosedByName\":\"Ana Perez\"", fixture.Outbox.Items[1].Payload);
    }

    private sealed class Fixture
    {
        public IncidentRepo Incidents { get; } = new();
        public OutboxRepo Outbox { get; } = new();
        public CommitCounter Commits { get; } = new();
        public AccessService Access { get; } = new();
        public AssignmentService Assignment { get; } = new();
        public EmergencyIncidentCommandService Service => new(Incidents, new FallRepo(), Outbox, Assignment, Access, Commits);
    }
    private class MemoryRepo<T> : IBaseRepository<T> where T : class
    {
        public List<T> Items { get; } = [];
        public Task AddAsync(T value) { Items.Add(value); return Task.CompletedTask; }
        public Task<T?> FindByIdAsync(long id) => Task.FromResult(Items.FirstOrDefault());
        public void Update(T value) { }
        public void Remove(T value) => Items.Remove(value);
        public Task<IEnumerable<T>> ListAsync() => Task.FromResult<IEnumerable<T>>(Items);
    }
    private sealed class IncidentRepo : MemoryRepo<EmergencyIncident>, IEmergencyIncidentRepository
    {
        public Task<EmergencyIncident?> FindLatestOpenByDeviceIdAsync(long id) => Task.FromResult(Items.FirstOrDefault(i => i.DeviceId == id && i.Status == EmergencyIncidentStatus.Open));
        public Task<EmergencyIncident?> FindActiveByPatientIdAsync(long id) => Task.FromResult(Items.FirstOrDefault(i => i.PatientId == id && i.Status == EmergencyIncidentStatus.Open));
        public Task<EmergencyIncident?> FindByIdWithFallTypeAsync(long id) => FindByIdAsync(id);
        public Task<EmergencyIncident?> FindByIncidentKeyWithFallTypeAsync(Guid key) => Task.FromResult(Items.FirstOrDefault(i => i.IncidentKey == key));
        public Task<IReadOnlyCollection<EmergencyIncident>> ListByPatientIdAsync(long id) => Task.FromResult<IReadOnlyCollection<EmergencyIncident>>(Items.Where(i => i.PatientId == id).ToList());
        public Task DeleteByPatientIdsAsync(IEnumerable<long> ids) { Items.RemoveAll(i => ids.Contains(i.PatientId)); return Task.CompletedTask; }
    }
    private sealed class OutboxRepo : MemoryRepo<EmergencyOutboxMessage>, IEmergencyOutboxMessageRepository
    {
        public Task<IReadOnlyCollection<EmergencyOutboxMessage>> ListPendingAsync(int size) => Task.FromResult<IReadOnlyCollection<EmergencyOutboxMessage>>(Items.Where(i => i.ProcessedOn is null).Take(size).ToList());
    }
    private sealed class FallRepo : MemoryRepo<FallType>, IFallTypeRepository
    {
        public Task<FallType?> FindByIdAsync(short id) => Task.FromResult<FallType?>(new FallType(id, id == 1 ? "FRONTAL" : "LATERAL", "Caída", 1));
        public Task<FallType?> FindByNameAsync(string name) => Task.FromResult<FallType?>(null);
    }
    private sealed class CommitCounter : IUnitOfWork
    {
        public int Count { get; private set; }
        public Task CompleteAsync() { Count++; return Task.CompletedTask; }
    }
    private sealed class AccessService : IPatientIncidentAccessService
    {
        public bool Allowed { get; set; } = true;
        public Task<bool> CanAccessIncidentAsync(long actor, long patient) => Task.FromResult(Allowed);
    }
    private sealed class AssignmentService : IDeviceIncidentAssignmentService
    {
        public long? Patient { get; set; } = 25;
        public Task<long?> FindAssignedPatientIdAsync(long device) => Task.FromResult(Patient);
    }
}
