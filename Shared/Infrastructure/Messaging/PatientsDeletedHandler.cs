using foll_emergency_analytics.Shared.Infrastructure.Persistence.EFC.Configuration;
using Microsoft.EntityFrameworkCore;

namespace foll_emergency_analytics.Shared.Infrastructure.Messaging;

public record AccountDeletionCompleted(Guid EventId, Guid OperationId, long UserId, long[] DeletedPatientIds,
    DateTime OccurredAtUtc, int Version = 1)
{
    public const string EventType = "care.account-deletion-completed.v1";
}

public sealed class PatientsDeletedHandler(AppDbContext db) : IBrokerMessageHandler
{
    public async Task HandleAsync(BrokerEnvelope envelope, CancellationToken ct)
    {
        if (envelope.EventType != AccountDeletionCompleted.EventType) throw new InvalidDataException("Evento no soportado.");
        var message = envelope.Read<AccountDeletionCompleted>();
        if (message.UserId <= 0 || message.DeletedPatientIds is null || message.DeletedPatientIds.Any(id => id <= 0))
            throw new InvalidDataException("Pacientes eliminados invalidos.");
        if (message.DeletedPatientIds.Length == 0) return;
        var ids = message.DeletedPatientIds.Distinct().ToArray();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM emergency.outbox_messages WHERE (payload->>'PatientId')::bigint = ANY ({ids})", ct);
        await db.EmergencyIncidents.Where(i => ids.Contains(i.PatientId)).ExecuteDeleteAsync(ct);
    }
}
