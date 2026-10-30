using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService
{
    public async Task<PageResponse<SalesOrderResponse>> ListSalesOrdersAsync(
        Guid companyId,
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var orders = db.SalesOrders.AsNoTracking().Where(value => value.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            orders = orders.Where(value =>
                value.OrderNumber.ToLower().Contains(term)
                || value.CustomerPurchaseOrderReference.ToLower().Contains(term));
        }

        var total = await orders.CountAsync(cancellationToken);
        var roots = await orders.OrderByDescending(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var responses = new List<SalesOrderResponse>(roots.Count);
        foreach (var order in roots)
        {
            var current = await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
            if (current is not null)
            {
                responses.Add(await MapSalesOrderAsync(companyId, order, current, cancellationToken));
            }
        }

        return new PageResponse<SalesOrderResponse>(responses, page, pageSize, total);
    }

    public async Task<MasterDataResult<SalesOrderResponse>> GetSalesOrderAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        var order = await db.SalesOrders.AsNoTracking().SingleOrDefaultAsync(
            value => value.CompanyId == companyId && value.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound<SalesOrderResponse>();
        }

        var revision = await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
        return revision is null
            ? NotFound<SalesOrderResponse>()
            : new MasterDataResult<SalesOrderResponse>(
                await MapSalesOrderAsync(companyId, order, revision, cancellationToken));
    }

    public Task<MasterDataResult<SalesOrderResponse>> CreateSalesOrderAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey, SalesOrderRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, "sales.order.create", request,
            async () =>
            {
                var errors = await ValidateSalesOrderAsync(companyId, request, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<SalesOrderResponse>(errors);
                }

                var reference = request.CustomerPurchaseOrderReference.Trim();
                if (await db.SalesOrders.AnyAsync(value =>
                        value.CompanyId == companyId
                        && value.CustomerId == request.CustomerId
                        && value.CustomerPurchaseOrderReference == reference,
                        cancellationToken))
                {
                    return Conflict<SalesOrderResponse>(
                        "A sales order already uses this customer purchase-order reference.");
                }

                var order = new SalesOrder
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    OrderNumber = NewDocumentNumber("SO"),
                    CustomerId = request.CustomerId,
                    CustomerPurchaseOrderReference = reference,
                    CurrentRevisionNumber = 1,
                    CreatedBySubject = actor.Subject,
                };
                var revision = NewSalesRevision(companyId, order.Id, 1, actor, request);
                db.SalesOrders.Add(order);
                db.SalesOrderRevisions.Add(revision);
                AddSalesLines(companyId, revision.Id, request.Lines);
                AddEvent(companyId, actor, "sales-order", order.Id, 1, "created",
                    new { order.OrderNumber, request.CustomerId, customerPurchaseOrderReference = reference });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    StatusCodes.Status201Created,
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<SalesOrderResponse>> UpdateSalesDraftAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        SalesOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, "sales.order.draft.update",
            new { id, request }, async () =>
            {
                var errors = await ValidateSalesOrderAsync(companyId, request, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<SalesOrderResponse>(errors);
                }

                var order = await db.SalesOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<SalesOrderResponse>();
                }

                if (revision.Status is not (OrderStatuses.Draft or OrderStatuses.Rejected))
                {
                    return Conflict<SalesOrderResponse>(
                        "Only a draft or rejected sales-order revision can be edited.");
                }

                if (order.CustomerId != request.CustomerId
                    || !string.Equals(
                        order.CustomerPurchaseOrderReference,
                        request.CustomerPurchaseOrderReference.Trim(),
                        StringComparison.Ordinal))
                {
                    return Invalid<SalesOrderResponse>(
                        "customerPurchaseOrderReference",
                        "The customer and buyer PO reference are fixed when the sales order is created.");
                }

                db.SalesOrderLines.RemoveRange(await db.SalesOrderLines
                    .Where(line => line.CompanyId == companyId
                        && line.SalesOrderRevisionId == revision.Id)
                    .ToListAsync(cancellationToken));
                ApplySalesHeader(revision, request);
                revision.Status = OrderStatuses.Draft;
                revision.ConcurrencyVersion++;
                revision.RejectionReason = null;
                AddSalesLines(companyId, revision.Id, request.Lines);
                AddEvent(companyId, actor, "sales-order", order.Id, revision.RevisionNumber,
                    "draft_updated", new { lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<SalesOrderResponse>> SubmitSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ChangeSalesOrderStatusAsync(companyId, actor, id, idempotencyKey,
            "sales.order.submit", OrderStatuses.Draft, OrderStatuses.Submitted,
            cancellationToken, revision =>
            {
                revision.SubmittedAt = DateTimeOffset.UtcNow;
                revision.SubmittedBySubject = actor.Subject;
            });

    public Task<MasterDataResult<SalesOrderResponse>> ApproveSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, "sales.order.approve", new { id },
            async () =>
            {
                var order = await db.SalesOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<SalesOrderResponse>();
                }

                if (revision.Status != OrderStatuses.Submitted)
                {
                    return Conflict<SalesOrderResponse>("Only a submitted sales order can be approved.");
                }

                if (string.Equals(revision.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<SalesOrderResponse>(
                        "The person who submitted a sales order cannot approve the same revision.");
                }

                revision.Status = OrderStatuses.Approved;
                revision.ApprovedAt = DateTimeOffset.UtcNow;
                revision.ApprovedBySubject = actor.Subject;
                revision.ConcurrencyVersion++;
                AddEvent(companyId, actor, "sales-order", order.Id, revision.RevisionNumber,
                    "approved", new { revision.ApprovedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<SalesOrderResponse>> RejectSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        RejectionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, "sales.order.reject",
            new { id, request }, async () =>
            {
                var validation = Validate(request);
                if (validation.Count > 0)
                {
                    return Invalid<SalesOrderResponse>(validation);
                }

                var order = await db.SalesOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<SalesOrderResponse>();
                }

                if (revision.Status != OrderStatuses.Submitted)
                {
                    return Conflict<SalesOrderResponse>("Only a submitted sales order can be rejected.");
                }

                if (string.Equals(revision.SubmittedBySubject, actor.Subject, StringComparison.Ordinal))
                {
                    return Conflict<SalesOrderResponse>(
                        "The person who submitted a sales order cannot reject the same revision.");
                }

                revision.Status = OrderStatuses.Rejected;
                revision.RejectionReason = request.Reason.Trim();
                revision.ConcurrencyVersion++;
                AddEvent(companyId, actor, "sales-order", order.Id, revision.RevisionNumber,
                    "rejected", new { }, revision.RejectionReason);
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<SalesOrderResponse>> ConfirmSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, "sales.order.confirm", new { id },
            async () =>
            {
                var order = await db.SalesOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<SalesOrderResponse>();
                }

                if (revision.Status != OrderStatuses.Approved)
                {
                    return Conflict<SalesOrderResponse>(
                        "Only an approved sales-order revision can be confirmed.");
                }

                revision.Status = OrderStatuses.Confirmed;
                revision.ConfirmedAt = DateTimeOffset.UtcNow;
                revision.ConcurrencyVersion++;
                AddEvent(companyId, actor, "sales-order", order.Id, revision.RevisionNumber,
                    "confirmed", new { revision.ConfirmedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<SalesOrderResponse>> AmendSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        SalesOrderRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, "sales.order.amend",
            new { id, request }, async () =>
            {
                var errors = await ValidateSalesOrderAsync(companyId, request, cancellationToken);
                if (errors.Count > 0)
                {
                    return Invalid<SalesOrderResponse>(errors);
                }

                var order = await db.SalesOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var prior = order is null
                    ? null
                    : await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
                if (order is null || prior is null)
                {
                    return NotFound<SalesOrderResponse>();
                }

                if (prior.Status != OrderStatuses.Confirmed)
                {
                    return Conflict<SalesOrderResponse>(
                        "Only a confirmed sales order can be amended.");
                }

                if (order.CustomerId != request.CustomerId
                    || !string.Equals(order.CustomerPurchaseOrderReference,
                        request.CustomerPurchaseOrderReference.Trim(), StringComparison.Ordinal))
                {
                    return Invalid<SalesOrderResponse>(
                        "customerPurchaseOrderReference",
                        "An amendment cannot change the customer or buyer PO reference.");
                }

                var priorLines = await db.SalesOrderLines
                    .Where(line => line.CompanyId == companyId
                        && line.SalesOrderRevisionId == prior.Id)
                    .Select(line => line.Id)
                    .ToArrayAsync(cancellationToken);
                if (await db.SalesOrderFulfillments.AnyAsync(
                        fulfillment => fulfillment.CompanyId == companyId
                            && priorLines.Contains(fulfillment.SalesOrderLineId),
                        cancellationToken))
                {
                    return Conflict<SalesOrderResponse>(
                        "A fulfilled sales order cannot be amended in this release.");
                }

                if (await db.WorkOrders.AnyAsync(workOrder =>
                        workOrder.CompanyId == companyId
                        && priorLines.Contains(workOrder.SalesOrderLineId)
                        && workOrder.Status != WorkOrderStatuses.Cancelled,
                        cancellationToken))
                {
                    return Conflict<SalesOrderResponse>(
                        "Cancel linked Work Orders before amending the confirmed sales order.");
                }

                var revision = NewSalesRevision(
                    companyId, order.Id, order.CurrentRevisionNumber + 1, actor, request);
                order.CurrentRevisionNumber = revision.RevisionNumber;
                db.SalesOrderRevisions.Add(revision);
                AddSalesLines(companyId, revision.Id, request.Lines);
                AddEvent(companyId, actor, "sales-order", order.Id, revision.RevisionNumber,
                    "amendment_created",
                    new { previousRevision = prior.RevisionNumber, lineCount = request.Lines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    public Task<MasterDataResult<SalesOrderResponse>> FulfillSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        SalesFulfillmentRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Conflict<SalesOrderResponse>(
            "Direct fulfillment is disabled. Pack, authorize, and confirm a dispatch shipment."));

    public async Task<IReadOnlyList<TransactionHistoryResponse>> SalesOrderHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        if (!await db.SalesOrders.AnyAsync(
                value => value.CompanyId == companyId && value.Id == id, cancellationToken))
        {
            return [];
        }

        return await GetHistoryAsync(companyId, "sales-order", id, cancellationToken);
    }

    private Task<MasterDataResult<SalesOrderResponse>> ChangeSalesOrderStatusAsync(
        Guid companyId,
        MasterDataActor actor,
        Guid id,
        string idempotencyKey,
        string operation,
        string expectedStatus,
        string nextStatus,
        CancellationToken cancellationToken,
        Action<SalesOrderRevision>? update = null) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey, operation, new { id },
            async () =>
            {
                var order = await db.SalesOrders.SingleOrDefaultAsync(
                    value => value.CompanyId == companyId && value.Id == id, cancellationToken);
                var revision = order is null
                    ? null
                    : await GetCurrentSalesOrderAsync(companyId, order, cancellationToken);
                if (order is null || revision is null)
                {
                    return NotFound<SalesOrderResponse>();
                }

                if (revision.Status != expectedStatus)
                {
                    return Conflict<SalesOrderResponse>(
                        $"A sales order in '{revision.Status}' status cannot perform this action.");
                }

                revision.Status = nextStatus;
                revision.ConcurrencyVersion++;
                update?.Invoke(revision);
                AddEvent(companyId, actor, "sales-order", order.Id, revision.RevisionNumber,
                    operation[(operation.LastIndexOf('.') + 1)..], new { status = nextStatus });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<SalesOrderResponse>(
                    await MapSalesOrderAsync(companyId, order, revision, cancellationToken),
                    ResourceId: order.Id);
            }, cancellationToken);

    private async Task<Dictionary<string, string[]>> ValidateSalesOrderAsync(
        Guid companyId, SalesOrderRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (!IsValidRequestList(request.Lines))
        {
            errors["lines"] = ["Add between 1 and 500 sales order lines."];
            return errors;
        }

        if (request.CustomerId == Guid.Empty)
        {
            errors["customerId"] = ["Select a customer."];
        }

        if (!await db.Customers.AnyAsync(customer =>
                customer.CompanyId == companyId && customer.Id == request.CustomerId
                && customer.IsActive, cancellationToken))
        {
            errors["customerId"] = ["Choose an active customer in this company."];
        }

        if (!IsCurrencyCode(request.CurrencyCode ?? string.Empty))
        {
            errors["currencyCode"] = ["Use a three-letter uppercase ISO 4217 currency code."];
        }

        if (request.CustomerPurchaseOrderDate == default)
        {
            errors["customerPurchaseOrderDate"] = ["Provide the buyer purchase-order date."];
        }

        var variantIds = request.Lines.Select(line => line.ProductVariantId).Distinct().ToArray();
        var activeVariants = await db.ProductVariants.AsNoTracking()
            .Where(variant => variant.CompanyId == companyId
                && variantIds.Contains(variant.Id)
                && variant.IsActive
                && variant.Product.IsActive)
            .Select(variant => new { variant.Id, variant.ProductId })
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line.ProductId == Guid.Empty
                || !activeVariants.TryGetValue(line.ProductVariantId, out var variant)
                || variant.ProductId != line.ProductId)
            {
                errors[$"lines[{index}].productVariantId"] =
                    ["Choose an active variant belonging to the selected product in this company."];
            }

            if (line.RequestedDeliveryDate == default)
            {
                errors[$"lines[{index}].requestedDeliveryDate"] = ["Provide a delivery date."];
            }
        }

        return errors;
    }

    private async Task<SalesOrderRevision?> GetCurrentSalesOrderAsync(
        Guid companyId, SalesOrder order, CancellationToken cancellationToken) =>
        await db.SalesOrderRevisions.SingleOrDefaultAsync(revision =>
            revision.CompanyId == companyId
            && revision.SalesOrderId == order.Id
            && revision.RevisionNumber == order.CurrentRevisionNumber,
            cancellationToken);

    private async Task<SalesOrderResponse> MapSalesOrderAsync(
        Guid companyId,
        SalesOrder order,
        SalesOrderRevision revision,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == revision.CustomerId, cancellationToken);
        var lines = await (
            from line in db.SalesOrderLines.AsNoTracking()
            where line.CompanyId == companyId && line.SalesOrderRevisionId == revision.Id
            join product in db.Products.AsNoTracking()
                on new { line.CompanyId, Id = line.ProductId }
                equals new { product.CompanyId, product.Id }
            join variant in db.ProductVariants.AsNoTracking()
                on new { line.CompanyId, line.ProductId, Id = line.ProductVariantId }
                equals new { variant.CompanyId, variant.ProductId, variant.Id }
            join item in db.Items.AsNoTracking()
                on new { variant.CompanyId, Id = variant.ItemId }
                equals new { item.CompanyId, item.Id }
            select new
            {
                line.Id,
                line.ProductId,
                ProductCode = product.Code,
                ProductName = product.Name,
                line.ProductVariantId,
                VariantCode = variant.Code,
                VariantName = variant.Name,
                StockUnitCode = db.UnitsOfMeasure
                    .Where(unit => unit.CompanyId == companyId && unit.Id == item.StockUnitId)
                    .Select(unit => unit.Code)
                    .Single(),
                line.Quantity,
                line.UnitPrice,
                line.RequestedDeliveryDate,
            }).ToListAsync(cancellationToken);
        var lineIds = lines.Select(line => line.Id).ToArray();
        var fulfillment = await db.SalesOrderFulfillments.AsNoTracking()
            .Where(value => value.CompanyId == companyId && lineIds.Contains(value.SalesOrderLineId))
            .ToListAsync(cancellationToken);
        var responseLines = lines.Select(line =>
        {
            var fulfilled = fulfillment.Where(value => value.SalesOrderLineId == line.Id)
                .Sum(value => value.Quantity);
            return new SalesOrderLineResponse(
                line.Id, line.ProductId, line.ProductCode, line.ProductName,
                line.ProductVariantId, line.VariantCode, line.VariantName, line.StockUnitCode,
                line.Quantity, line.UnitPrice, line.RequestedDeliveryDate,
                fulfilled, line.Quantity - fulfilled);
        }).ToArray();

        return new SalesOrderResponse(
            order.Id, order.OrderNumber, revision.RevisionNumber,
            customer.Id, customer.Code, customer.Name,
            revision.CustomerPurchaseOrderReference,
            revision.CustomerPurchaseOrderDate, revision.CurrencyCode, revision.Status,
            revision.CreatedAt, revision.SubmittedAt, revision.ApprovedAt,
            revision.ApprovedBySubject, revision.ConfirmedAt, revision.RejectionReason,
            responseLines);
    }

    private static SalesOrderRevision NewSalesRevision(
        Guid companyId,
        Guid orderId,
        int revisionNumber,
        MasterDataActor actor,
        SalesOrderRequest request)
    {
        var revision = new SalesOrderRevision
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            SalesOrderId = orderId,
            RevisionNumber = revisionNumber,
            CustomerId = request.CustomerId,
            CustomerPurchaseOrderReference = request.CustomerPurchaseOrderReference.Trim(),
            CustomerPurchaseOrderDate = request.CustomerPurchaseOrderDate,
            CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant(),
            Status = OrderStatuses.Draft,
            CreatedBySubject = actor.Subject,
        };
        return revision;
    }

    private static void ApplySalesHeader(SalesOrderRevision revision, SalesOrderRequest request)
    {
        revision.CustomerPurchaseOrderDate = request.CustomerPurchaseOrderDate;
        revision.CurrencyCode = request.CurrencyCode.Trim().ToUpperInvariant();
    }

    private void AddSalesLines(
        Guid companyId, Guid revisionId, IReadOnlyList<SalesOrderLineRequest> lines)
    {
        foreach (var line in lines)
        {
            db.SalesOrderLines.Add(new SalesOrderLine
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                SalesOrderRevisionId = revisionId,
                ProductId = line.ProductId,
                ProductVariantId = line.ProductVariantId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                RequestedDeliveryDate = line.RequestedDeliveryDate,
            });
        }
    }
}
