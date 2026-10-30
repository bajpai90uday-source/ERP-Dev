using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.Operations;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService
{
    public async Task<IReadOnlyList<DispatchShipmentResponse>> ListDispatchShipmentsAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var shipments = await db.DispatchShipments.AsNoTracking()
            .Where(value => value.CompanyId == companyId)
            .OrderByDescending(value => value.CreatedAt)
            .Take(250)
            .ToListAsync(cancellationToken);
        var result = new List<DispatchShipmentResponse>(shipments.Count);
        foreach (var shipment in shipments)
        {
            result.Add(await MapDispatchShipmentAsync(companyId, shipment, cancellationToken));
        }
        return result;
    }

    public Task<MasterDataResult<DispatchShipmentResponse>> CreateDispatchPlanAsync(
        Guid companyId, MasterDataActor actor, string key, DispatchPlanRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "dispatch.plan.create",
            request, async () =>
            {
                var errors = Validate(request);
                var requestedLines = request.Lines ?? [];
                var cartons = request.Cartons ?? [];
                if (requestedLines.Count == 0
                    || requestedLines.Select(value => value.SalesOrderLineId).Distinct().Count()
                        != requestedLines.Count)
                {
                    errors["lines"] = ["Provide each sales order line once with a positive dispatch quantity."];
                }
                if (cartons.Count == 0
                    || cartons.Select(value => value.CartonNumber.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                        != cartons.Count)
                {
                    errors["cartons"] = ["Provide cartons with unique carton numbers."];
                }
                if (errors.Count > 0)
                {
                    return Invalid<DispatchShipmentResponse>(errors);
                }

                var order = await db.SalesOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == request.SalesOrderId,
                    cancellationToken);
                if (order is null)
                {
                    return NotFound<DispatchShipmentResponse>();
                }
                var revision = await db.SalesOrderRevisions.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.SalesOrderId == order.Id
                    && value.RevisionNumber == order.CurrentRevisionNumber,
                    cancellationToken);
                if (revision is null || revision.Status != OrderStatuses.Confirmed)
                {
                    return Conflict<DispatchShipmentResponse>(
                        "A dispatch plan requires the current confirmed Sales Order revision.");
                }

                var orderLines = await db.SalesOrderLines.Where(value =>
                    value.CompanyId == companyId && value.SalesOrderRevisionId == revision.Id
                    && requestedLines.Select(input => input.SalesOrderLineId).Contains(value.Id))
                    .ToListAsync(cancellationToken);
                if (orderLines.Count != requestedLines.Count)
                {
                    return Invalid<DispatchShipmentResponse>(
                        "lines", "Every dispatch line must belong to the confirmed order revision.");
                }

                var lineIds = orderLines.Select(value => value.Id).ToArray();
                var variants = await db.ProductVariants.Where(value =>
                    value.CompanyId == companyId
                    && orderLines.Select(line => line.ProductVariantId).Contains(value.Id))
                    .ToDictionaryAsync(value => value.Id, cancellationToken);
                if (variants.Count != orderLines.Select(value => value.ProductVariantId).Distinct().Count())
                {
                    return Invalid<DispatchShipmentResponse>("lines", "A product variant is missing.");
                }

                var fulfillmentTotals = await db.SalesOrderFulfillments.AsNoTracking()
                    .Where(value => value.CompanyId == companyId && lineIds.Contains(value.SalesOrderLineId))
                    .GroupBy(value => value.SalesOrderLineId)
                    .Select(group => new { LineId = group.Key, Quantity = group.Sum(value => value.Quantity) })
                    .ToDictionaryAsync(value => value.LineId, value => value.Quantity, cancellationToken);
                var byLine = requestedLines.ToDictionary(value => value.SalesOrderLineId);
                foreach (var line in orderLines)
                {
                    var input = byLine[line.Id];
                    if (input.Quantity <= 0m
                        || fulfillmentTotals.GetValueOrDefault(line.Id) + input.Quantity > line.Quantity)
                    {
                        return Conflict<DispatchShipmentResponse>(
                            "Dispatch quantity exceeds the Sales Order line's outstanding quantity.");
                    }
                    var variant = variants[line.ProductVariantId];
                    if (!await db.Warehouses.AnyAsync(value =>
                        value.CompanyId == companyId && value.Id == input.WarehouseId && value.IsActive,
                        cancellationToken))
                    {
                        return Invalid<DispatchShipmentResponse>(
                            "warehouseId", "Choose an active warehouse belonging to this company.");
                    }
                    if (variant.ItemId == Guid.Empty)
                    {
                        return Invalid<DispatchShipmentResponse>("lines", "A dispatch variant must reference a stock item.");
                    }
                }

                var packedTotals = cartons.SelectMany(value => value.Contents ?? [])
                    .GroupBy(value => value.SalesOrderLineId)
                    .ToDictionary(group => group.Key, group => group.Sum(value => value.Quantity));
                if (cartons.Any(value => value.Weight < 0m
                        || value.Contents is null || value.Contents.Count == 0)
                    || requestedLines.Any(value =>
                        packedTotals.GetValueOrDefault(value.SalesOrderLineId) != value.Quantity)
                    || packedTotals.Keys.Except(lineIds).Any()
                    || cartons.SelectMany(value => value.Contents)
                        .Any(value => value.Quantity <= 0m || !lineIds.Contains(value.SalesOrderLineId)))
                {
                    return Invalid<DispatchShipmentResponse>(
                        "cartons", "Carton contents must use planned order lines and exactly equal each planned dispatch quantity.");
                }

                var shipment = new DispatchShipment
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    ShipmentNumber = NewDocumentNumber("DS"),
                    SalesOrderId = order.Id,
                    SalesOrderRevisionId = revision.Id,
                    CreatedBySubject = actor.Subject,
                };
                db.DispatchShipments.Add(shipment);
                var shipmentLines = new Dictionary<Guid, DispatchShipmentLine>();
                foreach (var line in orderLines)
                {
                    var input = byLine[line.Id];
                    var shipmentLine = new DispatchShipmentLine
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        DispatchShipmentId = shipment.Id,
                        SalesOrderLineId = line.Id,
                        ProductVariantId = line.ProductVariantId,
                        ItemId = variants[line.ProductVariantId].ItemId,
                        WarehouseId = input.WarehouseId,
                        Quantity = input.Quantity,
                    };
                    shipmentLines.Add(line.Id, shipmentLine);
                    db.DispatchShipmentLines.Add(shipmentLine);
                }

                foreach (var cartonRequest in cartons)
                {
                    var carton = new DispatchCarton
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        DispatchShipmentId = shipment.Id,
                        CartonNumber = cartonRequest.CartonNumber.Trim(),
                        Weight = cartonRequest.Weight,
                    };
                    db.DispatchCartons.Add(carton);
                    foreach (var content in cartonRequest.Contents)
                    {
                        var shipmentLine = shipmentLines[content.SalesOrderLineId];
                        db.DispatchCartonLines.Add(new DispatchCartonLine
                        {
                            Id = Guid.NewGuid(),
                            CompanyId = companyId,
                            DispatchCartonId = carton.Id,
                            DispatchShipmentLineId = shipmentLine.Id,
                            Quantity = content.Quantity,
                        });
                    }
                }
                AddEvent(companyId, actor, "dispatch-shipment", shipment.Id, null,
                    "dispatch_plan_created", new { shipment.ShipmentNumber, lineCount = shipmentLines.Count });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<DispatchShipmentResponse>(
                    await MapDispatchShipmentAsync(companyId, shipment, cancellationToken),
                    StatusCodes.Status201Created, ResourceId: shipment.Id);
            }, cancellationToken);

    public Task<MasterDataResult<DispatchShipmentResponse>> AuthorizeDispatchAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "dispatch.authorize",
            new { id }, async () =>
            {
                var shipment = await db.DispatchShipments.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (shipment is null)
                {
                    return NotFound<DispatchShipmentResponse>();
                }
                if (shipment.Status != DispatchStatuses.Draft)
                {
                    return Conflict<DispatchShipmentResponse>("Only a draft dispatch plan can be authorized.");
                }
                var order = await db.SalesOrders.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == shipment.SalesOrderId,
                    cancellationToken);
                var revision = await db.SalesOrderRevisions.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == shipment.SalesOrderRevisionId,
                    cancellationToken);
                if (revision is null || revision.Status != OrderStatuses.Confirmed
                    || order.CurrentRevisionNumber != revision.RevisionNumber)
                {
                    return Conflict<DispatchShipmentResponse>("Only the current confirmed order revision can be dispatched.");
                }

                var lines = await db.DispatchShipmentLines.Where(value =>
                    value.CompanyId == companyId && value.DispatchShipmentId == shipment.Id)
                    .ToListAsync(cancellationToken);
                var lineIds = lines.Select(value => value.SalesOrderLineId).ToArray();
                var fulfillments = await db.SalesOrderFulfillments.AsNoTracking()
                    .Where(value => value.CompanyId == companyId && lineIds.Contains(value.SalesOrderLineId))
                    .GroupBy(value => value.SalesOrderLineId)
                    .Select(group => new { Id = group.Key, Quantity = group.Sum(value => value.Quantity) })
                    .ToDictionaryAsync(value => value.Id, value => value.Quantity, cancellationToken);
                var orderLines = await db.SalesOrderLines.Where(value =>
                    value.CompanyId == companyId && value.SalesOrderRevisionId == revision.Id
                    && lineIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
                var authorizedLines = await (
                    from other in db.DispatchShipments.AsNoTracking()
                    join otherLine in db.DispatchShipmentLines.AsNoTracking()
                        on new { other.CompanyId, DispatchShipmentId = other.Id }
                        equals new { otherLine.CompanyId, otherLine.DispatchShipmentId }
                    where other.CompanyId == companyId && other.SalesOrderRevisionId == revision.Id
                        && other.Status == DispatchStatuses.Authorized && other.Id != shipment.Id
                        && lineIds.Contains(otherLine.SalesOrderLineId)
                    select otherLine).ToListAsync(cancellationToken);
                var blockedByAuthorized = authorizedLines.GroupBy(value => value.SalesOrderLineId)
                    .ToDictionary(group => group.Key, group => group.Sum(value => value.Quantity));
                foreach (var line in lines)
                {
                    var orderLine = orderLines.GetValueOrDefault(line.SalesOrderLineId);
                    if (orderLine is null
                        || fulfillments.GetValueOrDefault(line.SalesOrderLineId)
                            + blockedByAuthorized.GetValueOrDefault(line.SalesOrderLineId)
                            + line.Quantity > orderLine.Quantity)
                    {
                        return Conflict<DispatchShipmentResponse>(
                            "Dispatch authorization exceeds the remaining order quantity.");
                    }
                }

                foreach (var group in lines.GroupBy(value => new { value.ItemId, value.WarehouseId }))
                {
                    var balance = await db.InventoryStockBalances.SingleOrDefaultAsync(value =>
                        value.CompanyId == companyId && value.ItemId == group.Key.ItemId
                        && value.WarehouseId == group.Key.WarehouseId
                        && value.StockStatus == InventoryStockStatuses.Available,
                        cancellationToken);
                    var requestedQuantity = group.Sum(value => value.Quantity);
                    if (balance is null || balance.OnHandQuantity - balance.ReservedQuantity < requestedQuantity)
                    {
                        return Conflict<DispatchShipmentResponse>(
                            "Dispatch authorization exceeds eligible, unreserved warehouse stock.");
                    }
                    balance.ReservedQuantity += requestedQuantity;
                    balance.ConcurrencyVersion++;
                }

                shipment.Status = DispatchStatuses.Authorized;
                shipment.AuthorizedAt = DateTimeOffset.UtcNow;
                shipment.AuthorizedBySubject = actor.Subject;
                AddEvent(companyId, actor, "dispatch-shipment", shipment.Id, null,
                    "dispatch_authorized", new { shipment.ShipmentNumber });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<DispatchShipmentResponse>(
                    await MapDispatchShipmentAsync(companyId, shipment, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<DispatchShipmentResponse>> ConfirmDispatchAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "dispatch.confirm",
            new { id }, async () =>
            {
                var shipment = await db.DispatchShipments.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (shipment is null)
                {
                    return NotFound<DispatchShipmentResponse>();
                }
                if (shipment.Status != DispatchStatuses.Authorized)
                {
                    return Conflict<DispatchShipmentResponse>("Only an authorized shipment can be dispatched.");
                }
                var lines = await db.DispatchShipmentLines.Where(value =>
                    value.CompanyId == companyId && value.DispatchShipmentId == shipment.Id)
                    .ToListAsync(cancellationToken);
                foreach (var group in lines.GroupBy(value => new { value.ItemId, value.WarehouseId }))
                {
                    var balance = await db.InventoryStockBalances.SingleOrDefaultAsync(value =>
                        value.CompanyId == companyId && value.ItemId == group.Key.ItemId
                        && value.WarehouseId == group.Key.WarehouseId
                        && value.StockStatus == InventoryStockStatuses.Available,
                        cancellationToken);
                    var quantity = group.Sum(value => value.Quantity);
                    if (balance is null || balance.OnHandQuantity < quantity
                        || balance.ReservedQuantity < quantity)
                    {
                        return Conflict<DispatchShipmentResponse>(
                            "Authorized stock reservation is no longer available; reload the dispatch plan.");
                    }
                    balance.OnHandQuantity -= quantity;
                    balance.ReservedQuantity -= quantity;
                    balance.ConcurrencyVersion++;
                }

                foreach (var line in lines)
                {
                    db.InventoryLedgerEntries.Add(new InventoryLedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        ItemId = line.ItemId,
                        WarehouseId = line.WarehouseId,
                        StockStatus = InventoryStockStatuses.Available,
                        TransactionType = "issue",
                        Quantity = -line.Quantity,
                        ReferenceType = "dispatch-shipment",
                        ReferenceId = shipment.Id,
                        ReferenceLineId = line.Id,
                        ActorSubject = actor.Subject,
                    });
                    db.SalesOrderFulfillments.Add(new SalesOrderFulfillment
                    {
                        Id = Guid.NewGuid(),
                        CompanyId = companyId,
                        SalesOrderRevisionId = shipment.SalesOrderRevisionId,
                        SalesOrderLineId = line.SalesOrderLineId,
                        Reference = shipment.ShipmentNumber,
                        Quantity = line.Quantity,
                        ActorSubject = actor.Subject,
                    });
                }
                shipment.Status = DispatchStatuses.Dispatched;
                shipment.DispatchedAt = DateTimeOffset.UtcNow;
                shipment.DispatchedBySubject = actor.Subject;
                AddEvent(companyId, actor, "dispatch-shipment", shipment.Id, null,
                    "dispatch_confirmed", new
                    {
                        shipment.ShipmentNumber,
                        quantities = lines.Select(value => new { value.SalesOrderLineId, value.Quantity }),
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<DispatchShipmentResponse>(
                    await MapDispatchShipmentAsync(companyId, shipment, cancellationToken));
            }, cancellationToken);

    private async Task<DispatchShipmentResponse> MapDispatchShipmentAsync(
        Guid companyId, DispatchShipment shipment, CancellationToken cancellationToken)
    {
        var orderNumber = await db.SalesOrders.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.Id == shipment.SalesOrderId)
            .Select(value => value.OrderNumber).SingleAsync(cancellationToken);
        var lineRows = await (
            from line in db.DispatchShipmentLines.AsNoTracking()
            join variant in db.ProductVariants.AsNoTracking()
                on new { line.CompanyId, line.ProductVariantId }
                equals new { variant.CompanyId, ProductVariantId = variant.Id }
            join warehouse in db.Warehouses.AsNoTracking()
                on new { line.CompanyId, line.WarehouseId }
                equals new { warehouse.CompanyId, WarehouseId = warehouse.Id }
            where line.CompanyId == companyId && line.DispatchShipmentId == shipment.Id
            orderby line.Id
            select new DispatchPlanLineResponse(
                line.Id, line.SalesOrderLineId, line.ProductVariantId, variant.Code,
                variant.SizeCode ?? string.Empty, variant.ColorCode ?? string.Empty,
                variant.ColorName ?? string.Empty, line.ItemId, line.WarehouseId,
                warehouse.Code, line.Quantity)).ToArrayAsync(cancellationToken);
        var cartonRows = await db.DispatchCartons.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.DispatchShipmentId == shipment.Id)
            .OrderBy(value => value.CartonNumber).ToListAsync(cancellationToken);
        var cartons = new List<DispatchCartonResponse>(cartonRows.Count);
        foreach (var carton in cartonRows)
        {
            var contents = await (
                from content in db.DispatchCartonLines.AsNoTracking()
                join line in db.DispatchShipmentLines.AsNoTracking()
                    on new { content.CompanyId, DispatchShipmentLineId = content.DispatchShipmentLineId }
                    equals new { line.CompanyId, DispatchShipmentLineId = line.Id }
                join variant in db.ProductVariants.AsNoTracking()
                    on new { line.CompanyId, line.ProductVariantId }
                    equals new { variant.CompanyId, ProductVariantId = variant.Id }
                where content.CompanyId == companyId && content.DispatchCartonId == carton.Id
                select new DispatchCartonContentResponse(
                    line.Id, variant.Code, variant.SizeCode ?? string.Empty,
                    variant.ColorCode ?? string.Empty, content.Quantity))
                .ToArrayAsync(cancellationToken);
            cartons.Add(new DispatchCartonResponse(
                carton.Id, carton.CartonNumber, carton.Weight, contents));
        }
        return new DispatchShipmentResponse(
            shipment.Id, shipment.ShipmentNumber, shipment.SalesOrderId, orderNumber,
            shipment.Status, shipment.CreatedAt, shipment.CreatedBySubject,
            shipment.AuthorizedBySubject, shipment.AuthorizedAt,
            shipment.DispatchedBySubject, shipment.DispatchedAt, lineRows, cartons);
    }
}
