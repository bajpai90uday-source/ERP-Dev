using Erp.Api.MasterData;
using Erp.Domain.Commerce;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService
{
    public async Task<IReadOnlyList<InventoryBalanceResponse>> GetInventoryBalancesAsync(
        Guid companyId,
        Guid? itemId,
        Guid? warehouseId,
        CancellationToken cancellationToken)
    {
        var balances =
            from balance in db.InventoryStockBalances.AsNoTracking()
            where balance.CompanyId == companyId
                && (itemId == null || balance.ItemId == itemId)
                && (warehouseId == null || balance.WarehouseId == warehouseId)
                && (balance.OnHandQuantity != 0 || balance.ReservedQuantity != 0)
            join item in db.Items.AsNoTracking()
                on new { balance.CompanyId, Id = balance.ItemId }
                equals new { item.CompanyId, item.Id }
            join warehouse in db.Warehouses.AsNoTracking()
                on new { balance.CompanyId, Id = balance.WarehouseId }
                equals new { warehouse.CompanyId, warehouse.Id }
            where warehouse.IsActive
            orderby item.Code, warehouse.Code, balance.StockStatus
            select new InventoryBalanceResponse(
                item.Id, item.Code, item.Name, warehouse.Id, warehouse.Code,
                balance.StockStatus, balance.OnHandQuantity, balance.ReservedQuantity,
                balance.OnHandQuantity - balance.ReservedQuantity);
        return await balances.ToArrayAsync(cancellationToken);
    }

    public Task<MasterDataResult<InventoryBalanceResponse>> AdjustInventoryAsync(
        Guid companyId,
        MasterDataActor actor,
        string idempotencyKey,
        InventoryAdjustmentRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "inventory.adjust", request, async () =>
            {
                if (request.Quantity == 0m)
                {
                    return Invalid<InventoryBalanceResponse>(
                        "quantity", "Adjustment quantity cannot be zero.");
                }

                var item = await db.Items.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == request.ItemId && value.IsActive,
                    cancellationToken);
                var warehouse = await db.Warehouses.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == request.WarehouseId && value.IsActive,
                    cancellationToken);
                if (item is null || warehouse is null)
                {
                    return Invalid<InventoryBalanceResponse>(
                        "itemId", "Choose an active item and warehouse in this company.");
                }

                var balance = await GetOrCreateStockBalanceAsync(
                    companyId, item.Id, warehouse.Id, request.StockStatus, cancellationToken);
                if (balance.OnHandQuantity + request.Quantity < 0m
                    || balance.OnHandQuantity + request.Quantity < balance.ReservedQuantity)
                {
                    return Conflict<InventoryBalanceResponse>(
                        "The adjustment cannot reduce inventory below zero or below reserved quantity.");
                }

                balance.OnHandQuantity += request.Quantity;
                balance.ConcurrencyVersion++;
                var entry = new InventoryLedgerEntry
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    ItemId = item.Id,
                    WarehouseId = warehouse.Id,
                    StockStatus = request.StockStatus,
                    TransactionType = "adjustment",
                    Quantity = request.Quantity,
                    ReferenceType = "inventory-adjustment",
                    ReferenceId = Guid.NewGuid(),
                    ReferenceLineId = Guid.NewGuid(),
                    ActorSubject = actor.Subject,
                };
                db.InventoryLedgerEntries.Add(entry);
                AddEvent(companyId, actor, "inventory", entry.ReferenceId, null,
                    "adjusted", new
                    {
                        itemId = item.Id,
                        warehouseId = warehouse.Id,
                        request.StockStatus,
                        request.Quantity,
                    }, request.Reason.Trim());
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<InventoryBalanceResponse>(
                    new InventoryBalanceResponse(
                        item.Id, item.Code, item.Name, warehouse.Id, warehouse.Code,
                        balance.StockStatus, balance.OnHandQuantity, balance.ReservedQuantity,
                        balance.OnHandQuantity - balance.ReservedQuantity));
            }, cancellationToken);

    public Task<MasterDataResult<MrpRunResponse>> RunMrpAsync(
        Guid companyId,
        MasterDataActor actor,
        string idempotencyKey,
        MrpRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, idempotencyKey,
            "planning.mrp.calculate", request, async () =>
            {
                if (request.FromDate == default || request.ThroughDate < request.FromDate)
                {
                    return Invalid<MrpRunResponse>(
                        "throughDate", "Provide a valid planning date range.");
                }

                if (request.ThroughDate.DayNumber - request.FromDate.DayNumber > 366)
                {
                    return Invalid<MrpRunResponse>(
                        "throughDate", "The MRP planning horizon cannot exceed 366 days.");
                }

                var demands = await LoadSalesDemandsAsync(
                    companyId, request.FromDate, request.ThroughDate, cancellationToken);
                var openWorkOrders = await db.WorkOrders.AsNoTracking()
                    .Where(value => value.CompanyId == companyId
                        && value.Status != WorkOrderStatuses.Cancelled
                        && value.Status != WorkOrderStatuses.Rejected
                        && value.Status != WorkOrderStatuses.Completed
                        && value.PlannedStartDate <= request.ThroughDate)
                    .ToListAsync(cancellationToken);
                var activeWorkOrders = openWorkOrders
                    .GroupBy(value => value.SalesOrderLineId)
                    .ToDictionary(group => group.Key, group => group.Sum(value => value.PlannedQuantity));
                var workOrderIds = openWorkOrders.Select(value => value.Id).ToArray();
                List<WorkOrderMaterialRequirement> workOrderMaterials = workOrderIds.Length == 0
                    ? []
                    : await db.WorkOrderMaterialRequirements.AsNoTracking()
                        .Where(value => value.CompanyId == companyId
                            && workOrderIds.Contains(value.WorkOrderId))
                        .ToListAsync(cancellationToken);
                var materialRequirementIds = workOrderMaterials.Select(value => value.Id).ToArray();
                var reservedByRequirement = materialRequirementIds.Length == 0
                    ? new Dictionary<Guid, decimal>()
                    : await db.InventoryReservations.AsNoTracking()
                        .Where(value => value.CompanyId == companyId
                            && value.Status == "active"
                            && materialRequirementIds.Contains(value.WorkOrderMaterialRequirementId))
                        .GroupBy(value => value.WorkOrderMaterialRequirementId)
                        .Select(group => new { RequirementId = group.Key, Quantity = group.Sum(value => value.Quantity) })
                        .ToDictionaryAsync(value => value.RequirementId, value => value.Quantity,
                            cancellationToken);
                var workOrdersById = openWorkOrders.ToDictionary(value => value.Id);
                var planningDemands = demands
                    .Select(value => new MrpPlanningDemand(value.NeedByDate, value, null))
                    .ToList();
                planningDemands.AddRange(workOrderMaterials.Select(material =>
                    new MrpPlanningDemand(
                        workOrdersById[material.WorkOrderId].PlannedStartDate,
                        null,
                        new MrpWorkOrderMaterialDemand(
                            workOrdersById[material.WorkOrderId],
                            material,
                            reservedByRequirement.GetValueOrDefault(material.Id)))));
                var requirements = new List<ExplodedMaterialRequirement>();
                var supplyByItem = new Dictionary<Guid, MrpInventorySupply>();
                foreach (var planningDemand in planningDemands
                    .OrderBy(value => value.NeedByDate)
                    .ThenBy(value => value.WorkOrderMaterialDemand is null ? 1 : 0))
                {
                    if (planningDemand.WorkOrderMaterialDemand is { } workOrderDemand)
                    {
                        var material = workOrderDemand.Requirement;
                        var item = await db.Items.AsNoTracking().SingleAsync(value =>
                            value.CompanyId == companyId && value.Id == material.ItemId,
                            cancellationToken);
                        var unit = await db.UnitsOfMeasure.AsNoTracking().SingleAsync(value =>
                            value.CompanyId == companyId && value.Id == item.StockUnitId,
                            cancellationToken);
                        var uncoveredBeforeInventory = Math.Max(
                            0m, material.RequiredQuantity - workOrderDemand.ReservedQuantity);
                        var netQuantity = await ConsumeMrpSupplyAsync(
                            companyId, item.Id, uncoveredBeforeInventory,
                            planningDemand.NeedByDate, request.ThroughDate,
                            supplyByItem, cancellationToken);
                        requirements.Add(new ExplodedMaterialRequirement(
                            item.Id, item.Code, item.Name, unit.Code, item.StockUnitId,
                            material.BomVersionId, material.BomComponentId,
                            material.SourcePath, material.QuantityPerFinishedUnit,
                            material.WastePercent, material.RequiredQuantity, netQuantity,
                            planningDemand.NeedByDate, workOrderDemand.WorkOrder.WorkOrderNumber));
                        continue;
                    }

                    var demand = planningDemand.SalesDemand!;
                    var remainingDemand = Math.Max(0m,
                        demand.OutstandingQuantity - activeWorkOrders.GetValueOrDefault(demand.SalesOrderLineId));
                    if (remainingDemand == 0m)
                    {
                        continue;
                    }

                    var finishedItem = await db.ProductVariants.AsNoTracking()
                        .Where(value => value.CompanyId == companyId && value.Id == demand.ProductVariantId)
                        .Join(db.Items.AsNoTracking(),
                            variant => new { variant.CompanyId, Id = variant.ItemId },
                            item => new { item.CompanyId, item.Id },
                            (_, item) => item)
                        .SingleAsync(cancellationToken);
                    var netFinishedQuantity = await ConsumeMrpSupplyAsync(
                        companyId, finishedItem.Id, remainingDemand, demand.NeedByDate,
                        request.ThroughDate, supplyByItem, cancellationToken);
                    if (netFinishedQuantity == 0m)
                    {
                        continue;
                    }

                    var effectiveBoms = await GetEffectiveBomVersionsAsync(
                        companyId, demand.ProductId, demand.NeedByDate, cancellationToken);
                    if (effectiveBoms.Count != 1)
                    {
                        return Invalid<MrpRunResponse>(
                            "bom", $"No unique approved effective BOM exists for product '{demand.ProductCode}' on {demand.NeedByDate:yyyy-MM-dd}.");
                    }

                    var explosion = await ExplodeBomAsync(
                        companyId, effectiveBoms[0], netFinishedQuantity, demand.NeedByDate,
                        new HashSet<Guid>(), demand.ProductCode, cancellationToken,
                        supplyByItem, throughDate: request.ThroughDate);
                    if (explosion.Error is not null)
                    {
                        return Invalid<MrpRunResponse>("bom", explosion.Error);
                    }

                    requirements.AddRange(explosion.Lines.Select(line =>
                        line with { ContributingOrder = demand.OrderNumber }));
                }

                var rows = await NetMaterialRequirementsAsync(
                    companyId, requirements, request.ThroughDate, cancellationToken);
                var calculatedAt = DateTimeOffset.UtcNow;
                AddEvent(companyId, actor, "mrp-run", Guid.NewGuid(), null,
                    "calculated", new
                    {
                        request.FromDate,
                        request.ThroughDate,
                        materialCount = rows.Count,
                        sourceOrders = demands.Count,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<MrpRunResponse>(
                    new MrpRunResponse(request.FromDate, request.ThroughDate, calculatedAt, rows));
            }, cancellationToken);

    private async Task<List<SalesDemand>> LoadSalesDemandsAsync(
        Guid companyId,
        DateOnly fromDate,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var lines = await (
            from order in db.SalesOrders.AsNoTracking()
            join revision in db.SalesOrderRevisions.AsNoTracking()
                on new { order.CompanyId, orderId = order.Id }
                equals new { revision.CompanyId, orderId = revision.SalesOrderId }
            where order.CompanyId == companyId
                && order.CurrentRevisionNumber == revision.RevisionNumber
                && revision.Status == OrderStatuses.Confirmed
            join line in db.SalesOrderLines.AsNoTracking()
                on new { revision.CompanyId, revisionId = revision.Id }
                equals new { line.CompanyId, revisionId = line.SalesOrderRevisionId }
            where line.RequestedDeliveryDate >= fromDate
                && line.RequestedDeliveryDate <= throughDate
            join product in db.Products.AsNoTracking()
                on new { line.CompanyId, Id = line.ProductId }
                equals new { product.CompanyId, product.Id }
            select new
            {
                OrderId = order.Id,
                order.OrderNumber,
                revision.Id,
                LineId = line.Id,
                line.ProductId,
                ProductCode = product.Code,
                line.ProductVariantId,
                line.Quantity,
                line.RequestedDeliveryDate,
            }).ToListAsync(cancellationToken);
        if (lines.Count == 0)
        {
            return [];
        }

        var lineIds = lines.Select(value => value.LineId).ToArray();
        var fulfilled = await db.SalesOrderFulfillments.AsNoTracking()
            .Where(value => value.CompanyId == companyId && lineIds.Contains(value.SalesOrderLineId))
            .GroupBy(value => value.SalesOrderLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => value.LineId, value => value.Quantity, cancellationToken);
        return lines.Select(value => new SalesDemand(
            value.OrderId,
            value.OrderNumber,
            value.LineId,
            value.ProductId,
            value.ProductCode,
            value.ProductVariantId,
            Math.Max(0m, value.Quantity - fulfilled.GetValueOrDefault(value.LineId)),
            value.RequestedDeliveryDate))
            .Where(value => value.OutstandingQuantity > 0m)
            .ToList();
    }

    private async Task<decimal> ConsumeMrpSupplyAsync(
        Guid companyId,
        Guid itemId,
        decimal requiredQuantity,
        DateOnly needByDate,
        DateOnly throughDate,
        Dictionary<Guid, MrpInventorySupply> supplyByItem,
        CancellationToken cancellationToken)
    {
        if (!supplyByItem.TryGetValue(itemId, out var supply))
        {
            var balances = await (
                from balance in db.InventoryStockBalances.AsNoTracking()
                join warehouse in db.Warehouses.AsNoTracking()
                    on new { balance.CompanyId, Id = balance.WarehouseId }
                    equals new { warehouse.CompanyId, warehouse.Id }
                where balance.CompanyId == companyId && balance.ItemId == itemId
                    && balance.StockStatus == InventoryStockStatuses.Available
                    && warehouse.IsActive
                select balance).ToListAsync(cancellationToken);
            var deliveries = await GetOpenPurchaseReceiptScheduleAsync(
                companyId, itemId, throughDate, cancellationToken);
            supply = new MrpInventorySupply(
                balances.Sum(value => Math.Max(0m, value.OnHandQuantity - value.ReservedQuantity)),
                deliveries);
            supplyByItem.Add(itemId, supply);
        }

        var remaining = requiredQuantity;
        var fromOnHand = Math.Min(remaining, supply.RemainingOnHand);
        supply.RemainingOnHand -= fromOnHand;
        remaining -= fromOnHand;
        foreach (var delivery in supply.Deliveries)
        {
            if (delivery.ExpectedDate > needByDate || remaining <= 0m)
            {
                continue;
            }

            var fromDelivery = Math.Min(remaining, delivery.RemainingQuantity);
            delivery.RemainingQuantity -= fromDelivery;
            remaining -= fromDelivery;
        }

        return remaining;
    }

    private async Task<decimal> GetOpenPurchaseReceiptsAsync(
        Guid companyId,
        Guid itemId,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var deliveries = await GetOpenPurchaseReceiptScheduleAsync(
            companyId, itemId, throughDate, cancellationToken);
        return deliveries.Sum(value => value.RemainingQuantity);
    }

    private async Task<List<MrpPurchaseReceiptSupply>> GetOpenPurchaseReceiptScheduleAsync(
        Guid companyId,
        Guid itemId,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var purchaseLines = await (
            from order in db.PurchaseOrders.AsNoTracking()
            join revision in db.PurchaseOrderRevisions.AsNoTracking()
                on new { order.CompanyId, orderId = order.Id }
                equals new { revision.CompanyId, orderId = revision.PurchaseOrderId }
            where order.CompanyId == companyId
                && order.CurrentRevisionNumber == revision.RevisionNumber
                && (revision.Status == OrderStatuses.Issued
                    || revision.Status == OrderStatuses.PartiallyReceived)
            join line in db.PurchaseOrderLineRevisions.AsNoTracking()
                on new { revision.CompanyId, revisionId = revision.Id }
                equals new { line.CompanyId, revisionId = line.PurchaseOrderRevisionId }
            where line.ItemId == itemId && line.ExpectedDeliveryDate <= throughDate
            select new
            {
                line.PurchaseOrderLineId,
                line.ItemId,
                line.UnitOfMeasureId,
                line.Quantity,
                line.ExpectedDeliveryDate,
            })
            .ToListAsync(cancellationToken);
        if (purchaseLines.Count == 0)
        {
            return [];
        }

        var lineIds = purchaseLines.Select(value => value.PurchaseOrderLineId).ToArray();
        var received = await db.GoodsReceiptLines.AsNoTracking()
            .Where(value => value.CompanyId == companyId && lineIds.Contains(value.PurchaseOrderLineId))
            .GroupBy(value => value.PurchaseOrderLineId)
            .Select(group => new { LineId = group.Key, Quantity = group.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => value.LineId, value => value.Quantity, cancellationToken);
        var supplies = new List<MrpPurchaseReceiptSupply>();
        foreach (var line in purchaseLines)
        {
            var openQuantity = Math.Max(0m, line.Quantity - received.GetValueOrDefault(line.PurchaseOrderLineId));
            if (openQuantity == 0m)
            {
                continue;
            }

            var item = await db.Items.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == line.ItemId, cancellationToken);
            var factor = await GetStockConversionFactorAsync(
                companyId, item, line.UnitOfMeasureId, throughDate, cancellationToken);
            if (factor is null)
            {
                throw new InvalidOperationException(
                    $"Purchase unit for item {item.Code} has no unambiguous conversion to its stock unit.");
            }

            supplies.Add(new MrpPurchaseReceiptSupply(
                line.ExpectedDeliveryDate, decimal.Round(openQuantity * factor.Value, 6)));
        }

        return supplies.OrderBy(value => value.ExpectedDate).ToList();
    }

    private async Task<List<MrpMaterialRequirementResponse>> NetMaterialRequirementsAsync(
        Guid companyId,
        IReadOnlyList<ExplodedMaterialRequirement> requirements,
        DateOnly throughDate,
        CancellationToken cancellationToken)
    {
        var rows = new List<MrpMaterialRequirementResponse>();
        foreach (var group in requirements.GroupBy(value => value.ItemId))
        {
            var item = await db.Items.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == group.Key, cancellationToken);
            var stockUnit = await db.UnitsOfMeasure.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == item.StockUnitId, cancellationToken);
            var purchaseUnit = await db.UnitsOfMeasure.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == item.PurchaseUnitId, cancellationToken);
            var ordered = group.OrderBy(value => value.NeedByDate).ToArray();
            var gross = ordered.Sum(value => value.Quantity);
            var balances = await (
                from balance in db.InventoryStockBalances.AsNoTracking()
                join warehouse in db.Warehouses.AsNoTracking()
                    on new { balance.CompanyId, Id = balance.WarehouseId }
                    equals new { warehouse.CompanyId, warehouse.Id }
                where balance.CompanyId == companyId && balance.ItemId == item.Id
                    && balance.StockStatus == InventoryStockStatuses.Available
                    && warehouse.IsActive
                select new { balance.OnHandQuantity, balance.ReservedQuantity })
                .ToListAsync(cancellationToken);
            var onHand = balances.Sum(value => value.OnHandQuantity);
            var reserved = balances.Sum(value => value.ReservedQuantity);
            var incoming = await GetOpenPurchaseReceiptsAsync(
                companyId, item.Id, throughDate, cancellationToken);
            var net = ordered.Sum(value => value.NetQuantity);
            var suggestionScale = (decimal)Math.Pow(10, purchaseUnit.DecimalPlaces);
            var suggestion = item.PurchaseToStockFactor <= 0m
                ? 0m
                : Math.Ceiling(net / item.PurchaseToStockFactor * suggestionScale)
                    / suggestionScale;
            rows.Add(new MrpMaterialRequirementResponse(
                item.Id, item.Code, item.Name, stockUnit.Code,
                gross, onHand, reserved, incoming, net, suggestion,
                ordered.Min(value => value.NeedByDate),
                ordered.Select(value => value.ContributingOrder)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()));
        }

        return rows.OrderByDescending(value => value.NetRequirement)
            .ThenBy(value => value.ItemCode, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<List<BomVersion>> GetEffectiveBomVersionsAsync(
        Guid companyId,
        Guid productId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var versions = await (
            from bom in db.Boms.AsNoTracking()
            join version in db.BomVersions.AsNoTracking()
                on new { bom.CompanyId, bomId = bom.Id }
                equals new { version.CompanyId, bomId = version.BomId }
            where bom.CompanyId == companyId && bom.ProductId == productId && bom.IsActive
                && version.Status == BomVersionStatuses.Approved
                && version.EffectiveFrom <= asOfDate
                && (version.EffectiveTo == null || version.EffectiveTo >= asOfDate)
            select version).ToListAsync(cancellationToken);
        return versions;
    }

    private async Task<BomExplosionResult> ExplodeBomAsync(
        Guid companyId,
        BomVersion bomVersion,
        decimal parentQuantity,
        DateOnly asOfDate,
        HashSet<Guid> productPath,
        string sourcePath,
        CancellationToken cancellationToken,
        Dictionary<Guid, MrpInventorySupply>? supplyByItem = null,
        decimal quantityPerFinishedUnit = 1m,
        DateOnly? throughDate = null)
    {
        var bom = await db.Boms.AsNoTracking().SingleAsync(value =>
            value.CompanyId == companyId && value.Id == bomVersion.BomId, cancellationToken);
        if (!productPath.Add(bom.ProductId))
        {
            return new BomExplosionResult([], $"A multi-level BOM cycle was found at product '{sourcePath}'.");
        }

        var components = await db.BomComponents.AsNoTracking()
            .Where(value => value.CompanyId == companyId && value.BomVersionId == bomVersion.Id)
            .OrderBy(value => value.LineNumber)
            .ToListAsync(cancellationToken);
        if (components.Count == 0)
        {
            return new BomExplosionResult([], $"Approved BOM version {bomVersion.VersionNumber} has no material lines.");
        }

        var output = new List<ExplodedMaterialRequirement>();
        foreach (var component in components)
        {
            if (component.IsOptional)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(component.AlternativeGroupCode))
            {
                return new BomExplosionResult([],
                    $"BOM alternative group '{component.AlternativeGroupCode}' requires a selected component before planning.");
            }

            var item = await db.Items.AsNoTracking().SingleOrDefaultAsync(value =>
                value.CompanyId == companyId && value.Id == component.ItemId && value.IsActive,
                cancellationToken);
            if (item is null)
            {
                return new BomExplosionResult([], "An approved BOM references an inactive or missing material.");
            }

            var conversion = await GetStockConversionFactorAsync(
                companyId, item, component.UnitOfMeasureId, asOfDate, cancellationToken);
            if (conversion is null)
            {
                return new BomExplosionResult([],
                    $"BOM material '{item.Code}' has no unambiguous effective conversion to its stock unit.");
            }

            var quantityPerUnit = decimal.Round(
                component.Quantity * (1m + component.WastePercent / 100m) * conversion.Value, 10);
            var requiredQuantity = decimal.Round(quantityPerUnit * parentQuantity, 6);
            if (requiredQuantity <= 0m)
            {
                return new BomExplosionResult([], $"BOM material '{item.Code}' rounds below stock precision.");
            }

            var uncoveredQuantity = supplyByItem is null
                ? requiredQuantity
                : await ConsumeMrpSupplyAsync(
                    companyId, item.Id, requiredQuantity, asOfDate,
                    throughDate ?? asOfDate,
                    supplyByItem, cancellationToken);
            var subassembly = await db.ProductVariants.AsNoTracking()
                .Where(value => value.CompanyId == companyId && value.ItemId == item.Id && value.IsActive)
                .Select(value => new { value.ProductId, value.Code })
                .SingleOrDefaultAsync(cancellationToken);
            if (subassembly is not null)
            {
                var childVersions = await GetEffectiveBomVersionsAsync(
                    companyId, subassembly.ProductId, asOfDate, cancellationToken);
                if (childVersions.Count > 1)
                {
                    return new BomExplosionResult([],
                        $"Multiple approved BOM versions are effective for subassembly '{subassembly.Code}'.");
                }

                if (childVersions.Count == 1)
                {
                    if (uncoveredQuantity == 0m)
                    {
                        continue;
                    }

                    var childPath = $"{sourcePath}/{subassembly.Code}";
                    var children = await ExplodeBomAsync(
                        companyId, childVersions[0], uncoveredQuantity, asOfDate,
                        new HashSet<Guid>(productPath), childPath, cancellationToken,
                        supplyByItem, quantityPerFinishedUnit * quantityPerUnit,
                        throughDate);
                    if (children.Error is not null)
                    {
                        return children;
                    }

                    output.AddRange(children.Lines);
                    continue;
                }
            }

            var sourceUnit = await db.UnitsOfMeasure.AsNoTracking().SingleAsync(value =>
                value.CompanyId == companyId && value.Id == item.StockUnitId, cancellationToken);
            output.Add(new ExplodedMaterialRequirement(
                item.Id, item.Code, item.Name, sourceUnit.Code, item.StockUnitId,
                bomVersion.Id, component.Id,
                $"{sourcePath}/BOM-v{bomVersion.VersionNumber}/line-{component.LineNumber}",
                quantityPerFinishedUnit * quantityPerUnit, component.WastePercent,
                requiredQuantity, uncoveredQuantity, asOfDate, string.Empty));
        }

        return new BomExplosionResult(output, null);
    }

    private sealed record SalesDemand(
        Guid SalesOrderId,
        string OrderNumber,
        Guid SalesOrderLineId,
        Guid ProductId,
        string ProductCode,
        Guid ProductVariantId,
        decimal OutstandingQuantity,
        DateOnly NeedByDate);

    private sealed record MrpPlanningDemand(
        DateOnly NeedByDate,
        SalesDemand? SalesDemand,
        MrpWorkOrderMaterialDemand? WorkOrderMaterialDemand);

    private sealed record MrpWorkOrderMaterialDemand(
        WorkOrder WorkOrder,
        WorkOrderMaterialRequirement Requirement,
        decimal ReservedQuantity);

    private sealed record ExplodedMaterialRequirement(
        Guid ItemId,
        string ItemCode,
        string ItemName,
        string UnitCode,
        Guid UnitOfMeasureId,
        Guid BomVersionId,
        Guid BomComponentId,
        string SourcePath,
        decimal QuantityPerFinishedUnit,
        decimal WastePercent,
        decimal Quantity,
        decimal NetQuantity,
        DateOnly NeedByDate,
        string ContributingOrder);

    private sealed class MrpInventorySupply(
        decimal remainingOnHand,
        IReadOnlyList<MrpPurchaseReceiptSupply> deliveries)
    {
        public decimal RemainingOnHand { get; set; } = remainingOnHand;
        public List<MrpPurchaseReceiptSupply> Deliveries { get; } = deliveries.ToList();
    }

    private sealed class MrpPurchaseReceiptSupply(DateOnly expectedDate, decimal remainingQuantity)
    {
        public DateOnly ExpectedDate { get; } = expectedDate;
        public decimal RemainingQuantity { get; set; } = remainingQuantity;
    }

    private sealed record BomExplosionResult(
        IReadOnlyList<ExplodedMaterialRequirement> Lines,
        string? Error);
}
