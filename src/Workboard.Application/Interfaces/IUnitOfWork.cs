namespace Workboard.Application.Interfaces;

public interface IUnitOfWork
{
    Task<IWriteTransaction> BeginWriteAsync(CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IWriteTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public enum WriteFailure { Conflict, Busy, DuplicateKey }

public class WriteException(WriteFailure failure, Exception innerException) : Exception("The write could not be completed.", innerException)
{
    public WriteFailure Failure { get; } = failure;
}
