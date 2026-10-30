using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService
{
    private static readonly TimeSpan AtRiskWindow = TimeSpan.FromDays(3);

    public async Task<PageResponse<ProductionPlanResponse>> ListProductionPlansAsync(
        Guid companyId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.WorkOrders.AsNoTracking()
            .Where(value => value.CompanyId == companyId
                && value.Status != WorkOrderStatuses.Cancelled
                && value.Status != WorkOrderStatuses.Rejected);
        var total = await query.CountAsync(cancellationToken);
        var orders = await query.OrderBy(value => value.PlannedEndDate)
            .ThenBy(value => value.Priority)
            .ThenBy(value => value.WorkOrderNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var rows = new List<ProductionPlanResponse>(orders.Count);
        foreach (var order in orders)
        {
            rows.Add(await MapProductionPlanAsync(companyId, order, cancellationToken));
        }

        return new PageResponse<ProductionPlanResponse>(rows, page, pageSize, total);
    }

    public Task<MasterDataResult<ProductionPlanResponse>> UpdateProductionPlanAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        ProductionPlanRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "production-plan.update", new { id, request }, async () =>
            {
                var errors = Validate(request);
                var inputs = request.Operations ?? [];
                if (request.PlannedStartDate == default
                    || request.PlannedEndDate < request.PlannedStartDate)
                {
                    errors["plannedEndDate"] = ["Provide a valid production plan date range."];
                }

                if (inputs.Count is < 1 or > 100
                    || inputs.Select(value => value.OperationId).Distinct().Count() != inputs.Count)
                {
                    errors["operations"] = ["Provide each Work Order operation exactly once."];
                }

                if (errors.Count > 0)
                {
                    return Invalid<ProductionPlanResponse>(errors);
                }

                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<ProductionPlanResponse>();
                }

                if (workOrder.Status != WorkOrderStatuses.Approved
                    || await db.JobCards.AnyAsync(value =>
                        value.CompanyId == companyId && value.WorkOrderId == id,
                        cancellationToken))
                {
                    return Conflict<ProductionPlanResponse>(
                        "A production plan can only be changed on an approved Work Order before Job Cards are generated.");
                }

                var operations = await db.WorkOrderOperations.Where(value =>
                    value.CompanyId == companyId && value.WorkOrderId == id)
                    .OrderBy(value => value.Sequence)
                    .ToListAsync(cancellationToken);
                if (operations.Count != inputs.Count
                    || operations.Select(value => value.Id).Except(inputs.Select(value => value.OperationId)).Any())
                {
                    return Invalid<ProductionPlanResponse>(
                        "operations", "Include every operation belonging to this Work Order.");
                }

                var byId = inputs.ToDictionary(value => value.OperationId);
                var targets = operations.Select(value => byId[value.Id].TargetQuantity).ToArray();
                if (targets[^1] != workOrder.PlannedQuantity
                    || targets.Zip(targets.Skip(1), (current, next) => current >= next).Any(valid => !valid))
                {
                    return Invalid<ProductionPlanResponse>(
                        "operations", "Operation targets must not increase through the route, and the final target must equal the Work Order quantity.");
                }

                var machineIds = inputs.Where(value => value.MachineId.HasValue)
                    .Select(value => value.MachineId!.Value).Distinct().ToArray();
                var machines = await db.Machines.Where(value =>
                    value.CompanyId == companyId && value.IsActive
                    && machineIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
                if (machines.Count != machineIds.Length)
                {
                    return Invalid<ProductionPlanResponse>(
                        "operations", "Choose active machines belonging to this company.");
                }

                foreach (var operation in operations)
                {
                    var input = byId[operation.Id];
                    if (input.PlannedStartDate < request.PlannedStartDate
                        || input.PlannedEndDate > request.PlannedEndDate
                        || input.PlannedEndDate < input.PlannedStartDate)
                    {
                        return Invalid<ProductionPlanResponse>(
                            "operations", "Operation dates must be valid and within the Work Order schedule.");
                    }

                    if (input.MachineId is { } machineId
                        && machines[machineId].ProductionLine is { Length: > 0 } machineLine
                        && !string.Equals(machineLine, input.ProductionLine?.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return Invalid<ProductionPlanResponse>(
                            "operations", "The assigned machine belongs to a different production line.");
                    }

                    operation.PlannedStartDate = input.PlannedStartDate;
                    operation.PlannedEndDate = input.PlannedEndDate;
                    operation.PlannedTargetQuantity = input.TargetQuantity;
                    operation.ProductionLine = EmptyToNull(input.ProductionLine);
                    operation.MachineId = input.MachineId;
                    operation.AssignedTeam = EmptyToNull(input.AssignedTeam);
                }

                workOrder.Priority = request.Priority;
                workOrder.PlannedStartDate = request.PlannedStartDate;
                workOrder.PlannedEndDate = request.PlannedEndDate;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, id, "production_plan_updated",
                    new
                    {
                        workOrder.Priority,
                        workOrder.PlannedStartDate,
                        workOrder.PlannedEndDate,
                        operations = operations.Select(value => new
                        {
                            value.Id,
                            value.PlannedTargetQuantity,
                            value.ProductionLine,
                            value.MachineId,
                            value.AssignedTeam,
                            value.PlannedStartDate,
                            value.PlannedEndDate,
                        }),
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<ProductionPlanResponse>(
                    await MapProductionPlanAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public async Task<PageResponse<JobCardResponse>> ListJobCardsAsync(
        Guid companyId, int page, int pageSize, Guid? workOrderId,
        CancellationToken cancellationToken)
    {
        var query = db.JobCards.AsNoTracking().Where(value => value.CompanyId == companyId);
        if (workOrderId.HasValue)
        {
            query = query.Where(value => value.WorkOrderId == workOrderId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var cards = await query.OrderBy(value => value.JobCardNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var rows = new List<JobCardResponse>(cards.Count);
        foreach (var card in cards)
        {
            rows.Add(await MapJobCardAsync(companyId, card, cancellationToken));
        }

        return new PageResponse<JobCardResponse>(rows, page, pageSize, total);
    }

    public Task<MasterDataResult<IReadOnlyList<JobCardResponse>>> GenerateJobCardsAsync(
        Guid companyId, MasterDataActor actor, Guid workOrderId, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "job-card.generate", new { workOrderId }, async () =>
            {
                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == workOrderId, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<IReadOnlyList<JobCardResponse>>();
                }

                if (workOrder.Status is not (WorkOrderStatuses.Approved or WorkOrderStatuses.Released))
                {
                    return Conflict<IReadOnlyList<JobCardResponse>>(
                        "Job Cards can only be generated from an approved or released Work Order.");
                }

                await EnsureJobCardsAsync(companyId, actor, workOrder, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                var cards = await db.JobCards.AsNoTracking()
                    .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrderId)
                    .OrderBy(value => value.JobCardNumber)
                    .ToListAsync(cancellationToken);
                var responses = new List<JobCardResponse>(cards.Count);
                foreach (var card in cards)
                {
                    responses.Add(await MapJobCardAsync(companyId, card, cancellationToken));
                }

                return new MasterDataResult<IReadOnlyList<JobCardResponse>>(responses);
            }, cancellationToken);

    public Task<MasterDataResult<JobCardResponse>> ChangeJobCardStatusAsync(
        Guid companyId, MasterDataActor actor, Guid jobCardId, string idempotencyKey,
        string action, WorkOrderReasonRequest? request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            $"job-card.{action}", new { jobCardId, request }, async () =>
            {
                if (action == "hold")
                {
                    var errors = request is null
                        ? new Dictionary<string, string[]> { ["reason"] = ["A reason is required."] }
                        : ValidateWorkOrderReason(request);
                    if (errors.Count > 0)
                    {
                        return Invalid<JobCardResponse>(errors);
                    }
                }

                var card = await db.JobCards.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == jobCardId, cancellationToken);
                if (card is null)
                {
                    return NotFound<JobCardResponse>();
                }

                var workOrder = await db.WorkOrders.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == card.WorkOrderId, cancellationToken);
                if (workOrder.Status != WorkOrderStatuses.Released)
                {
                    return Conflict<JobCardResponse>("Job Cards can only progress while the Work Order is released.");
                }

                var operation = await db.WorkOrderOperations.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == card.WorkOrderOperationId,
                    cancellationToken);
                switch (action)
                {
                    case "start":
                        if (card.Status is not (JobCardStatuses.Ready or JobCardStatuses.PartiallyCompleted))
                        {
                            return Conflict<JobCardResponse>(
                                "Only a ready or partially completed Job Card can start.");
                        }

                        if (await db.JobCards.AnyAsync(value =>
                            value.CompanyId == companyId
                            && value.WorkOrderId == card.WorkOrderId
                            && value.Id != card.Id
                            && db.WorkOrderOperations.Any(previous =>
                                previous.CompanyId == companyId
                                && previous.Id == value.WorkOrderOperationId
                                && previous.Sequence < operation.Sequence)
                            && value.Status != JobCardStatuses.Completed,
                            cancellationToken))
                        {
                            return Conflict<JobCardResponse>(
                                "Complete earlier operation Job Cards before starting this one.");
                        }

                        if (await db.JobCards.AnyAsync(previous =>
                            previous.CompanyId == companyId
                            && previous.WorkOrderId == card.WorkOrderId
                            && previous.Id != card.Id
                            && db.WorkOrderOperations.Any(previousOperation =>
                                previousOperation.CompanyId == companyId
                                && previousOperation.Id == previous.WorkOrderOperationId
                                && previousOperation.Sequence < operation.Sequence)
                            && !db.QualityInspections.Any(inspection =>
                                inspection.CompanyId == companyId
                                && inspection.SourceType == "job_card"
                                && inspection.SourceId == previous.Id
                                && inspection.Stage == "in_process"
                                && inspection.Status == QualityStatuses.Approved
                                && inspection.AcceptedQuantity == inspection.InspectedQuantity),
                            cancellationToken))
                        {
                            return Conflict<JobCardResponse>(
                                "Approve quality inspection for each prior operation before starting this Job Card.");
                        }

                        card.Status = JobCardStatuses.InProgress;
                        card.StartedAt ??= DateTimeOffset.UtcNow;
                        break;
                    case "hold":
                        if (card.Status != JobCardStatuses.InProgress)
                        {
                            return Conflict<JobCardResponse>(
                                "Only an in-progress Job Card can be placed on hold.");
                        }

                        card.Status = JobCardStatuses.OnHold;
                        break;
                    case "resume":
                        if (card.Status != JobCardStatuses.OnHold)
                        {
                            return Conflict<JobCardResponse>(
                                "Only a Job Card on hold can be resumed.");
                        }

                        card.Status = JobCardStatuses.InProgress;
                        break;
                    default:
                        return Invalid<JobCardResponse>(
                            "action", "Unsupported Job Card state transition.");
                }

                card.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, card.WorkOrderId,
                    $"job_card_{action}", new { card.Id, card.JobCardNumber, card.Status },
                    request?.Reason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<JobCardResponse>(
                    await MapJobCardAsync(companyId, card, cancellationToken));
            }, cancellationToken);

    public async Task<IReadOnlyList<ProductionConfirmationResponse>> ListProductionConfirmationsAsync(
        Guid companyId, Guid jobCardId, CancellationToken cancellationToken)
    {
        var exists = await db.JobCards.AnyAsync(value =>
            value.CompanyId == companyId && value.Id == jobCardId, cancellationToken);
        if (!exists)
        {
            return [];
        }

        var rows = await db.ProductionConfirmations.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.JobCardId == jobCardId)
            .OrderBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(MapProductionConfirmation).ToArray();
    }

    public async Task<MasterDataResult<ProductionConfirmationResponse>> GetProductionConfirmationAsync(
        Guid companyId, Guid confirmationId, CancellationToken cancellationToken)
    {
        var value = await db.ProductionConfirmations.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == companyId && row.Id == confirmationId, cancellationToken);
        return value is null
            ? NotFound<ProductionConfirmationResponse>()
            : new MasterDataResult<ProductionConfirmationResponse>(MapProductionConfirmation(value));
    }

    public Task<MasterDataResult<ProductionConfirmationResponse>> CreateProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid jobCardId, string idempotencyKey,
        ProductionConfirmationRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "production-confirmation.create", new { jobCardId, request }, async () =>
            {
                var errors = Validate(request);
                var reported = QuantityTotal(request.GoodQuantity, request.RejectedQuantity,
                    request.ReworkQuantity, request.ScrapQuantity);
                if (reported <= 0m)
                {
                    errors["goodQuantity"] = ["At least one production quantity must be greater than zero."];
                }

                if (request.StartedAt == default || request.CompletedAt < request.StartedAt
                    || request.CompletedAt > DateTimeOffset.UtcNow.AddMinutes(1))
                {
                    errors["completedAt"] = ["Provide a valid start and completion timestamp not in the future."];
                }

                if (errors.Count > 0)
                {
                    return Invalid<ProductionConfirmationResponse>(errors);
                }

                var card = await db.JobCards.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == jobCardId, cancellationToken);
                if (card is null)
                {
                    return NotFound<ProductionConfirmationResponse>();
                }

                var workOrder = await db.WorkOrders.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == card.WorkOrderId,
                    cancellationToken);
                if (workOrder.Status != WorkOrderStatuses.Released
                    || card.Status != JobCardStatuses.InProgress)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Production can only be confirmed against an in-progress Job Card on a released Work Order.");
                }

                if (await db.ProductionConfirmations.AnyAsync(value =>
                    value.CompanyId == companyId && value.JobCardId == jobCardId
                    && value.Status == ProductionConfirmationStatuses.PendingApproval,
                    cancellationToken))
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Resolve the pending production confirmation before submitting another.");
                }

                var processed = await GetProcessedQuantityAsync(companyId, jobCardId, cancellationToken);
                if (processed + reported > card.PlannedQuantity)
                {
                    return Invalid<ProductionConfirmationResponse>(
                        "quantities", "Reported quantity exceeds the Job Card's outstanding target.");
                }

                var machineId = request.MachineId ?? card.MachineId;
                var machine = machineId is { } id
                    ? await db.Machines.SingleOrDefaultAsync(value =>
                        value.CompanyId == companyId && value.Id == id && value.IsActive,
                        cancellationToken)
                    : null;
                if (machineId.HasValue && machine is null)
                {
                    return Invalid<ProductionConfirmationResponse>(
                        "machineId", "Choose an active machine belonging to this company.");
                }

                var line = EmptyToNull(request.ProductionLine) ?? card.ProductionLine;
                if (machine?.ProductionLine is { Length: > 0 } machineLine
                    && line is not null
                    && !string.Equals(machineLine, line, StringComparison.OrdinalIgnoreCase))
                {
                    return Invalid<ProductionConfirmationResponse>(
                        "productionLine", "The selected machine belongs to a different production line.");
                }

                var confirmation = new ProductionConfirmation
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    WorkOrderId = workOrder.Id,
                    JobCardId = card.Id,
                    GoodQuantity = request.GoodQuantity,
                    RejectedQuantity = request.RejectedQuantity,
                    ReworkQuantity = request.ReworkQuantity,
                    ScrapQuantity = request.ScrapQuantity,
                    Shift = EmptyToNull(request.Shift),
                    ProductionLine = line ?? machine?.ProductionLine,
                    MachineId = machineId,
                    Team = EmptyToNull(request.Team) ?? card.AssignedTeam,
                    StartedAt = request.StartedAt,
                    CompletedAt = request.CompletedAt,
                    CreatedBySubject = actor.Subject,
                };
                card.ConcurrencyVersion++;
                db.ProductionConfirmations.Add(confirmation);
                AddWorkOrderEvent(companyId, actor, workOrder.Id, "production_confirmation_submitted",
                    new
                    {
                        confirmation.Id,
                        confirmation.JobCardId,
                        confirmation.GoodQuantity,
                        confirmation.RejectedQuantity,
                        confirmation.ReworkQuantity,
                        confirmation.ScrapQuantity,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<ProductionConfirmationResponse>(
                    MapProductionConfirmation(confirmation),
                    StatusCodes.Status201Created,
                    ResourceId: confirmation.Id);
            }, cancellationToken);

    public Task<MasterDataResult<ProductionConfirmationResponse>> ApproveProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid confirmationId, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "production-confirmation.approve", new { confirmationId }, async () =>
            {
                var confirmation = await db.ProductionConfirmations.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == confirmationId, cancellationToken);
                if (confirmation is null)
                {
                    return NotFound<ProductionConfirmationResponse>();
                }

                if (confirmation.Status != ProductionConfirmationStatuses.PendingApproval)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Only a pending production confirmation can be approved.");
                }

                if (string.Equals(confirmation.CreatedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "The confirmation submitter cannot approve the same confirmation.");
                }

                var workOrder = await db.WorkOrders.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == confirmation.WorkOrderId,
                    cancellationToken);
                var card = await db.JobCards.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == confirmation.JobCardId,
                    cancellationToken);
                if (workOrder.Status != WorkOrderStatuses.Released)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Production confirmations can only be approved while the Work Order is released.");
                }

                var effectiveQuantity = await GetProcessedQuantityAsync(
                    companyId, card.Id, cancellationToken);
                var effectiveGood = await GetAcceptedGoodQuantityAsync(
                    companyId, card.Id, cancellationToken);
                var oldConfirmation = confirmation.CorrectsConfirmationId is { } oldId
                    ? await db.ProductionConfirmations.SingleOrDefaultAsync(value =>
                        value.CompanyId == companyId && value.Id == oldId
                        && value.Status == ProductionConfirmationStatuses.Approved,
                        cancellationToken)
                    : null;
                if (confirmation.CorrectsConfirmationId.HasValue && oldConfirmation is null)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "The confirmation being corrected is no longer the current approved record.");
                }

                var total = QuantityTotal(confirmation.GoodQuantity, confirmation.RejectedQuantity,
                    confirmation.ReworkQuantity, confirmation.ScrapQuantity);
                var processedAfter = effectiveQuantity
                    - (oldConfirmation is null ? 0m : QuantityTotal(
                        oldConfirmation.GoodQuantity, oldConfirmation.RejectedQuantity,
                        oldConfirmation.ReworkQuantity, oldConfirmation.ScrapQuantity))
                    + total;
                if (processedAfter > card.PlannedQuantity)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Approved production would exceed the Job Card target.");
                }

                var goodDelta = confirmation.GoodQuantity
                    - (oldConfirmation?.GoodQuantity ?? 0m);
                var operation = await db.WorkOrderOperations.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == card.WorkOrderOperationId,
                    cancellationToken);
                var isFinalOperation = !await db.WorkOrderOperations.AnyAsync(value =>
                    value.CompanyId == companyId && value.WorkOrderId == workOrder.Id
                    && value.Sequence > operation.Sequence, cancellationToken);
                if (isFinalOperation && goodDelta != 0m)
                {
                    var inventoryError = await ApplyAcceptedGoodInventoryAsync(
                        companyId, actor, workOrder, goodDelta, confirmation.Id,
                        cancellationToken);
                    if (inventoryError is not null)
                    {
                        return Conflict<ProductionConfirmationResponse>(inventoryError);
                    }
                }

                if (oldConfirmation is not null)
                {
                    oldConfirmation.Status = ProductionConfirmationStatuses.Superseded;
                }

                confirmation.Status = ProductionConfirmationStatuses.Approved;
                confirmation.ApprovedBySubject = actor.Subject;
                confirmation.ApprovedAt = DateTimeOffset.UtcNow;
                card.ConcurrencyVersion++;
                card.Status = processedAfter >= card.PlannedQuantity
                    ? JobCardStatuses.Completed
                    : JobCardStatuses.PartiallyCompleted;
                card.CompletedAt = card.Status == JobCardStatuses.Completed
                    ? confirmation.ApprovedAt
                    : null;
                operation.Status = card.Status == JobCardStatuses.Completed ? "completed" : "pending";
                operation.CompletedAt = card.CompletedAt;
                operation.CompletedBySubject = card.CompletedAt is null ? null : actor.Subject;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, workOrder.Id,
                    confirmation.CorrectsConfirmationId.HasValue
                        ? "production_confirmation_corrected"
                        : "production_confirmation_approved",
                    new
                    {
                        confirmation.Id,
                        supersededConfirmationId = oldConfirmation?.Id,
                        acceptedGoodDelta = goodDelta,
                        jobCardProcessedQuantity = processedAfter,
                        jobCardTargetQuantity = card.PlannedQuantity,
                    },
                    confirmation.CorrectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<ProductionConfirmationResponse>(
                    MapProductionConfirmation(confirmation));
            }, cancellationToken);

    public Task<MasterDataResult<ProductionConfirmationResponse>> RejectProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid confirmationId, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "production-confirmation.reject", new { confirmationId, request }, async () =>
            {
                var errors = ValidateWorkOrderReason(request);
                if (errors.Count > 0)
                {
                    return Invalid<ProductionConfirmationResponse>(errors);
                }

                var confirmation = await db.ProductionConfirmations.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == confirmationId, cancellationToken);
                if (confirmation is null)
                {
                    return NotFound<ProductionConfirmationResponse>();
                }

                if (confirmation.Status != ProductionConfirmationStatuses.PendingApproval)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Only a pending production confirmation can be rejected.");
                }

                if (string.Equals(confirmation.CreatedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "The confirmation submitter cannot reject the same confirmation.");
                }

                confirmation.Status = ProductionConfirmationStatuses.Rejected;
                confirmation.RejectionReason = request.Reason.Trim();
                AddWorkOrderEvent(companyId, actor, confirmation.WorkOrderId,
                    "production_confirmation_rejected",
                    new { confirmation.Id, confirmation.JobCardId }, confirmation.RejectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<ProductionConfirmationResponse>(
                    MapProductionConfirmation(confirmation));
            }, cancellationToken);

    public Task<MasterDataResult<ProductionConfirmationResponse>> CorrectProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid confirmationId, string idempotencyKey,
        ProductionConfirmationCorrectionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "production-confirmation.correct", new { confirmationId, request }, async () =>
            {
                var errors = Validate(request);
                if (QuantityTotal(request.GoodQuantity, request.RejectedQuantity,
                        request.ReworkQuantity, request.ScrapQuantity) <= 0m)
                {
                    errors["quantities"] = ["Corrected production quantities must total more than zero."];
                }

                if (string.IsNullOrWhiteSpace(request.Reason))
                {
                    errors["reason"] = ["Provide a correction reason."];
                }

                if (errors.Count > 0)
                {
                    return Invalid<ProductionConfirmationResponse>(errors);
                }

                var original = await db.ProductionConfirmations.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == confirmationId, cancellationToken);
                if (original is null)
                {
                    return NotFound<ProductionConfirmationResponse>();
                }

                if (original.Status != ProductionConfirmationStatuses.Approved)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Only the current approved confirmation can be corrected.");
                }

                var workOrder = await db.WorkOrders.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == original.WorkOrderId,
                    cancellationToken);
                if (workOrder.Status != WorkOrderStatuses.Released)
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Corrections are only permitted before the Work Order is completed or cancelled.");
                }

                if (await db.ProductionConfirmations.AnyAsync(value =>
                    value.CompanyId == companyId && value.JobCardId == original.JobCardId
                    && value.Status == ProductionConfirmationStatuses.PendingApproval,
                    cancellationToken))
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Resolve the pending confirmation before creating a correction.");
                }

                var card = await db.JobCards.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == original.JobCardId,
                    cancellationToken);
                var operation = await db.WorkOrderOperations.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == card.WorkOrderOperationId,
                    cancellationToken);
                if (await (
                    from laterCard in db.JobCards
                    join laterOperation in db.WorkOrderOperations
                        on new { laterCard.CompanyId, Id = laterCard.WorkOrderOperationId }
                        equals new { laterOperation.CompanyId, laterOperation.Id }
                    join laterConfirmation in db.ProductionConfirmations
                        on new { laterCard.CompanyId, JobCardId = laterCard.Id }
                        equals new { laterConfirmation.CompanyId, laterConfirmation.JobCardId }
                    where laterCard.CompanyId == companyId
                        && laterCard.WorkOrderId == workOrder.Id
                        && laterOperation.Sequence > operation.Sequence
                        && laterConfirmation.Status == ProductionConfirmationStatuses.Approved
                    select laterConfirmation.Id).AnyAsync(cancellationToken))
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "Correct earlier operations before any later operation has approved output.");
                }

                var currentTotal = await GetProcessedQuantityAsync(
                    companyId, card.Id, cancellationToken);
                if (await db.QualityInspections.AnyAsync(value =>
                    value.CompanyId == companyId && value.SourceType == "job_card"
                    && value.SourceId == card.Id && value.Status == QualityStatuses.Approved,
                    cancellationToken))
                {
                    return Conflict<ProductionConfirmationResponse>(
                        "A production confirmation cannot be corrected after quality has approved the Job Card output.");
                }
                var oldTotal = QuantityTotal(original.GoodQuantity, original.RejectedQuantity,
                    original.ReworkQuantity, original.ScrapQuantity);
                var replacementTotal = QuantityTotal(request.GoodQuantity, request.RejectedQuantity,
                    request.ReworkQuantity, request.ScrapQuantity);
                if (currentTotal - oldTotal + replacementTotal > card.PlannedQuantity)
                {
                    return Invalid<ProductionConfirmationResponse>(
                        "quantities", "Corrected quantities exceed the Job Card target.");
                }

                var correction = new ProductionConfirmation
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    WorkOrderId = original.WorkOrderId,
                    JobCardId = original.JobCardId,
                    CorrectsConfirmationId = original.Id,
                    GoodQuantity = request.GoodQuantity,
                    RejectedQuantity = request.RejectedQuantity,
                    ReworkQuantity = request.ReworkQuantity,
                    ScrapQuantity = request.ScrapQuantity,
                    Shift = original.Shift,
                    ProductionLine = original.ProductionLine,
                    MachineId = original.MachineId,
                    Team = original.Team,
                    StartedAt = original.StartedAt,
                    CompletedAt = original.CompletedAt,
                    CreatedBySubject = actor.Subject,
                    CorrectionReason = request.Reason.Trim(),
                };
                card.ConcurrencyVersion++;
                db.ProductionConfirmations.Add(correction);
                AddWorkOrderEvent(companyId, actor, workOrder.Id,
                    "production_confirmation_correction_submitted",
                    new { correction.Id, correctsConfirmationId = original.Id },
                    correction.CorrectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<ProductionConfirmationResponse>(
                    MapProductionConfirmation(correction),
                    StatusCodes.Status201Created,
                    ResourceId: correction.Id);
            }, cancellationToken);

    private async Task EnsureJobCardsAsync(
        Guid companyId, MasterDataActor actor, WorkOrder workOrder,
        CancellationToken cancellationToken)
    {
        var operations = await db.WorkOrderOperations
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrder.Id)
            .OrderBy(value => value.Sequence)
            .ToListAsync(cancellationToken);
        var existingOperationIds = await db.JobCards.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrder.Id)
            .Select(value => value.WorkOrderOperationId)
            .ToHashSetAsync(cancellationToken);
        foreach (var operation in operations.Where(value => !existingOperationIds.Contains(value.Id)))
        {
            var card = new JobCard
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                WorkOrderId = workOrder.Id,
                WorkOrderOperationId = operation.Id,
                JobCardNumber = $"{workOrder.WorkOrderNumber}-JC-{operation.Sequence:D3}",
                PlannedQuantity = operation.PlannedTargetQuantity,
                ProductionLine = operation.ProductionLine,
                MachineId = operation.MachineId,
                AssignedTeam = operation.AssignedTeam,
                Status = JobCardStatuses.Ready,
                CreatedBySubject = actor.Subject,
            };
            db.JobCards.Add(card);
            AddWorkOrderEvent(companyId, actor, workOrder.Id, "job_card_generated",
                new { card.Id, card.JobCardNumber, operationId = operation.Id });
        }
    }

    private async Task<ProductionPlanResponse> MapProductionPlanAsync(
        Guid companyId, WorkOrder workOrder, CancellationToken cancellationToken)
    {
        var operations = await db.WorkOrderOperations.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrder.Id)
            .OrderBy(value => value.Sequence)
            .Select(value => new WorkOrderOperationResponse(
                value.Id, value.Sequence, value.ProductionOperationId,
                value.OperationCodeSnapshot, value.OperationNameSnapshot,
                value.PlannedStartDate, value.PlannedEndDate, value.PlannedTargetQuantity,
                value.ProductionLine, value.MachineId, value.AssignedTeam, value.Status,
                value.CompletedAt, value.CompletedBySubject))
            .ToArrayAsync(cancellationToken);
        var finalOperationId = operations.LastOrDefault()?.Id;
        var card = finalOperationId is null
            ? null
            : await db.JobCards.AsNoTracking().SingleOrDefaultAsync(value =>
                value.CompanyId == companyId && value.WorkOrderOperationId == finalOperationId,
                cancellationToken);
        var processed = card is null
            ? 0m
            : await GetProcessedQuantityAsync(companyId, card.Id, cancellationToken);
        var accepted = card is null
            ? 0m
            : await GetAcceptedGoodQuantityAsync(companyId, card.Id, cancellationToken);
        var outstanding = Math.Max(0m, workOrder.PlannedQuantity - processed);
        var scheduleStatus = outstanding == 0m || workOrder.Status == WorkOrderStatuses.Completed
            ? "completed"
            : workOrder.PlannedEndDate < DateOnly.FromDateTime(DateTime.UtcNow)
                ? "delayed"
                : workOrder.PlannedEndDate <= DateOnly.FromDateTime(DateTime.UtcNow.Add(AtRiskWindow))
                    ? "at_risk"
                    : "on_track";
        var downtimeEvents = await db.MachineDowntimes.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrder.Id)
            .ToListAsync(cancellationToken);
        var downtimeHours = downtimeEvents.Sum(value =>
            Math.Max(0m, (decimal)((value.EndedAt ?? DateTimeOffset.UtcNow) - value.StartedAt).TotalHours));
        if (outstanding > 0m && downtimeEvents.Count > 0 && scheduleStatus == "on_track")
        {
            scheduleStatus = "at_risk";
        }
        return new ProductionPlanResponse(
            workOrder.Id, workOrder.WorkOrderNumber, workOrder.Priority,
            workOrder.PlannedStartDate, workOrder.PlannedEndDate,
            workOrder.PlannedQuantity, processed, accepted, outstanding,
            scheduleStatus, workOrder.Status, downtimeEvents.Count, downtimeHours, operations);
    }

    private async Task<JobCardResponse> MapJobCardAsync(
        Guid companyId, JobCard card, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == card.WorkOrderId, cancellationToken);
        var operation = await db.WorkOrderOperations.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == card.WorkOrderOperationId,
            cancellationToken);
        var processed = await GetProcessedQuantityAsync(companyId, card.Id, cancellationToken);
        return new JobCardResponse(
            card.Id, card.JobCardNumber, card.WorkOrderId, workOrder.WorkOrderNumber,
            card.WorkOrderOperationId, operation.Sequence, operation.OperationCodeSnapshot,
            operation.OperationNameSnapshot, card.PlannedQuantity, processed,
            Math.Max(0m, card.PlannedQuantity - processed), card.ProductionLine,
            card.MachineId, card.AssignedTeam, card.Status, card.CreatedAt,
            card.StartedAt, card.CompletedAt);
    }

    private async Task<decimal> GetProcessedQuantityAsync(
        Guid companyId, Guid jobCardId, CancellationToken cancellationToken) =>
        await db.ProductionConfirmations.AsNoTracking()
            .Where(value => value.CompanyId == companyId
                && value.JobCardId == jobCardId
                && value.Status == ProductionConfirmationStatuses.Approved)
            .SumAsync(value => (decimal?)(
                value.GoodQuantity + value.RejectedQuantity
                + value.ReworkQuantity + value.ScrapQuantity), cancellationToken) ?? 0m;

    private async Task<decimal> GetAcceptedGoodQuantityAsync(
        Guid companyId, Guid jobCardId, CancellationToken cancellationToken) =>
        await db.ProductionConfirmations.AsNoTracking()
            .Where(value => value.CompanyId == companyId
                && value.JobCardId == jobCardId
                && value.Status == ProductionConfirmationStatuses.Approved)
            .SumAsync(value => (decimal?)value.GoodQuantity, cancellationToken)
            ?? 0m;

    private async Task<string?> ApplyAcceptedGoodInventoryAsync(
        Guid companyId, MasterDataActor actor, WorkOrder workOrder, decimal quantityDelta,
        Guid confirmationId, CancellationToken cancellationToken)
    {
        var salesLine = await db.SalesOrderLines.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == workOrder.SalesOrderLineId,
            cancellationToken);
        var variant = await db.ProductVariants.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == salesLine.ProductVariantId,
            cancellationToken);
        var balance = await GetOrCreateStockBalanceAsync(
            companyId, variant.ItemId, workOrder.OutputWarehouseId,
            InventoryStockStatuses.Quarantine, cancellationToken);
        if (quantityDelta < 0m
            && balance.OnHandQuantity - balance.ReservedQuantity < -quantityDelta)
        {
            return "Quality-held finished goods have already been released or consumed; this correction cannot reduce their balance.";
        }

        balance.OnHandQuantity += quantityDelta;
        balance.ConcurrencyVersion++;
        db.InventoryLedgerEntries.Add(new InventoryLedgerEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ItemId = variant.ItemId,
            WarehouseId = workOrder.OutputWarehouseId,
            StockStatus = InventoryStockStatuses.Quarantine,
            TransactionType = quantityDelta > 0m ? "receipt" : "adjustment",
            Quantity = quantityDelta,
            ReferenceType = "production-confirmation",
            ReferenceId = workOrder.Id,
            ReferenceLineId = confirmationId,
            ActorSubject = actor.Subject,
        });
        return null;
    }

    private static ProductionConfirmationResponse MapProductionConfirmation(
        ProductionConfirmation value) =>
        new(
            value.Id, value.WorkOrderId, value.JobCardId, value.CorrectsConfirmationId,
            value.GoodQuantity, value.RejectedQuantity, value.ReworkQuantity,
            value.ScrapQuantity, value.Shift, value.ProductionLine, value.MachineId,
            value.Team, value.StartedAt, value.CompletedAt, value.Status,
            value.CreatedBySubject, value.CreatedAt, value.ApprovedBySubject,
            value.ApprovedAt, value.CorrectionReason, value.RejectionReason);

    private static decimal QuantityTotal(
        decimal good, decimal rejected, decimal rework, decimal scrap) =>
        good + rejected + rework + scrap;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
