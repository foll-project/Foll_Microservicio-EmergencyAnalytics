namespace foll_emergency_analytics.Shared.Domain.Repositories;

public interface IUnitOfWork
{
    Task CompleteAsync();
}
