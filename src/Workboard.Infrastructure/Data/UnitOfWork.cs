using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workboard.Application.Interfaces;

namespace Workboard.Infrastructure.Data;

public class UnitOfWork(ApplicationDbContext context) : IUnitOfWork
{
    public async Task<IWriteTransaction> BeginWriteAsync(CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        SqliteTransaction? transaction = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction = ((SqliteConnection)context.Database.GetDbConnection()).BeginTransaction(deferred: false);
            var attached = await context.Database.UseTransactionAsync(transaction, cancellationToken);
            return new WriteTransaction(context, transaction, attached!);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            transaction?.Dispose();
            await context.Database.CloseConnectionAsync();
            throw new WriteException(WriteFailure.Busy, exception);
        }
        catch
        {
            transaction?.Dispose();
            await context.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new WriteException(WriteFailure.Conflict, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            throw new WriteException(WriteFailure.Busy, exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 } sqlite
            && sqlite.Message.Contains("Projects.Key", StringComparison.Ordinal))
        {
            throw new WriteException(WriteFailure.DuplicateKey, exception);
        }
    }
}

internal class WriteTransaction(ApplicationDbContext context, SqliteTransaction transaction, IDbContextTransaction attached) : IWriteTransaction
{
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await attached.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            throw new WriteException(WriteFailure.Busy, exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await attached.DisposeAsync();
        }
        finally
        {
            await transaction.DisposeAsync();
            await context.Database.CloseConnectionAsync();
        }
    }
}
