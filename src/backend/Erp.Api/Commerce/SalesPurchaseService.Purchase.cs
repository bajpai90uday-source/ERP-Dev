using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService
{
    public async Task<PageResponse<PurchaseRequisitionResponse>> ListPurchaseRequisitionsAsync(
        Guid companyId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PurchaseRequisitions.AsNoTracking()
            .Where(value => value.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(value => value.RequisitionNumber.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var records = await query.OrderByDescending(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var responses = new List<PurchaseRequisitionResponse>(records.Count);
        foreach (var record in records)
        {
            responses.Add(await MapPurchaseRequisitionAsync(companyId, record, cancellationToken));
        }

        return new PageResponse<PurchaseRequisitionResponse>(responses, page, pageSize, total);
    }

    public async Task<MasterDataResult<PurchaseRequisitionResponse>> GetPurchaseRequisitionAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var record = await db.PurchaseRequisitions.AsNoTracking().SingleOrDefaultAsync(
            value => value.CompanyId == companyId && value.Id == id, cancellationToken);
        return record is null
            ? NotFound<PurchaseRequisitionResponse>()
            : new MasterDataResult<PurchaseRequisitionResponse>(
                await MapPurchaseRequisitionAsync(companyId, record, cancellationToken));
    }

    public Task<MasterDataResult<PurchaseRequisitionResponse>> CreatePurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        PurchaseRequisitionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.requisition.create", request, async () =>
            {
                var errors = await ValidateRequisitionAsync(companyId, request, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<PurchaseRequisitionResponse>(errors);
                }

                var requisition = new PurchaseRequisition
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    RequisitionNumber = NewDocumentNumber("PR"),
                    Status = OrderStatuses.Draft,
                    CreatedBySubject = actor.Subject,
                };
                db.PurchaseRequisitions.Add(requisition);
                AddRequisitionLines(companyId, requisition.Id, request.Lines);
                AddEvent(companyId, actor, "purchase-requisition", requisition.Id, 1,
                    "created", new { lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseRequisitionResponse>(
                    await MapPurchaseRequisitionAsync(companyId, requisition, cancellationToken),
                    StatusCodes.Status201Created,
                    ResourceId: requisition.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseRequisitionResponse>> UpdatePurchaseRequisitionDraftAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        PurchaseRequisitionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.requisition.draft.update", new { id, request }, async () =>
            {
                var errors = await ValidateRequisitionAsync(companyId, request, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<PurchaseRequisitionResponse>(errors);
                }

                var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (requisition is null)
                {
                    return NotFound<PurchaseRequisitionResponse>();
                }

                if (requisition.Status is not (OrderStatuses.Draft or OrderStatuses.Rejected))
                {
                    return Conflict<PurchaseRequisitionResponse>(
                        "Only a draft or rejected requisition can be edited.");
                }

                db.PurchaseRequisitionLines.RemoveRange(await db.PurchaseRequisitionLines
                    .Where(line => line.CompanyId == companyId
                        && line.PurchaseRequisitionId == requisition.Id)
                    .ToListAsync(cancellationToken));
                requisition.Status = OrderStatuses.Draft;
                requisition.RejectionReason = null;
                requisition.ApprovedAt = null;
                requisition.ApprovedBySubject = null;
                requisition.SubmittedAt = null;
                requisition.SubmittedBySubject = null;
                requisition.ConcurrencyVersion++;
                AddRequisitionLines(companyId, requisition.Id, request.Lines);
                AddEvent(companyId, actor, "purchase-requisition", requisition.Id, 1,
                    "draft_updated", new { lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseRequisitionResponse>(
                    await MapPurchaseRequisitionAsync(companyId, requisition, cancellationToken),
                    ResourceId: requisition.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseRequisitionResponse>> SubmitPurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangeRequisitionStatusAsync(companyId, actor, id, idempotencyKey,
            "purchase.requisition.submit", OrderStatuses.Draft, OrderStatuses.Submitted,
            cancellationToken, requisition =>
            {
                requisition.SubmittedAt = DateTimeOffset.UtcNow;
                requisition.SubmittedBySubject = actor.Subject;
            });

    public Task<MasterDataResult<PurchaseRequisitionResponse>> ApprovePurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.requisition.approve", new { id }, async () =>
            {
                var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (requisition is null)
                {
                    return NotFound<PurchaseRequisitionResponse>();
                }

                if (requisition.Status != OrderStatuses.Submitted)
                {
                    return Conflict<PurchaseRequisitionResponse>(
                        "Only a submitted requisition can be approved.");
                }

                if (string.Equals(requisition.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<PurchaseRequisitionResponse>(
                        "The person who submitted a requisition cannot approve it.");
                }

                requisition.Status = OrderStatuses.Approved;
                requisition.ApprovedAt = DateTimeOffset.UtcNow;
                requisition.ApprovedBySubject = actor.Subject;
                requisition.ConcurrencyVersion++;
                AddEvent(companyId, actor, "purchase-requisition", requisition.Id, 1,
                    "approved", new { requisition.ApprovedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseRequisitionResponse>(
                    await MapPurchaseRequisitionAsync(companyId, requisition, cancellationToken),
                    ResourceId: requisition.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseRequisitionResponse>> RejectPurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        RejectionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.requisition.reject", new { id, request }, async () =>
            {
                var validation = Validate(request);
                if (validation.Count > 0)
                {
                    return Invalid<PurchaseRequisitionResponse>(validation);
                }

                var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (requisition is null)
                {
                    return NotFound<PurchaseRequisitionResponse>();
                }

                if (requisition.Status != OrderStatuses.Submitted)
                {
                    return Conflict<PurchaseRequisitionResponse>(
                        "Only a submitted requisition can be rejected.");
                }

                if (string.Equals(requisition.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<PurchaseRequisitionResponse>(
                        "The person who submitted a requisition cannot reject it.");
                }

                requisition.Status = OrderStatuses.Rejected;
                requisition.RejectionReason = request.Reason.Trim();
                requisition.ConcurrencyVersion++;
                AddEvent(companyId, actor, "purchase-requisition", requisition.Id, 1,
                    "rejected", new { }, requisition.RejectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseRequisitionResponse>(
                    await MapPurchaseRequisitionAsync(companyId, requisition, cancellationToken),
                    ResourceId: requisition.Id);
            }, cancellationToken);

    public async Task<IReadOnlyList<TransactionHistoryResponse>> PurchaseRequisitionHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        if (!await db.PurchaseRequisitions.AnyAsync(
                value => value.CompanyId == companyId && value.Id == id, cancellationToken))
        {
            return [];
        }

        return await GetHistoryAsync(companyId, "purchase-requisition", id, cancellationToken);
    }

    public async Task<PageResponse<PurchaseOrderResponse>> ListPurchaseOrdersAsync(
        Guid companyId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.PurchaseOrders.AsNoTracking()
            .Where(value => value.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(value => value.OrderNumber.ToLower().Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var orders = await query.OrderByDescending(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var responses = new List<PurchaseOrderResponse>(orders.Count);
        foreach (var order in orders)
        {
            var revision = await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
            if (revision is not null)
            {
                responses.Add(await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken));
            }
        }

        return new PageResponse<PurchaseOrderResponse>(responses, page, pageSize, total);
    }

    public async Task<MasterDataResult<PurchaseOrderResponse>> GetPurchaseOrderAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(
            value => value.CompanyId == companyId && value.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound<PurchaseOrderResponse>();
        }

        var revision = await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
        return revision is null
            ? NotFound<PurchaseOrderResponse>()
            : new MasterDataResult<PurchaseOrderResponse>(
                await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken));
    }

    public Task<MasterDataResult<PurchaseOrderResponse>> CreatePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        PurchaseOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.order.create", request, async () =>
            {
                var errors = await ValidatePurchaseOrderAsync(
                    companyId, request, null, allowNewLines: true, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<PurchaseOrderResponse>(errors);
                }

                var order = new PurchaseOrder
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    OrderNumber = NewDocumentNumber("PO"),
                    CurrentRevisionNumber = 1,
                    CreatedBySubject = actor.Subject,
                };
                var revision = NewPurchaseOrderRevision(companyId, order.Id, 1, actor, request);
                db.PurchaseOrders.Add(order);
                db.PurchaseOrderRevisions.Add(revision);
                await ReplacePurchaseOrderLinesAsync(
                    companyId, order.Id, revision.Id, request.Lines, null, cancellationToken);
                AddEvent(companyId, actor, "purchase-order", order.Id, 1,
                    "created", new { supplierId = request.SupplierId, lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseOrderResponse>(
                    await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken),
                    StatusCodes.Status201Created,
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseOrderResponse>> UpdatePurchaseOrderDraftAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        PurchaseOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.order.draft.update", new { id, request }, async () =>
            {
                var errors = await ValidatePurchaseOrderAsync(
                    companyId, request, id, allowNewLines: true, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<PurchaseOrderResponse>(errors);
                }

                var order = await db.PurchaseOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<PurchaseOrderResponse>();
                }

                if (revision.Status is not (OrderStatuses.Draft or OrderStatuses.Rejected))
                {
                    return Conflict<PurchaseOrderResponse>(
                        "Only a draft or rejected purchase order can be edited.");
                }

                revision.SupplierId = request.SupplierId;
                revision.PurchaseRequisitionId = request.PurchaseRequisitionId;
                revision.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
                revision.Status = OrderStatuses.Draft;
                revision.ConcurrencyVersion++;
                revision.RejectionReason = null;
                revision.SubmittedAt = null;
                revision.SubmittedBySubject = null;
                revision.ApprovedAt = null;
                revision.ApprovedBySubject = null;
                await ReplacePurchaseOrderLinesAsync(
                    companyId, order.Id, revision.Id, request.Lines, null, cancellationToken);
                AddEvent(companyId, actor, "purchase-order", order.Id, revision.RevisionNumber,
                    "draft_updated", new { lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseOrderResponse>(
                    await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseOrderResponse>> SubmitPurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangePurchaseOrderStatusAsync(companyId, actor, id, idempotencyKey,
            "purchase.order.submit", OrderStatuses.Draft, OrderStatuses.Submitted,
            cancellationToken, revision =>
            {
                revision.SubmittedAt = DateTimeOffset.UtcNow;
                revision.SubmittedBySubject = actor.Subject;
            });

    public Task<MasterDataResult<PurchaseOrderResponse>> ApprovePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.order.approve", new { id }, async () =>
            {
                var order = await db.PurchaseOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<PurchaseOrderResponse>();
                }

                if (revision.Status != OrderStatuses.Submitted)
                {
                    return Conflict<PurchaseOrderResponse>(
                        "Only a submitted purchase order can be approved.");
                }

                if (string.Equals(revision.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<PurchaseOrderResponse>(
                        "The person who submitted a purchase order cannot approve it.");
                }

                revision.Status = OrderStatuses.Approved;
                revision.ApprovedAt = DateTimeOffset.UtcNow;
                revision.ApprovedBySubject = actor.Subject;
                revision.ConcurrencyVersion++;
                AddEvent(companyId, actor, "purchase-order", order.Id, revision.RevisionNumber,
                    "approved", new { revision.ApprovedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseOrderResponse>(
                    await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseOrderResponse>> RejectPurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        RejectionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.order.reject", new { id, request }, async () =>
            {
                var validation = Validate(request);
                if (validation.Count > 0)
                {
                    return Invalid<PurchaseOrderResponse>(validation);
                }

                var order = await db.PurchaseOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<PurchaseOrderResponse>();
                }

                if (revision.Status != OrderStatuses.Submitted)
                {
                    return Conflict<PurchaseOrderResponse>(
                        "Only a submitted purchase order can be rejected.");
                }

                if (string.Equals(revision.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<PurchaseOrderResponse>(
                        "The person who submitted a purchase order cannot reject it.");
                }

                revision.Status = OrderStatuses.Rejected;
                revision.RejectionReason = request.Reason.Trim();
                revision.ConcurrencyVersion++;
                AddEvent(companyId, actor, "purchase-order", order.Id, revision.RevisionNumber,
                    "rejected", new { }, revision.RejectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseOrderResponse>(
                    await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<PurchaseOrderResponse>> IssuePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangePurchaseOrderStatusAsync(companyId, actor, id, idempotencyKey,
            "purchase.order.issue", OrderStatuses.Approved, OrderStatuses.Issued,
            cancellationToken, revision => revision.IssuedAt = DateTimeOffset.UtcNow);

    public Task<MasterDataResult<PurchaseOrderResponse>> AmendPurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        PurchaseOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.order.amend", new { id, request }, async () =>
            {
                var errors = await ValidatePurchaseOrderAsync(
                    companyId, request, id, allowNewLines: true, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<PurchaseOrderResponse>(errors);
                }

                var order = await db.PurchaseOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var prior = order is null
                    ? null
                    : await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
                if (order is null || prior is null)
                {
                    return NotFound<PurchaseOrderResponse>();
                }

                if (prior.Status is not (OrderStatuses.Issued
                    or OrderStatuses.PartiallyReceived or OrderStatuses.Received))
                {
                    return Conflict<PurchaseOrderResponse>(
                        "Only an issued purchase order can be amended.");
                }

                if (request.Lines.Select(line => line.LineId)
                        .Where(lineId => lineId.HasValue)
                        .Select(lineId => lineId!.Value)
                        .Distinct()
                        .Count()
                    != request.Lines.Count(line => line.LineId.HasValue))
                {
                    return Invalid<PurchaseOrderResponse>(
                        "lines", "A purchase order line cannot appear more than once in an amendment.");
                }

                var priorLines = await db.PurchaseOrderLineRevisions.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && line.PurchaseOrderRevisionId == prior.Id)
                    .ToListAsync(cancellationToken);
                var priorLineIds = priorLines.Select(line => line.PurchaseOrderLineId).ToArray();
                var receivedRows = await db.GoodsReceiptLines.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && priorLineIds.Contains(line.PurchaseOrderLineId))
                    .GroupBy(line => line.PurchaseOrderLineId)
                    .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
                    .ToDictionaryAsync(line => line.LineId, line => line.Quantity, cancellationToken);
                var existingLines = await db.PurchaseOrderLines.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && line.PurchaseOrderId == order.Id
                        && priorLineIds.Contains(line.Id))
                    .ToDictionaryAsync(line => line.Id, cancellationToken);
                foreach (var priorLine in priorLines)
                {
                    var received = receivedRows.GetValueOrDefault(priorLine.PurchaseOrderLineId);
                    var requestLine = request.Lines.SingleOrDefault(line =>
                        line.LineId == priorLine.PurchaseOrderLineId);
                    if (received > 0 && requestLine is null)
                    {
                        return Conflict<PurchaseOrderResponse>(
                            "An amendment cannot remove a line with received quantity.");
                    }

                    if (requestLine is not null && received > requestLine.Quantity)
                    {
                        return Conflict<PurchaseOrderResponse>(
                            "An amended quantity cannot be lower than the quantity already received.");
                    }
                }

                foreach (var line in request.Lines.Where(line => line.LineId.HasValue))
                {
                    if (!existingLines.ContainsKey(line.LineId!.Value)
                        || !priorLines.Any(priorLine =>
                            priorLine.PurchaseOrderLineId == line.LineId.Value))
                    {
                        return Invalid<PurchaseOrderResponse>(
                            "lines", "Amended line identifiers must belong to the current purchase-order revision.");
                    }
                }

                if (request.Lines.Any(line =>
                    line.LineId is { } lineId
                    && existingLines[lineId].PurchaseRequisitionLineId != line.PurchaseRequisitionLineId))
                {
                    return Invalid<PurchaseOrderResponse>(
                        "lines", "An amended purchase-order line cannot change its requisition source.");
                }

                var revision = NewPurchaseOrderRevision(
                    companyId, order.Id, order.CurrentRevisionNumber + 1, actor, request);
                order.CurrentRevisionNumber = revision.RevisionNumber;
                db.PurchaseOrderRevisions.Add(revision);
                await ReplacePurchaseOrderLinesAsync(
                    companyId, order.Id, revision.Id, request.Lines, prior.Id, cancellationToken);
                AddEvent(companyId, actor, "purchase-order", order.Id, revision.RevisionNumber,
                    "amendment_created",
                    new { previousRevision = prior.RevisionNumber, lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseOrderResponse>(
                    await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<GoodsReceiptResponse>> ReceivePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        GoodsReceiptRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "purchase.order.receive", new { id, request }, async () =>
            {
                var errors = Validate(request);
                var receiptLines = request.Lines;
                if (receiptLines is null)
                {
                    errors["lines"] = ["Add between 1 and 500 receipt lines."];
                    return Invalid<GoodsReceiptResponse>(errors);
                }

                if (receiptLines.Count is 0 or > 500)
                {
                    errors["lines"] = ["Add between 1 and 500 receipt lines."];
                }

                if (receiptLines.Select(line => line.PurchaseOrderLineId).Distinct().Count()
                    != receiptLines.Count)
                {
                    errors["lines"] = ["Each purchase-order line can appear only once per receipt."];
                }

                if (errors.Count > 0)
                {
                    return Invalid<GoodsReceiptResponse>(errors);
                }

                var order = await db.PurchaseOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<GoodsReceiptResponse>();
                }

                if (revision.Status is not (OrderStatuses.Issued
                    or OrderStatuses.PartiallyReceived))
                {
                    return Conflict<GoodsReceiptResponse>(
                        "Goods can only be received against an issued purchase order.");
                }

                var warehouse = await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId
                    && value.Id == request.WarehouseId
                    && value.IsActive, cancellationToken);
                if (warehouse is null)
                {
                    return Invalid<GoodsReceiptResponse>(
                        "warehouseId", "Choose an active warehouse in this company.");
                }

                var requestedIds = receiptLines.Select(line => line.PurchaseOrderLineId).ToArray();
                var currentLines = await db.PurchaseOrderLineRevisions.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && line.PurchaseOrderRevisionId == revision.Id
                        && requestedIds.Contains(line.PurchaseOrderLineId))
                    .ToListAsync(cancellationToken);
                if (currentLines.Count != requestedIds.Length)
                {
                    return Invalid<GoodsReceiptResponse>(
                        "lines", "Receipt lines must belong to the current issued purchase-order revision.");
                }

                var allCurrentLines = await db.PurchaseOrderLineRevisions.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && line.PurchaseOrderRevisionId == revision.Id)
                    .ToListAsync(cancellationToken);
                var stockFactors = new Dictionary<Guid, decimal>();
                var stockQuantities = new Dictionary<Guid, decimal>();
                foreach (var line in currentLines)
                {
                    var item = await db.Items.AsNoTracking().SingleAsync(value =>
                        value.CompanyId == companyId && value.Id == line.ItemId, cancellationToken);
                    var factor = await GetStockConversionFactorAsync(
                        companyId, item, line.UnitOfMeasureId,
                        DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
                    if (factor is null)
                    {
                        return Invalid<GoodsReceiptResponse>(
                            "lines", "A valid unit conversion to the item's stock unit is required.");
                    }

                    stockFactors[line.PurchaseOrderLineId] = factor.Value;
                }
                foreach (var input in receiptLines)
                {
                    var stockQuantity = decimal.Round(
                        input.Quantity * stockFactors[input.PurchaseOrderLineId], 6);
                    if (stockQuantity <= 0m)
                    {
                        return Invalid<GoodsReceiptResponse>(
                            "lines", "Receipt quantity rounds below the item's stock-unit precision.");
                    }

                    stockQuantities[input.PurchaseOrderLineId] = stockQuantity;
                }

                var currentLineIds = allCurrentLines
                    .Select(line => line.PurchaseOrderLineId)
                    .ToArray();
                var receivedRows = await db.GoodsReceiptLines.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && currentLineIds.Contains(line.PurchaseOrderLineId))
                    .GroupBy(line => line.PurchaseOrderLineId)
                    .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
                    .ToDictionaryAsync(line => line.LineId, line => line.Quantity, cancellationToken);
                foreach (var input in receiptLines)
                {
                    var ordered = currentLines.Single(line =>
                            line.PurchaseOrderLineId == input.PurchaseOrderLineId).Quantity;
                    var alreadyReceived = receivedRows.GetValueOrDefault(input.PurchaseOrderLineId);
                    if (alreadyReceived + input.Quantity > ordered)
                    {
                        return Invalid<GoodsReceiptResponse>(
                            "lines", "Received quantity cannot exceed the currently ordered quantity.");
                    }
                }

                var receipt = new GoodsReceipt
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    ReceiptNumber = NewDocumentNumber("GR"),
                    PurchaseOrderRevisionId = revision.Id,
                    WarehouseId = warehouse.Id,
                    SupplierDeliveryReference = request.SupplierDeliveryReference.Trim(),
                    CreatedBySubject = actor.Subject,
                };
                db.GoodsReceipts.Add(receipt);
                foreach (var input in receiptLines)
                {
                    var receiptLine = new GoodsReceiptLine
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        GoodsReceiptId = receipt.Id,
                        PurchaseOrderLineId = input.PurchaseOrderLineId,
                        Quantity = input.Quantity,
                    };
                    db.GoodsReceiptLines.Add(receiptLine);
                    var itemId = currentLines.Single(line =>
                        line.PurchaseOrderLineId == input.PurchaseOrderLineId).ItemId;
                    var balance = await GetOrCreateStockBalanceAsync(
                        companyId, itemId, warehouse.Id,
                        InventoryStockStatuses.Quarantine, cancellationToken);
                    balance.OnHandQuantity += stockQuantities[input.PurchaseOrderLineId];
                    balance.ConcurrencyVersion++;
                    db.InventoryLedgerEntries.Add(new InventoryLedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        ItemId = itemId,
                        WarehouseId = warehouse.Id,
                        StockStatus = InventoryStockStatuses.Quarantine,
                        TransactionType = "receipt",
                        Quantity = stockQuantities[input.PurchaseOrderLineId],
                        ReferenceType = "goods-receipt",
                        ReferenceId = receipt.Id,
                        ReferenceLineId = receiptLine.Id,
                        ActorSubject = actor.Subject,
                    });
                }

                var newlyReceived = receiptLines.ToDictionary(
                    input => input.PurchaseOrderLineId, input => input.Quantity);
                var updatedReceived = currentLineIds.Sum(line =>
                    receivedRows.GetValueOrDefault(line) + newlyReceived.GetValueOrDefault(line));
                var orderedTotal = allCurrentLines.Sum(line => line.Quantity);
                revision.Status = updatedReceived >= orderedTotal
                    ? OrderStatuses.Received
                    : OrderStatuses.PartiallyReceived;
                revision.ConcurrencyVersion++;
                AddEvent(companyId, actor, "purchase-order", order.Id, revision.RevisionNumber,
                    "goods_received",
                    new
                    {
                        receipt.ReceiptNumber,
                        receipt.SupplierDeliveryReference,
                        lineCount = receiptLines.Count,
                    });
                await db.SaveChangesAsync(cancellationToken);
                var responseLines = receiptLines.Select(line =>
                    new GoodsReceiptLineResponse(line.PurchaseOrderLineId, line.Quantity)).ToArray();
                return new MasterDataResult<GoodsReceiptResponse>(
                    new GoodsReceiptResponse(receipt.Id, receipt.ReceiptNumber, order.Id,
                        order.OrderNumber, warehouse.Id, receipt.SupplierDeliveryReference,
                        receipt.ReceivedAt, responseLines),
                    StatusCodes.Status201Created,
                    ResourceId: receipt.Id);
            }, cancellationToken);

    public async Task<IReadOnlyList<TransactionHistoryResponse>> PurchaseOrderHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        if (!await db.PurchaseOrders.AnyAsync(
                value => value.CompanyId == companyId && value.Id == id, cancellationToken))
        {
            return [];
        }

        return await GetHistoryAsync(companyId, "purchase-order", id, cancellationToken);
    }

    public async Task<MasterDataResult<GoodsReceiptResponse>> GetGoodsReceiptAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.GoodsReceipts.AsNoTracking().SingleOrDefaultAsync(value =>
            value.CompanyId == companyId && value.Id == id, cancellationToken);
        if (receipt is null)
        {
            return NotFound<GoodsReceiptResponse>();
        }

        var orderRevision = await db.PurchaseOrderRevisions.AsNoTracking()
            .SingleAsync(value => value.CompanyId == companyId
                && value.Id == receipt.PurchaseOrderRevisionId, cancellationToken);
        var order = await db.PurchaseOrders.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == orderRevision.PurchaseOrderId,
            cancellationToken);
        var lines = await db.GoodsReceiptLines.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.GoodsReceiptId == receipt.Id)
            .Select(value => new GoodsReceiptLineResponse(
                value.PurchaseOrderLineId, value.Quantity))
            .ToArrayAsync(cancellationToken);
        return new MasterDataResult<GoodsReceiptResponse>(
            new GoodsReceiptResponse(
                receipt.Id, receipt.ReceiptNumber, order.Id, order.OrderNumber,
                receipt.WarehouseId, receipt.SupplierDeliveryReference, receipt.ReceivedAt, lines));
    }

    private Task<MasterDataResult<PurchaseRequisitionResponse>> ChangeRequisitionStatusAsync(
        Guid companyId,
        MasterDataActor actor,
        Guid id,
        string idempotencyKey,
        string operation,
        string expectedStatus,
        string nextStatus,
        CancellationToken cancellationToken,
        Action<PurchaseRequisition>? update = null) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, operation, new { id },
            async () =>
            {
                var requisition = await db.PurchaseRequisitions.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (requisition is null)
                {
                    return NotFound<PurchaseRequisitionResponse>();
                }

                if (requisition.Status != expectedStatus)
                {
                    return Conflict<PurchaseRequisitionResponse>(
                        $"A requisition in '{requisition.Status}' status cannot perform this action.");
                }

                requisition.Status = nextStatus;
                requisition.ConcurrencyVersion++;
                update?.Invoke(requisition);
                AddEvent(companyId, actor, "purchase-requisition", requisition.Id, 1,
                    operation[(operation.LastIndexOf('.') + 1)..], new { status = nextStatus });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseRequisitionResponse>(
                    await MapPurchaseRequisitionAsync(companyId, requisition, cancellationToken),
                    ResourceId: requisition.Id);
            }, cancellationToken);

    private Task<MasterDataResult<PurchaseOrderResponse>> ChangePurchaseOrderStatusAsync(
        Guid companyId,
        MasterDataActor actor,
        Guid id,
        string idempotencyKey,
        string operation,
        string expectedStatus,
        string nextStatus,
        CancellationToken cancellationToken,
        Action<PurchaseOrderRevision>? update = null) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, operation, new { id },
            async () =>
            {
                var order = await db.PurchaseOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentPurchaseOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<PurchaseOrderResponse>();
                }

                if (revision.Status != expectedStatus)
                {
                    return Conflict<PurchaseOrderResponse>(
                        $"A purchase order in '{revision.Status}' status cannot perform this action.");
                }

                revision.Status = nextStatus;
                revision.ConcurrencyVersion++;
                update?.Invoke(revision);
                AddEvent(companyId, actor, "purchase-order", order.Id, revision.RevisionNumber,
                    operation[(operation.LastIndexOf('.') + 1)..], new { status = nextStatus });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<PurchaseOrderResponse>(
                    await MapPurchaseOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    private async Task<Dictionary<string, string[]>> ValidateRequisitionAsync(
        Guid companyId, PurchaseRequisitionRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (!IsValidRequestList(request.Lines))
        {
            errors["lines"] = ["Add between 1 and 500 requisition lines."];
            return errors;
        }

        var itemIds = request.Lines.Select(line => line.ItemId).Distinct().ToArray();
        var items = await db.Items.AsNoTracking()
            .Where(item => item.CompanyId == companyId && itemIds.Contains(item.Id) && item.IsActive)
            .Select(item => new { item.Id, item.StockUnitId })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var stockUnitIds = items.Values.Select(item => item.StockUnitId).Distinct().ToArray();
        var stockUnits = await db.UnitsOfMeasure.AsNoTracking()
            .Where(unit => unit.CompanyId == companyId && stockUnitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        var unitIds = request.Lines.Select(line => line.UnitOfMeasureId).Distinct().ToArray();
        var units = await db.UnitsOfMeasure.AsNoTracking()
            .Where(unit => unit.CompanyId == companyId && unitIds.Contains(unit.Id) && unit.IsActive)
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (!items.TryGetValue(line.ItemId, out var item)
                || !units.TryGetValue(line.UnitOfMeasureId, out var unit))
            {
                errors[$"lines[{index}]"] = ["Choose an active item and unit in this company."];
            }
            else
            {
                if (!stockUnits.TryGetValue(item.StockUnitId, out var stockUnit)
                    || !string.Equals(stockUnit.Dimension, unit.Dimension, StringComparison.Ordinal))
                {
                    errors[$"lines[{index}].unitOfMeasureId"] =
                        ["The requested unit must match the item's stock-unit dimension."];
                }
            }

            if (line.RequiredByDate == default)
            {
                errors[$"lines[{index}].requiredByDate"] = ["Provide a required-by date."];
            }
        }

        return errors;
    }

    private async Task<Dictionary<string, string[]>> ValidatePurchaseOrderAsync(
        Guid companyId,
        PurchaseOrderRequest request,
        Guid? orderId,
        bool allowNewLines,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (!IsValidRequestList(request.Lines))
        {
            errors["lines"] = ["Add between 1 and 500 purchase-order lines."];
            return errors;
        }

        if (request.SupplierId == Guid.Empty
            || !await db.Suppliers.AnyAsync(supplier =>
                supplier.CompanyId == companyId && supplier.Id == request.SupplierId
                && supplier.IsActive, cancellationToken))
        {
            errors["supplierId"] = ["Choose an active supplier in this company."];
        }

        if (!IsCurrencyCode(request.CurrencyCode ?? string.Empty))
        {
            errors["currencyCode"] = ["Use a three-letter uppercase ISO 4217 currency code."];
        }

        var itemIds = request.Lines.Select(line => line.ItemId).Distinct().ToArray();
        var items = await db.Items.AsNoTracking()
            .Where(item => item.CompanyId == companyId && itemIds.Contains(item.Id) && item.IsActive)
            .Select(item => new { item.Id, item.StockUnitId })
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var stockUnitIds = items.Values.Select(item => item.StockUnitId).Distinct().ToArray();
        var stockUnits = await db.UnitsOfMeasure.AsNoTracking()
            .Where(unit => unit.CompanyId == companyId && stockUnitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        var unitIds = request.Lines.Select(line => line.UnitOfMeasureId).Distinct().ToArray();
        var units = await db.UnitsOfMeasure.AsNoTracking()
            .Where(unit => unit.CompanyId == companyId && unitIds.Contains(unit.Id) && unit.IsActive)
            .ToDictionaryAsync(unit => unit.Id, cancellationToken);
        var requisitionLines = new Dictionary<Guid, PurchaseRequisitionLine>();
        if (request.PurchaseRequisitionId is { } requisitionId)
        {
            var requisition = await db.PurchaseRequisitions.AsNoTracking()
                .SingleOrDefaultAsync(value => value.CompanyId == companyId
                    && value.Id == requisitionId, cancellationToken);
            if (requisition?.Status != OrderStatuses.Approved)
            {
                errors["purchaseRequisitionId"] =
                    ["Only an approved requisition can be linked to a purchase order."];
            }
            else
            {
                requisitionLines = await db.PurchaseRequisitionLines.AsNoTracking()
                    .Where(line => line.CompanyId == companyId
                        && line.PurchaseRequisitionId == requisition.Id)
                    .ToDictionaryAsync(line => line.Id, cancellationToken);
            }
        }

        var usedRequisitionLineIds = new HashSet<Guid>();
        var requestedByRequisitionLine = new Dictionary<Guid, decimal>();
        var suppliedLineIds = new HashSet<Guid>();
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (!items.TryGetValue(line.ItemId, out var item)
                || !units.TryGetValue(line.UnitOfMeasureId, out var unit))
            {
                errors[$"lines[{index}]"] = ["Choose an active item and unit in this company."];
            }
            else
            {
                if (!stockUnits.TryGetValue(item.StockUnitId, out var stockUnit)
                    || !string.Equals(stockUnit.Dimension, unit.Dimension, StringComparison.Ordinal))
                {
                    errors[$"lines[{index}].unitOfMeasureId"] =
                        ["The purchase unit must match the item's stock-unit dimension."];
                }
            }

            if (line.ExpectedDeliveryDate == default)
            {
                errors[$"lines[{index}].expectedDeliveryDate"] = ["Provide an expected delivery date."];
            }

            if (line.LineId is { } lineId && !suppliedLineIds.Add(lineId))
            {
                errors["lines"] = ["A purchase-order line cannot be included more than once."];
            }

            if (line.PurchaseRequisitionLineId is { } requisitionLineId)
            {
                if (request.PurchaseRequisitionId is null
                    || !requisitionLines.TryGetValue(requisitionLineId, out var sourceLine)
                    || sourceLine.ItemId != line.ItemId
                    || sourceLine.UnitOfMeasureId != line.UnitOfMeasureId)
                {
                    errors[$"lines[{index}].purchaseRequisitionLineId"] =
                        ["The requisition line must belong to the selected approved requisition and match its item and unit."];
                }
                else
                {
                    usedRequisitionLineIds.Add(requisitionLineId);
                    requestedByRequisitionLine[requisitionLineId] =
                        requestedByRequisitionLine.GetValueOrDefault(requisitionLineId) + line.Quantity;
                }
            }
        }

        if (request.PurchaseRequisitionId is not null
            && usedRequisitionLineIds.Count != request.Lines.Count)
        {
            errors["lines"] = ["Each purchase-order line must reference a line from the selected requisition."];
        }

        if (requisitionLines.Count > 0 && requestedByRequisitionLine.Count > 0)
        {
            var requisitionLineIds = requestedByRequisitionLine.Keys.ToArray();
            var committedRows = await (
                from order in db.PurchaseOrders.AsNoTracking()
                join revision in db.PurchaseOrderRevisions.AsNoTracking()
                    on new { order.CompanyId, orderId = order.Id }
                    equals new { revision.CompanyId, orderId = revision.PurchaseOrderId }
                where order.CompanyId == companyId
                    && order.CurrentRevisionNumber == revision.RevisionNumber
                    && revision.PurchaseRequisitionId == request.PurchaseRequisitionId
                    && revision.Status != OrderStatuses.Rejected
                    && (orderId == null || order.Id != orderId.Value)
                join stableLine in db.PurchaseOrderLines.AsNoTracking()
                    on new { order.CompanyId, orderId = order.Id }
                    equals new { stableLine.CompanyId, orderId = stableLine.PurchaseOrderId }
                where stableLine.PurchaseRequisitionLineId.HasValue
                    && requisitionLineIds.Contains(stableLine.PurchaseRequisitionLineId.Value)
                join lineRevision in db.PurchaseOrderLineRevisions.AsNoTracking()
                    on new { stableLine.CompanyId, lineId = stableLine.Id, revisionId = revision.Id }
                    equals new
                    {
                        lineRevision.CompanyId,
                        lineId = lineRevision.PurchaseOrderLineId,
                        revisionId = lineRevision.PurchaseOrderRevisionId,
                    }
                select new
                {
                    RequisitionLineId = stableLine.PurchaseRequisitionLineId!.Value,
                    lineRevision.Quantity,
                }).ToListAsync(cancellationToken);
            var committedByRequisitionLine = committedRows
                .GroupBy(line => line.RequisitionLineId)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
            foreach (var (requisitionLineId, requestedQuantity) in requestedByRequisitionLine)
            {
                var sourceLine = requisitionLines[requisitionLineId];
                var committedQuantity = committedByRequisitionLine.GetValueOrDefault(requisitionLineId);
                if (committedQuantity + requestedQuantity > sourceLine.Quantity)
                {
                    errors["lines"] =
                        ["Purchase-order quantities cannot exceed the remaining approved requisition quantity."];
                }
            }
        }

        if (orderId is { } existingOrderId)
        {
            var allowedLineIds = await db.PurchaseOrderLines.AsNoTracking()
                .Where(line => line.CompanyId == companyId && line.PurchaseOrderId == existingOrderId)
                .Select(line => line.Id)
                .ToArrayAsync(cancellationToken);
            if (request.Lines.Any(line => line.LineId is { } id && !allowedLineIds.Contains(id)))
            {
                errors["lines"] = ["Line identifiers must belong to this purchase order."];
            }
        }
        else if (!allowNewLines && request.Lines.Any(line => line.LineId is not null))
        {
            errors["lines"] = ["New purchase-order lines cannot include identifiers."];
        }

        return errors;
    }

    private async Task ReplacePurchaseOrderLinesAsync(
        Guid companyId,
        Guid orderId,
        Guid revisionId,
        IReadOnlyList<PurchaseOrderLineRequest> requested,
        Guid? priorRevisionId,
        CancellationToken cancellationToken)
    {
        var priorLines = priorRevisionId is null
            ? new Dictionary<Guid, PurchaseOrderLineRevision>()
            : await db.PurchaseOrderLineRevisions.AsNoTracking()
                .Where(line => line.CompanyId == companyId
                    && line.PurchaseOrderRevisionId == priorRevisionId)
                .ToDictionaryAsync(line => line.PurchaseOrderLineId, cancellationToken);
        foreach (var input in requested)
        {
            var lineId = input.LineId is { } requestedId && priorLines.ContainsKey(requestedId)
                ? requestedId
                : Guid.NewGuid();
            if (lineId == Guid.Empty)
            {
                lineId = Guid.NewGuid();
            }

            if (!priorLines.ContainsKey(lineId))
            {
                db.PurchaseOrderLines.Add(new PurchaseOrderLine
                {
                    Id = lineId,
                    CompanyId = companyId,
                    PurchaseOrderId = orderId,
                    PurchaseRequisitionLineId = input.PurchaseRequisitionLineId,
                });
            }
            else
            {
                var stableLine = await db.PurchaseOrderLines.SingleAsync(line =>
                    line.CompanyId == companyId && line.Id == lineId, cancellationToken);
                if (stableLine.PurchaseRequisitionLineId != input.PurchaseRequisitionLineId)
                {
                    throw new InvalidOperationException(
                        "An amended purchase-order line cannot change its requisition source.");
                }
            }

            db.PurchaseOrderLineRevisions.Add(new PurchaseOrderLineRevision
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                PurchaseOrderRevisionId = revisionId,
                PurchaseOrderLineId = lineId,
                ItemId = input.ItemId,
                UnitOfMeasureId = input.UnitOfMeasureId,
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                ExpectedDeliveryDate = input.ExpectedDeliveryDate,
            });
        }
    }

    private async Task<PurchaseOrderRevision?> GetCurrentPurchaseOrderAsync(
        Guid companyId, PurchaseOrder order, CancellationToken cancellationToken) =>
        await db.PurchaseOrderRevisions.SingleOrDefaultAsync(revision =>
            revision.CompanyId == companyId
            && revision.PurchaseOrderId == order.Id
            && revision.RevisionNumber == order.CurrentRevisionNumber,
            cancellationToken);

    private async Task<PurchaseRequisitionResponse> MapPurchaseRequisitionAsync(
        Guid companyId,
        PurchaseRequisition requisition,
        CancellationToken cancellationToken)
    {
        var lines = await (
            from line in db.PurchaseRequisitionLines.AsNoTracking()
            where line.CompanyId == companyId && line.PurchaseRequisitionId == requisition.Id
            join item in db.Items.AsNoTracking()
                on new { line.CompanyId, Id = line.ItemId }
                equals new { item.CompanyId, item.Id }
            join unit in db.UnitsOfMeasure.AsNoTracking()
                on new { line.CompanyId, Id = line.UnitOfMeasureId }
                equals new { unit.CompanyId, unit.Id }
            orderby line.Id
            select new PurchaseRequisitionLineResponse(
                line.Id, item.Id, item.Code, item.Name,
                unit.Id, unit.Code, line.Quantity, line.RequiredByDate))
            .ToArrayAsync(cancellationToken);
        return new PurchaseRequisitionResponse(
            requisition.Id, requisition.RequisitionNumber, requisition.Status,
            requisition.CreatedAt, requisition.CreatedBySubject,
            requisition.SubmittedAt, requisition.ApprovedAt,
            requisition.ApprovedBySubject, requisition.RejectionReason, lines);
    }

    private async Task<PurchaseOrderResponse> MapPurchaseOrderAsync(
        Guid companyId,
        PurchaseOrder order,
        PurchaseOrderRevision revision,
        CancellationToken cancellationToken)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == revision.SupplierId, cancellationToken);
        var currentLines = await (
            from line in db.PurchaseOrderLineRevisions.AsNoTracking()
            where line.CompanyId == companyId && line.PurchaseOrderRevisionId == revision.Id
            join stableLine in db.PurchaseOrderLines.AsNoTracking()
                on new { line.CompanyId, Id = line.PurchaseOrderLineId }
                equals new { stableLine.CompanyId, stableLine.Id }
            join item in db.Items.AsNoTracking()
                on new { line.CompanyId, Id = line.ItemId }
                equals new { item.CompanyId, item.Id }
            join unit in db.UnitsOfMeasure.AsNoTracking()
                on new { line.CompanyId, Id = line.UnitOfMeasureId }
                equals new { unit.CompanyId, unit.Id }
            select new
            {
                LineId = line.PurchaseOrderLineId,
                stableLine.PurchaseRequisitionLineId,
                line.ItemId,
                ItemCode = item.Code,
                ItemName = item.Name,
                line.UnitOfMeasureId,
                UnitOfMeasureCode = unit.Code,
                line.Quantity,
                line.UnitPrice,
                line.ExpectedDeliveryDate,
            }).ToListAsync(cancellationToken);
        var lineIds = currentLines.Select(line => line.LineId).ToArray();
        var receipts = await db.GoodsReceiptLines.AsNoTracking()
            .Where(line => line.CompanyId == companyId && lineIds.Contains(line.PurchaseOrderLineId))
            .GroupBy(line => line.PurchaseOrderLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(line => line.Quantity) })
            .ToDictionaryAsync(line => line.LineId, line => line.Quantity, cancellationToken);
        var responseLines = currentLines.Select(line =>
        {
            var received = receipts.GetValueOrDefault(line.LineId);
            return new PurchaseOrderLineResponse(
                line.LineId, line.PurchaseRequisitionLineId,
                line.ItemId, line.ItemCode, line.ItemName,
                line.UnitOfMeasureId, line.UnitOfMeasureCode,
                line.Quantity, line.UnitPrice, line.ExpectedDeliveryDate,
                received, line.Quantity - received);
        }).ToArray();
        var status = CommerceStatus.PurchaseOrderStatus(
            revision.Status,
            responseLines.Sum(line => line.ReceivedQuantity),
            responseLines.Sum(line => line.Quantity));
        return new PurchaseOrderResponse(
            order.Id, order.OrderNumber, revision.RevisionNumber,
            supplier.Id, supplier.Code, supplier.Name, revision.PurchaseRequisitionId,
            revision.CurrencyCode, status,
            revision.CreatedAt, revision.SubmittedAt, revision.ApprovedAt,
            revision.ApprovedBySubject, revision.IssuedAt,
            revision.RejectionReason, responseLines);
    }

    private static PurchaseOrderRevision NewPurchaseOrderRevision(
        Guid companyId,
        Guid orderId,
        int revisionNumber,
        MasterDataActor actor,
        PurchaseOrderRequest request) =>
        new()
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            PurchaseOrderId = orderId,
            RevisionNumber = revisionNumber,
            SupplierId = request.SupplierId,
            PurchaseRequisitionId = request.PurchaseRequisitionId,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Status = OrderStatuses.Draft,
            CreatedBySubject = actor.Subject,
        };

    private void AddRequisitionLines(
        Guid companyId, Guid requisitionId, IReadOnlyList<PurchaseRequisitionLineRequest> lines)
    {
        foreach (var line in lines)
        {
            db.PurchaseRequisitionLines.Add(new PurchaseRequisitionLine
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                PurchaseRequisitionId = requisitionId,
                ItemId = line.ItemId,
                UnitOfMeasureId = line.UnitOfMeasureId,
                Quantity = line.Quantity,
                RequiredByDate = line.RequiredByDate,
            });
        }
    }
}
