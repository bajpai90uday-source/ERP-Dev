using System.Text.Json;
using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Operations;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService
{
    public async Task<PageResponse<WorkOrderResponse>> ListWorkOrdersAsync(
        Guid companyId,
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        var query = db.WorkOrders.AsNoTracking().Where(value => value.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(value => value.WorkOrderNumber.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderByDescending(value => value.WorkOrderNumber)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var rows = new List<WorkOrderResponse>(records.Count);
        foreach (var record in records)
        {
            rows.Add(await MapWorkOrderAsync(companyId, record, cancellationToken));
        }

        return new PageResponse<WorkOrderResponse>(rows, page, pageSize, total);
    }

    public async Task<MasterDataResult<WorkOrderResponse>> GetWorkOrderAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(value =>
            value.CompanyId == companyId && value.Id == id, cancellationToken);
        return workOrder is null
            ? NotFound<WorkOrderResponse>()
            : new MasterDataResult<WorkOrderResponse>(
                await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
    }

    public Task<MasterDataResult<WorkOrderResponse>> CreateWorkOrderAsync(
        Guid companyId,
        MasterDataActor actor,
        string idempotencyKey,
        WorkOrderRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.create", request, async () =>
            {
                var operationRequests = request.Operations ?? [];
                var validation = Validate(request);
                if (operationRequests is not { Count: > 0 and <= 100 })
                {
                    validation["operations"] = ["Add between 1 and 100 production operations."];
                }

                if (request.PlannedStartDate == default
                    || request.PlannedEndDate < request.PlannedStartDate)
                {
                    validation["plannedEndDate"] = ["Provide a valid planned date range."];
                }

                if (operationRequests.Select(value => value.Sequence).Distinct().Count()
                    != operationRequests.Count)
                {
                    validation["operations"] = ["Operation sequence values must be unique."];
                }

                if (validation.Count > 0)
                {
                    return Invalid<WorkOrderResponse>(validation);
                }

                var order = await db.SalesOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == request.SalesOrderId, cancellationToken);
                if (order is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                var orderRevision = await db.SalesOrderRevisions.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId
                    && value.SalesOrderId == order.Id
                    && value.RevisionNumber == order.CurrentRevisionNumber,
                    cancellationToken);
                if (orderRevision?.Status != OrderStatuses.Confirmed)
                {
                    return Conflict<WorkOrderResponse>(
                        "A Work Order can only be created from the current confirmed Sales Order revision.");
                }

                var salesLine = await db.SalesOrderLines.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId
                    && value.SalesOrderRevisionId == orderRevision.Id
                    && value.Id == request.SalesOrderLineId, cancellationToken);
                if (salesLine is null)
                {
                    return Invalid<WorkOrderResponse>(
                        "salesOrderLineId", "Choose a line from the confirmed Sales Order.");
                }

                var fulfillment = await db.SalesOrderFulfillments.AsNoTracking()
                    .Where(value => value.CompanyId == companyId
                        && value.SalesOrderLineId == salesLine.Id)
                    .SumAsync(value => (decimal?)value.Quantity, cancellationToken) ?? 0m;
                var alreadyPlanned = await db.WorkOrders.AsNoTracking()
                    .Where(value => value.CompanyId == companyId
                        && value.SalesOrderLineId == salesLine.Id
                        && value.Status != WorkOrderStatuses.Cancelled
                        && value.Status != WorkOrderStatuses.Rejected)
                    .SumAsync(value => (decimal?)value.PlannedQuantity, cancellationToken) ?? 0m;
                if (request.PlannedQuantity > salesLine.Quantity - fulfillment - alreadyPlanned)
                {
                    return Invalid<WorkOrderResponse>(
                        "plannedQuantity", "Planned quantity exceeds the Sales Order line's unfulfilled and unplanned quantity.");
                }

                var variant = await db.ProductVariants.AsNoTracking().SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == salesLine.ProductVariantId,
                    cancellationToken);
                var bomVersion = await db.BomVersions.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == request.BomVersionId,
                    cancellationToken);
                if (bomVersion is null
                    || bomVersion.Status != BomVersionStatuses.Approved
                    || bomVersion.EffectiveFrom > request.PlannedStartDate
                    || bomVersion.EffectiveTo < request.PlannedStartDate)
                {
                    return Invalid<WorkOrderResponse>(
                        "bomVersionId", "Choose an approved BOM version effective on the planned start date.");
                }

                var bom = await db.Boms.AsNoTracking().SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == bomVersion.BomId,
                    cancellationToken);
                if (bom.ProductId != variant.ProductId)
                {
                    return Invalid<WorkOrderResponse>(
                        "bomVersionId", "The BOM version must belong to the Sales Order line's product.");
                }

                if (!await db.Warehouses.AnyAsync(value =>
                        value.CompanyId == companyId && value.Id == request.OutputWarehouseId
                        && value.IsActive, cancellationToken))
                {
                    return Invalid<WorkOrderResponse>(
                        "outputWarehouseId", "Choose an active output warehouse in this company.");
                }

                var operationIds = operationRequests.Select(value => value.ProductionOperationId)
                    .Distinct().ToArray();
                if (operationIds.Length != operationRequests.Count)
                {
                    return Invalid<WorkOrderResponse>(
                        "operations", "A production operation may appear only once.");
                }

                var operations = await db.ProductionOperations.AsNoTracking()
                    .Where(value => value.CompanyId == companyId
                        && operationIds.Contains(value.Id) && value.IsActive)
                    .ToDictionaryAsync(value => value.Id, cancellationToken);
                if (operations.Count != operationIds.Length)
                {
                    return Invalid<WorkOrderResponse>(
                        "operations", "Choose active production operations in this company.");
                }

                foreach (var input in operationRequests)
                {
                    if (input.PlannedStartDate < request.PlannedStartDate
                        || input.PlannedEndDate > request.PlannedEndDate
                        || input.PlannedStartDate == default
                        || input.PlannedEndDate < input.PlannedStartDate)
                    {
                        return Invalid<WorkOrderResponse>(
                            "operations", "Each operation's planned dates must fit within the Work Order date range.");
                    }
                }

                var explosion = await ExplodeBomAsync(
                    companyId, bomVersion, request.PlannedQuantity,
                    request.PlannedStartDate, [], order.OrderNumber, cancellationToken);
                if (explosion.Error is not null)
                {
                    return Invalid<WorkOrderResponse>("bomVersionId", explosion.Error);
                }

                var workOrder = new WorkOrder
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    WorkOrderNumber = NewDocumentNumber("WO"),
                    SalesOrderRevisionId = orderRevision.Id,
                    SalesOrderLineId = salesLine.Id,
                    BomVersionId = bomVersion.Id,
                    BomVersionNumber = bomVersion.VersionNumber,
                    OutputWarehouseId = request.OutputWarehouseId,
                    PlannedQuantity = request.PlannedQuantity,
                    PlannedStartDate = request.PlannedStartDate,
                    PlannedEndDate = request.PlannedEndDate,
                    Status = WorkOrderStatuses.Draft,
                    CreatedBySubject = actor.Subject,
                };
                db.WorkOrders.Add(workOrder);
                foreach (var material in explosion.Lines)
                {
                    db.WorkOrderMaterialRequirements.Add(new WorkOrderMaterialRequirement
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        WorkOrderId = workOrder.Id,
                        BomVersionId = material.BomVersionId,
                        BomComponentId = material.BomComponentId,
                        SourcePath = material.SourcePath,
                        ItemId = material.ItemId,
                        UnitOfMeasureId = material.UnitOfMeasureId,
                        QuantityPerFinishedUnit = material.QuantityPerFinishedUnit,
                        WastePercent = material.WastePercent,
                        RequiredQuantity = material.Quantity,
                    });
                }

                foreach (var input in operationRequests.OrderBy(value => value.Sequence))
                {
                    var operation = operations[input.ProductionOperationId];
                    db.WorkOrderOperations.Add(new WorkOrderOperation
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        WorkOrderId = workOrder.Id,
                        ProductionOperationId = operation.Id,
                        Sequence = input.Sequence,
                        OperationCodeSnapshot = operation.Code,
                        OperationNameSnapshot = operation.Name,
                        PlannedStartDate = input.PlannedStartDate,
                        PlannedEndDate = input.PlannedEndDate,
                        PlannedTargetQuantity = workOrder.PlannedQuantity,
                    });
                }

                AddWorkOrderEvent(companyId, actor, workOrder.Id, "created",
                    new
                    {
                        workOrder.WorkOrderNumber,
                        salesOrderId = order.Id,
                        salesOrderLineId = salesLine.Id,
                        bomVersionId = bomVersion.Id,
                        bomVersion.VersionNumber,
                        materialCount = explosion.Lines.Count,
                        operationCount = operations.Count,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken),
                    StatusCodes.Status201Created,
                    ResourceId: workOrder.Id);
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> SubmitWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangeWorkOrderStatusAsync(companyId, actor, id, idempotencyKey,
            "work-order.submit", WorkOrderStatuses.Draft, WorkOrderStatuses.Submitted,
            cancellationToken, workOrder =>
            {
                workOrder.SubmittedBySubject = actor.Subject;
                workOrder.SubmittedAt = DateTimeOffset.UtcNow;
            });

    private static Dictionary<string, string[]> ValidateWorkOrderReason(
        WorkOrderReasonRequest request)
    {
        var errors = Validate(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors["reason"] = ["A non-empty reason is required."];
        }

        return errors;
    }

    public Task<MasterDataResult<WorkOrderResponse>> ApproveWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.approve", new { id }, async () =>
            {
                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status != WorkOrderStatuses.Submitted)
                {
                    return Conflict<WorkOrderResponse>("Only a submitted Work Order can be approved.");
                }

                if (string.Equals(workOrder.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<WorkOrderResponse>("The submitter cannot approve the same Work Order.");
                }

                workOrder.Status = WorkOrderStatuses.Approved;
                workOrder.ApprovedBySubject = actor.Subject;
                workOrder.ApprovedAt = DateTimeOffset.UtcNow;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, workOrder.Id, "approved",
                    new { workOrder.ApprovedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> RejectWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.reject", new { id, request }, async () =>
            {
                var errors = ValidateWorkOrderReason(request);
                if (errors.Count > 0)
                {
                    return Invalid<WorkOrderResponse>(errors);
                }

                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status != WorkOrderStatuses.Submitted)
                {
                    return Conflict<WorkOrderResponse>("Only a submitted Work Order can be rejected.");
                }

                if (string.Equals(workOrder.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<WorkOrderResponse>("The submitter cannot reject the same Work Order.");
                }

                workOrder.Status = WorkOrderStatuses.Rejected;
                workOrder.RejectionReason = request.Reason.Trim();
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, workOrder.Id, "rejected",
                    new { }, workOrder.RejectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> ReleaseWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.release", new { id }, async () =>
            {
                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status == WorkOrderStatuses.OnHold)
                {
                    workOrder.Status = WorkOrderStatuses.Released;
                    workOrder.ConcurrencyVersion++;
                    AddWorkOrderEvent(companyId, actor, workOrder.Id, "resumed", new { });
                    await db.SaveChangesAsync(cancellationToken);
                    return new MasterDataResult<WorkOrderResponse>(
                        await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
                }

                if (workOrder.Status != WorkOrderStatuses.Approved)
                {
                    return Conflict<WorkOrderResponse>("Only an approved Work Order can be released.");
                }

                var allocation = await AllocateWorkOrderMaterialsAsync(
                    companyId, workOrder, cancellationToken);
                if (allocation.Error is not null)
                {
                    return Conflict<WorkOrderResponse>(allocation.Error);
                }

                foreach (var reserved in allocation.Reservations)
                {
                    reserved.Balance.ReservedQuantity += reserved.Quantity;
                    reserved.Balance.ConcurrencyVersion++;
                    db.InventoryReservations.Add(new InventoryReservation
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        WorkOrderMaterialRequirementId = reserved.MaterialRequirementId,
                        WarehouseId = reserved.Balance.WarehouseId,
                        Quantity = reserved.Quantity,
                    });
                }

                await EnsureJobCardsAsync(companyId, actor, workOrder, cancellationToken);
                workOrder.Status = WorkOrderStatuses.Released;
                workOrder.ReleasedAt = DateTimeOffset.UtcNow;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, workOrder.Id, "released",
                    new { reservationCount = allocation.Reservations.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> HoldWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.hold", new { id, request }, async () =>
            {
                var errors = ValidateWorkOrderReason(request);
                if (errors.Count > 0)
                {
                    return Invalid<WorkOrderResponse>(errors);
                }

                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status != WorkOrderStatuses.Released)
                {
                    return Conflict<WorkOrderResponse>("Only a released Work Order can be placed on hold.");
                }

                workOrder.Status = WorkOrderStatuses.OnHold;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, workOrder.Id, "held",
                    new { }, request.Reason.Trim());
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> CompleteWorkOrderOperationAsync(
        Guid companyId, MasterDataActor actor, Guid id, Guid operationId, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.operation.complete", new { id, operationId }, async () =>
            {
                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status != WorkOrderStatuses.Released)
                {
                    return Conflict<WorkOrderResponse>("Operations can only be completed on a released Work Order.");
                }

                var operation = await db.WorkOrderOperations.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.WorkOrderId == id
                    && value.Id == operationId, cancellationToken);
                if (operation is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (operation.Status == "completed")
                {
                    return new MasterDataResult<WorkOrderResponse>(
                        await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
                }

                return Conflict<WorkOrderResponse>(
                    "Operations complete only after their Job Card production confirmations are approved.");
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> CompleteWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.complete", new { id }, async () =>
            {
                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status != WorkOrderStatuses.Released)
                {
                    return Conflict<WorkOrderResponse>("Only a released Work Order can be completed.");
                }

                if (await db.WorkOrderOperations.AnyAsync(value =>
                        value.CompanyId == companyId && value.WorkOrderId == id
                        && value.Status != "completed", cancellationToken))
                {
                    return Conflict<WorkOrderResponse>(
                        "Complete every production operation before completing the Work Order.");
                }

                var requirements = await db.WorkOrderMaterialRequirements.AsNoTracking()
                    .Where(value => value.CompanyId == companyId && value.WorkOrderId == id)
                    .ToListAsync(cancellationToken);
                var reservations = await db.InventoryReservations
                    .Where(value => value.CompanyId == companyId
                        && value.Status == "active"
                        && requirements.Select(requirement => requirement.Id)
                            .Contains(value.WorkOrderMaterialRequirementId))
                    .ToListAsync(cancellationToken);
                var reservedByRequirement = reservations
                    .GroupBy(value => value.WorkOrderMaterialRequirementId)
                    .ToDictionary(group => group.Key, group => group.Sum(value => value.Quantity));
                foreach (var requirement in requirements)
                {
                    if (reservedByRequirement.GetValueOrDefault(requirement.Id)
                        < requirement.RequiredQuantity)
                    {
                        return Conflict<WorkOrderResponse>(
                            "Required material reservations are incomplete; Work Order cannot complete.");
                    }
                }

                foreach (var reservation in reservations)
                {
                    var requirement = requirements.Single(value =>
                        value.Id == reservation.WorkOrderMaterialRequirementId);
                    var balance = await db.InventoryStockBalances.SingleAsync(value =>
                        value.CompanyId == companyId
                        && value.ItemId == requirement.ItemId
                        && value.WarehouseId == reservation.WarehouseId
                        && value.StockStatus == InventoryStockStatuses.Available,
                        cancellationToken);
                    if (balance.OnHandQuantity < reservation.Quantity
                        || balance.ReservedQuantity < reservation.Quantity)
                    {
                        return Conflict<WorkOrderResponse>(
                            "Inventory changed below the reserved quantity; reload and reconcile stock.");
                    }

                    balance.OnHandQuantity -= reservation.Quantity;
                    balance.ReservedQuantity -= reservation.Quantity;
                    balance.ConcurrencyVersion++;
                    reservation.Status = "consumed";
                    reservation.ReleasedAt = DateTimeOffset.UtcNow;
                    db.InventoryLedgerEntries.Add(new InventoryLedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        ItemId = requirement.ItemId,
                        WarehouseId = reservation.WarehouseId,
                        StockStatus = InventoryStockStatuses.Available,
                        TransactionType = "issue",
                        Quantity = -reservation.Quantity,
                        ReferenceType = "work-order-consumption",
                        ReferenceId = workOrder.Id,
                        ReferenceLineId = reservation.Id,
                        ActorSubject = actor.Subject,
                    });
                }

                var finalOperation = await db.WorkOrderOperations.AsNoTracking()
                    .Where(value => value.CompanyId == companyId && value.WorkOrderId == id)
                    .OrderByDescending(value => value.Sequence)
                    .FirstAsync(cancellationToken);
                var finalJobCard = await db.JobCards.SingleAsync(value =>
                    value.CompanyId == companyId
                    && value.WorkOrderOperationId == finalOperation.Id, cancellationToken);
                var finalGoodQuantity = await db.ProductionConfirmations.AsNoTracking()
                    .Where(value => value.CompanyId == companyId
                        && value.JobCardId == finalJobCard.Id
                        && value.Status == ProductionConfirmationStatuses.Approved)
                    .SumAsync(value => (decimal?)value.GoodQuantity, cancellationToken) ?? 0m;
                workOrder.Status = WorkOrderStatuses.Completed;
                workOrder.CompletedAt = DateTimeOffset.UtcNow;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, workOrder.Id, "completed",
                    new { workOrder.PlannedQuantity, acceptedGoodQuantity = finalGoodQuantity });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<WorkOrderResponse>> CancelWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "work-order.cancel", new { id, request }, async () =>
            {
                var errors = ValidateWorkOrderReason(request);
                if (errors.Count > 0)
                {
                    return Invalid<WorkOrderResponse>(errors);
                }

                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status is WorkOrderStatuses.Completed
                    or WorkOrderStatuses.Cancelled
                    or WorkOrderStatuses.Rejected)
                {
                    return Conflict<WorkOrderResponse>(
                        "A completed or cancelled Work Order cannot be cancelled.");
                }

                await ReleaseReservationsAsync(companyId, id, cancellationToken);
                var jobCards = await db.JobCards.Where(value =>
                    value.CompanyId == companyId && value.WorkOrderId == id
                    && value.Status != JobCardStatuses.Completed
                    && value.Status != JobCardStatuses.Cancelled).ToListAsync(cancellationToken);
                foreach (var jobCard in jobCards)
                {
                    jobCard.Status = JobCardStatuses.Cancelled;
                    jobCard.ConcurrencyVersion++;
                }

                var pendingConfirmations = await db.ProductionConfirmations.Where(value =>
                    value.CompanyId == companyId && value.WorkOrderId == id
                    && value.Status == ProductionConfirmationStatuses.PendingApproval)
                    .ToListAsync(cancellationToken);
                foreach (var confirmation in pendingConfirmations)
                {
                    confirmation.Status = ProductionConfirmationStatuses.Rejected;
                    confirmation.RejectionReason = request.Reason.Trim();
                }

                workOrder.Status = WorkOrderStatuses.Cancelled;
                workOrder.CancelledAt = DateTimeOffset.UtcNow;
                workOrder.ConcurrencyVersion++;
                AddWorkOrderEvent(companyId, actor, id, "cancelled",
                    new { }, request.Reason.Trim());
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    public async Task<MasterDataResult<WorkOrderMaterialAvailabilityResponse>> GetWorkOrderMaterialsAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var workOrder = await db.WorkOrders.AsNoTracking().SingleOrDefaultAsync(value =>
            value.CompanyId == companyId && value.Id == id, cancellationToken);
        if (workOrder is null)
        {
            return NotFound<WorkOrderMaterialAvailabilityResponse>();
        }

        var requirements = await db.WorkOrderMaterialRequirements.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == id)
            .GroupBy(value => value.ItemId)
            .Select(group => new
            {
                ItemId = group.Key,
                RequiredQuantity = group.Sum(value => value.RequiredQuantity),
            })
            .ToListAsync(cancellationToken);
        var result = new List<WorkOrderMaterialAvailabilityLine>(requirements.Count);
        foreach (var requirement in requirements)
        {
            var item = await db.Items.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == requirement.ItemId,
                cancellationToken);
            var stockUnit = await db.UnitsOfMeasure.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == item.StockUnitId,
                cancellationToken);
            var balances = await (
                from balance in db.InventoryStockBalances.AsNoTracking()
                join warehouse in db.Warehouses.AsNoTracking()
                    on new { balance.CompanyId, Id = balance.WarehouseId }
                    equals new { warehouse.CompanyId, warehouse.Id }
                where balance.CompanyId == companyId && balance.ItemId == item.Id
                    && balance.StockStatus == InventoryStockStatuses.Available
                    && warehouse.IsActive
                select balance).ToListAsync(cancellationToken);
            var activeReservations = await (
                from reservation in db.InventoryReservations.AsNoTracking()
                where reservation.CompanyId == companyId && reservation.Status == "active"
                join material in db.WorkOrderMaterialRequirements.AsNoTracking()
                    on new { reservation.CompanyId, Id = reservation.WorkOrderMaterialRequirementId }
                    equals new { material.CompanyId, material.Id }
                where material.ItemId == item.Id
                select new { material.WorkOrderId, reservation.Quantity })
                .ToListAsync(cancellationToken);
            var ownReserved = activeReservations.Where(value => value.WorkOrderId == id)
                .Sum(value => value.Quantity);
            var reservedForOthers = activeReservations.Where(value => value.WorkOrderId != id)
                .Sum(value => value.Quantity);
            var onHand = balances.Sum(value => value.OnHandQuantity);
            var physicalAvailable = Math.Max(0m, onHand - reservedForOthers);
            var incoming = await GetOpenPurchaseReceiptsAsync(
                companyId, item.Id, workOrder.PlannedStartDate, cancellationToken);
            var availableByStart = physicalAvailable + incoming;
            var shortage = Math.Max(0m, requirement.RequiredQuantity - availableByStart);
            result.Add(new WorkOrderMaterialAvailabilityLine(
                item.Id, item.Code, item.Name, stockUnit.Code,
                requirement.RequiredQuantity, onHand, reservedForOthers, incoming,
                availableByStart, shortage));
        }

        return new MasterDataResult<WorkOrderMaterialAvailabilityResponse>(
            new WorkOrderMaterialAvailabilityResponse(
                workOrder.Id, workOrder.WorkOrderNumber, workOrder.PlannedStartDate,
                result.All(value => value.Shortage == 0m), result));
    }

    public async Task<IReadOnlyList<TransactionHistoryResponse>> GetWorkOrderHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        if (!await db.WorkOrders.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken))
        {
            return [];
        }

        var events = await db.WorkOrderTransactionEvents.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == id)
            .ToListAsync(cancellationToken);
        return events.OrderBy(value => value.OccurredAt)
            .ThenBy(value => value.Id)
            .Select(value => new TransactionHistoryResponse(
                "work-order", value.WorkOrderId, null, value.Action,
                value.ActorSubject, value.Reason, value.DetailsJson, value.OccurredAt))
            .ToArray();
    }

    private Task<MasterDataResult<WorkOrderResponse>> ChangeWorkOrderStatusAsync(
        Guid companyId,
        MasterDataActor actor,
        Guid id,
        string idempotencyKey,
        string operation,
        string expectedStatus,
        string nextStatus,
        CancellationToken cancellationToken,
        Action<WorkOrder>? update = null) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, operation, new { id },
            async () =>
            {
                var workOrder = await db.WorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (workOrder is null)
                {
                    return NotFound<WorkOrderResponse>();
                }

                if (workOrder.Status != expectedStatus)
                {
                    return Conflict<WorkOrderResponse>(
                        $"A Work Order in '{workOrder.Status}' status cannot perform this action.");
                }

                workOrder.Status = nextStatus;
                workOrder.ConcurrencyVersion++;
                update?.Invoke(workOrder);
                AddWorkOrderEvent(companyId, actor, workOrder.Id,
                    operation[(operation.LastIndexOf('.') + 1)..], new { status = nextStatus });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<WorkOrderResponse>(
                    await MapWorkOrderAsync(companyId, workOrder, cancellationToken));
            }, cancellationToken);

    private async Task<MaterialAllocationResult> AllocateWorkOrderMaterialsAsync(
        Guid companyId, WorkOrder workOrder, CancellationToken cancellationToken)
    {
        var requirements = await db.WorkOrderMaterialRequirements.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrder.Id)
            .ToListAsync(cancellationToken);
        var balances = await (
            from balance in db.InventoryStockBalances
            join warehouse in db.Warehouses
                on new { balance.CompanyId, Id = balance.WarehouseId }
                equals new { warehouse.CompanyId, warehouse.Id }
            where balance.CompanyId == companyId
                && balance.StockStatus == InventoryStockStatuses.Available
                && warehouse.IsActive
            select balance).ToListAsync(cancellationToken);
        var availableByBalance = balances.ToDictionary(
            value => value.Id, value => value.OnHandQuantity - value.ReservedQuantity);
        var plans = new List<ReservationAllocation>();
        foreach (var itemGroup in requirements.GroupBy(value => value.ItemId))
        {
            var candidates = balances.Where(value => value.ItemId == itemGroup.Key)
                .OrderBy(value => value.WarehouseId)
                .ToArray();
            foreach (var requirement in itemGroup)
            {
                var remaining = requirement.RequiredQuantity;
                foreach (var balance in candidates)
                {
                    var available = Math.Max(0m, availableByBalance[balance.Id]);
                    var allocated = Math.Min(remaining, available);
                    if (allocated <= 0m)
                    {
                        continue;
                    }

                    plans.Add(new ReservationAllocation(requirement.Id, balance, allocated));
                    availableByBalance[balance.Id] -= allocated;
                    remaining -= allocated;
                    if (remaining == 0m)
                    {
                        break;
                    }
                }

                if (remaining > 0m)
                {
                    var item = await db.Items.AsNoTracking().SingleAsync(value =>
                        value.CompanyId == companyId && value.Id == requirement.ItemId,
                        cancellationToken);
                    return new MaterialAllocationResult([],
                        $"Insufficient available stock for '{item.Code}'; shortage is {remaining:0.######} in stock units.");
                }
            }
        }

        var trackedIds = plans.Select(value => value.Balance.Id).Distinct().ToArray();
        if (trackedIds.Length > 0)
        {
            var trackedBalances = await db.InventoryStockBalances
                .Where(value => value.CompanyId == companyId && trackedIds.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, cancellationToken);
            plans = plans.Select(value => value with
            {
                Balance = trackedBalances[value.Balance.Id],
            }).ToList();
        }

        return new MaterialAllocationResult(plans, null);
    }

    private async Task ReleaseReservationsAsync(
        Guid companyId, Guid workOrderId, CancellationToken cancellationToken)
    {
        var requirements = await db.WorkOrderMaterialRequirements.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrderId)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);
        var reservations = await db.InventoryReservations
            .Where(value => value.CompanyId == companyId && value.Status == "active"
                && requirements.Contains(value.WorkOrderMaterialRequirementId))
            .ToListAsync(cancellationToken);
        foreach (var reservation in reservations)
        {
            var requirement = await db.WorkOrderMaterialRequirements.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == reservation.WorkOrderMaterialRequirementId,
                cancellationToken);
            var balance = await db.InventoryStockBalances.SingleAsync(value =>
                value.CompanyId == companyId && value.ItemId == requirement.ItemId
                && value.WarehouseId == reservation.WarehouseId
                && value.StockStatus == InventoryStockStatuses.Available, cancellationToken);
            balance.ReservedQuantity -= reservation.Quantity;
            balance.ConcurrencyVersion++;
            reservation.Status = "released";
            reservation.ReleasedAt = DateTimeOffset.UtcNow;
        }
    }

    private async Task<WorkOrderResponse> MapWorkOrderAsync(
        Guid companyId, WorkOrder workOrder, CancellationToken cancellationToken)
    {
        var orderRevision = await db.SalesOrderRevisions.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == workOrder.SalesOrderRevisionId,
            cancellationToken);
        var order = await db.SalesOrders.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == orderRevision.SalesOrderId,
            cancellationToken);
        var line = await db.SalesOrderLines.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == workOrder.SalesOrderLineId,
            cancellationToken);
        var product = await db.Products.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == line.ProductId, cancellationToken);
        var variant = await db.ProductVariants.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == line.ProductVariantId,
            cancellationToken);
        var operations = await db.WorkOrderOperations.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.WorkOrderId == workOrder.Id)
            .OrderBy(value => value.Sequence)
            .Select(value => new WorkOrderOperationResponse(
                value.Id, value.Sequence, value.ProductionOperationId,
                value.OperationCodeSnapshot, value.OperationNameSnapshot,
                value.PlannedStartDate, value.PlannedEndDate,
                value.PlannedTargetQuantity, value.ProductionLine, value.MachineId,
                value.AssignedTeam, value.Status,
                value.CompletedAt, value.CompletedBySubject))
            .ToArrayAsync(cancellationToken);
        var materials = await (
            from material in db.WorkOrderMaterialRequirements.AsNoTracking()
            where material.CompanyId == companyId && material.WorkOrderId == workOrder.Id
            join item in db.Items.AsNoTracking()
                on new { material.CompanyId, Id = material.ItemId }
                equals new { item.CompanyId, item.Id }
            join unit in db.UnitsOfMeasure.AsNoTracking()
                on new { material.CompanyId, Id = material.UnitOfMeasureId }
                equals new { unit.CompanyId, unit.Id }
            orderby item.Code, material.SourcePath
            select new WorkOrderMaterialResponse(
                material.Id, item.Id, item.Code, item.Name, unit.Id, unit.Code,
                material.SourcePath, material.QuantityPerFinishedUnit,
                material.WastePercent, material.RequiredQuantity))
            .ToArrayAsync(cancellationToken);
        return new WorkOrderResponse(
            workOrder.Id, workOrder.WorkOrderNumber, order.Id, order.OrderNumber,
            line.Id, product.Id, variant.Id, product.Code, variant.Code,
            workOrder.BomVersionId, workOrder.BomVersionNumber,
            workOrder.PlannedQuantity, workOrder.PlannedStartDate,
            workOrder.PlannedEndDate, workOrder.OutputWarehouseId,
            workOrder.Priority,
            workOrder.Status, workOrder.CreatedBySubject,
            workOrder.ApprovedBySubject, workOrder.CreatedAt, workOrder.ApprovedAt,
            workOrder.ReleasedAt, workOrder.CompletedAt, workOrder.RejectionReason,
            operations, materials);
    }

    private void AddWorkOrderEvent(
        Guid companyId, MasterDataActor actor, Guid workOrderId,
        string action, object details, string? reason = null)
    {
        db.WorkOrderTransactionEvents.Add(new WorkOrderTransactionEvent
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            WorkOrderId = workOrderId,
            Action = action,
            ActorSubject = actor.Subject,
            Reason = reason,
            DetailsJson = JsonSerializer.Serialize(details),
        });
        if (action == "released")
        {
            db.InAppNotifications.Add(new InAppNotification
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                Category = "production",
                Title = "Work Order released",
                Message = $"Work Order {workOrderId} was released to production.",
                SourceType = "work-order-release",
                SourceId = workOrderId,
            });
        }
    }

    private sealed record ReservationAllocation(
        Guid MaterialRequirementId,
        InventoryStockBalance Balance,
        decimal Quantity);

    private sealed record MaterialAllocationResult(
        IReadOnlyList<ReservationAllocation> Reservations,
        string? Error);
}
