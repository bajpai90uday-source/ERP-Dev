using System.Security.Cryptography;
using System.Text.Json;
using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService(
    ErpDbContext db,
    ILogger<SalesPurchaseService> logger) : ISalesPurchaseService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private async Task<MasterDataResult<T>> ExecuteIdempotentAsync<T>(
        Guid companyId,
        MasterDataActor actor,
        string key,
        string operation,
        object request,
        Func<Task<MasterDataResult<T>>> execute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100)
        {
            return Invalid<T>("Idempotency-Key", "Provide an Idempotency-Key of 1-100 characters.");
        }

        var requestHash = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions)));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var prior = await db.TransactionIdempotencyRecords.SingleOrDefaultAsync(
            value => value.CompanyId == companyId
                && value.ActorUserId == actor.UserId
                && value.Key == key,
            cancellationToken);
        if (prior is not null)
        {
            return Replay<T>(prior, operation, requestHash);
        }

        var idempotencyRecord = new TransactionIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ActorUserId = actor.UserId,
            Key = key,
            Operation = operation,
            RequestHash = requestHash,
        };
        db.TransactionIdempotencyRecords.Add(idempotencyRecord);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            var result = await execute();
            idempotencyRecord.ResponseJson = JsonSerializer.Serialize(result, JsonOptions);
            idempotencyRecord.StatusCode = result.StatusCode;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            logger.LogInformation(exception,
                "Duplicate transaction or idempotency key rejected for {Operation}.", operation);
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var committed = await db.TransactionIdempotencyRecords.AsNoTracking()
                .SingleOrDefaultAsync(value => value.CompanyId == companyId
                    && value.ActorUserId == actor.UserId
                    && value.Key == key, cancellationToken);
            if (committed is not null)
            {
                return Replay<T>(committed, operation, requestHash);
            }

            return Conflict<T>("A transaction with this key or business reference already exists.");
        }
        catch (DbUpdateConcurrencyException exception)
        {
            logger.LogInformation(exception,
                "Concurrent transaction change rejected for {Operation}.", operation);
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return Conflict<T>("The transaction changed while this request was running. Reload and retry.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static MasterDataResult<T> Replay<T>(
        TransactionIdempotencyRecord record,
        string operation,
        string requestHash)
    {
        if (!string.Equals(record.Operation, operation, StringComparison.Ordinal)
            || !string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return Conflict<T>(
                "This Idempotency-Key was already used for a different request.");
        }

        if (record.ResponseJson is null)
        {
            return Conflict<T>("The original request is still being processed. Retry shortly.");
        }

        return JsonSerializer.Deserialize<MasterDataResult<T>>(record.ResponseJson, JsonOptions)
            ?? throw new InvalidOperationException("Stored idempotency response could not be deserialized.");
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };

    private void AddEvent(
        Guid companyId,
        MasterDataActor actor,
        string aggregateType,
        Guid aggregateId,
        int? revisionNumber,
        string action,
        object details,
        string? reason = null) =>
        db.BusinessTransactionEvents.Add(new BusinessTransactionEvent
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            RevisionNumber = revisionNumber,
            Action = action,
            ActorUserId = actor.UserId,
            ActorSubject = actor.Subject,
            Reason = reason,
            DetailsJson = JsonSerializer.Serialize(details, JsonOptions),
            CorrelationId = actor.CorrelationId,
        });

    private async Task<IReadOnlyList<TransactionHistoryResponse>> GetHistoryAsync(
        Guid companyId,
        string aggregateType,
        Guid aggregateId,
        CancellationToken cancellationToken)
    {
        var events = db.BusinessTransactionEvents.AsNoTracking()
            .Where(value => value.CompanyId == companyId
                && value.AggregateType == aggregateType
                && value.AggregateId == aggregateId);
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var records = await events
                .Select(value => new
                {
                    value.Id,
                    Response = new TransactionHistoryResponse(
                        value.AggregateType,
                        value.AggregateId,
                        value.RevisionNumber,
                        value.Action,
                        value.ActorSubject,
                        value.Reason,
                        value.DetailsJson,
                        value.OccurredAt),
                })
                .ToListAsync(cancellationToken);
            return records.OrderBy(value => value.Response.OccurredAt)
                .ThenBy(value => value.Id)
                .Select(value => value.Response)
                .ToArray();
        }

        return await events.OrderBy(value => value.OccurredAt)
            .ThenBy(value => value.Id)
            .Select(value => new TransactionHistoryResponse(
                value.AggregateType,
                value.AggregateId,
                value.RevisionNumber,
                value.Action,
                value.ActorSubject,
                value.Reason,
                value.DetailsJson,
                value.OccurredAt))
            .ToArrayAsync(cancellationToken);
    }

    private static Dictionary<string, string[]> Validate(object request)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            request,
            new System.ComponentModel.DataAnnotations.ValidationContext(request),
            results,
            validateAllProperties: true);
        return results
            .SelectMany(result => result.MemberNames.DefaultIfEmpty(string.Empty)
                .Select(member => (member, error: result.ErrorMessage ?? "The value is invalid.")))
            .GroupBy(item => item.member, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.error).Distinct().ToArray(),
                StringComparer.Ordinal);
    }

    private static MasterDataResult<T> Invalid<T>(string field, string message) =>
        new(default, StatusCodes.Status400BadRequest,
            ValidationErrors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [field] = [message],
            });

    private static MasterDataResult<T> Invalid<T>(Dictionary<string, string[]> errors) =>
        new(default, StatusCodes.Status400BadRequest, ValidationErrors: errors);

    private static MasterDataResult<T> NotFound<T>() =>
        new(default, StatusCodes.Status404NotFound);

    private static MasterDataResult<T> Conflict<T>(string message) =>
        new(default, StatusCodes.Status409Conflict, message);

    private static bool IsCurrencyCode(string value) =>
        value.Length == 3 && value.All(character => character is >= 'A' and <= 'Z');

    private static bool IsValidRequestList<T>(IReadOnlyList<T>? values) =>
        values is { Count: > 0 and <= 500 };

    private static string NewDocumentNumber(string prefix) =>
        $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..(prefix.Length + 18)];
}
