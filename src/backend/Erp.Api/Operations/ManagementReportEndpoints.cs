using Erp.Api.Security;
using Erp.Domain.Commerce;
using Erp.Domain.Operations;
using Erp.Domain.Products;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Operations;

internal static class ManagementReportEndpoints
{
    public static void MapManagementReportEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/reports/operations", GetOperationsReportAsync)
            .RequireAuthorization(PermissionPolicies.For("reports.read"));
    }

    private static async Task<IResult> GetOperationsReportAsync(
        HttpContext context, ErpDbContext db, CancellationToken cancellationToken)
    {
        var companyId = Guid.Parse(context.User.FindFirst(SecurityClaims.CompanyId)?.Value
            ?? throw new InvalidOperationException("The authenticated company scope is missing."));

        var salesOrders = await (
            from order in db.SalesOrders.AsNoTracking()
            join revision in db.SalesOrderRevisions.AsNoTracking()
                on new { order.CompanyId, SalesOrderId = order.Id, order.CurrentRevisionNumber }
                equals new { revision.CompanyId, revision.SalesOrderId, CurrentRevisionNumber = revision.RevisionNumber }
            where order.CompanyId == companyId
            group revision by revision.Status into statuses
            select new { Status = statuses.Key, Count = statuses.Count() })
            .ToArrayAsync(cancellationToken);
        var purchaseOrders = await (
            from order in db.PurchaseOrders.AsNoTracking()
            join revision in db.PurchaseOrderRevisions.AsNoTracking()
                on new { order.CompanyId, PurchaseOrderId = order.Id, order.CurrentRevisionNumber }
                equals new { revision.CompanyId, revision.PurchaseOrderId, CurrentRevisionNumber = revision.RevisionNumber }
            where order.CompanyId == companyId
            group revision by revision.Status into statuses
            select new { Status = statuses.Key, Count = statuses.Count() })
            .ToArrayAsync(cancellationToken);
        var inventoryBalances = await (
            from balance in db.InventoryStockBalances.AsNoTracking()
            join item in db.Items.AsNoTracking()
                on new { balance.CompanyId, balance.ItemId }
                equals new { item.CompanyId, ItemId = item.Id }
            join warehouse in db.Warehouses.AsNoTracking()
                on new { balance.CompanyId, balance.WarehouseId }
                equals new { warehouse.CompanyId, WarehouseId = warehouse.Id }
            where balance.CompanyId == companyId
            orderby item.Code, warehouse.Code, balance.StockStatus
            select new
            {
                balance.ItemId, item.Code, ItemName = item.Name,
                balance.WarehouseId, WarehouseCode = warehouse.Code,
                balance.StockStatus, balance.OnHandQuantity, balance.ReservedQuantity,
                AvailableQuantity = balance.StockStatus == InventoryStockStatuses.Available
                    ? balance.OnHandQuantity - balance.ReservedQuantity : 0m,
            }).Take(2000).ToArrayAsync(cancellationToken);
        var inventoryLedgerQuery = (
            from entry in db.InventoryLedgerEntries.AsNoTracking()
            join item in db.Items.AsNoTracking()
                on new { entry.CompanyId, entry.ItemId }
                equals new { item.CompanyId, ItemId = item.Id }
            join warehouse in db.Warehouses.AsNoTracking()
                on new { entry.CompanyId, entry.WarehouseId }
                equals new { warehouse.CompanyId, WarehouseId = warehouse.Id }
            where entry.CompanyId == companyId
            select new
            {
                entry.Id, entry.ItemId, ItemCode = item.Code, ItemName = item.Name,
                entry.WarehouseId, WarehouseCode = warehouse.Code, entry.StockStatus,
                entry.TransactionType, entry.Quantity, entry.ReferenceType,
                entry.ReferenceId, entry.ReferenceLineId, entry.ActorSubject, entry.OccurredAt,
            });
        var inventoryLedger = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? (await inventoryLedgerQuery.Take(2000).ToArrayAsync(cancellationToken))
                .OrderByDescending(value => value.OccurredAt).ToArray()
            : await inventoryLedgerQuery.OrderByDescending(value => value.OccurredAt)
                .Take(2000).ToArrayAsync(cancellationToken);

        var requirements = await (
            from requirement in db.WorkOrderMaterialRequirements.AsNoTracking()
            join order in db.WorkOrders.AsNoTracking()
                on new { requirement.CompanyId, requirement.WorkOrderId }
                equals new { order.CompanyId, WorkOrderId = order.Id }
            where requirement.CompanyId == companyId
                && order.Status != WorkOrderStatuses.Completed
                && order.Status != WorkOrderStatuses.Cancelled
                && order.Status != WorkOrderStatuses.Rejected
            select new { requirement.ItemId, requirement.RequiredQuantity })
            .Take(10000).ToArrayAsync(cancellationToken);
        var freeByItem = inventoryBalances.Where(value =>
                value.StockStatus == InventoryStockStatuses.Available)
            .GroupBy(value => value.ItemId)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.AvailableQuantity));
        var materialShortages = requirements.GroupBy(value => value.ItemId).Select(group =>
        {
            var required = group.Sum(value => value.RequiredQuantity);
            var free = freeByItem.GetValueOrDefault(group.Key);
            return new
            {
                ItemId = group.Key,
                RequiredQuantity = required,
                EligibleAvailableQuantity = free,
                ShortageQuantity = Math.Max(0m, required - free),
            };
        }).Where(value => value.ShortageQuantity > 0m).ToArray();

        var workOrders = await db.WorkOrders.AsNoTracking()
            .Where(value => value.CompanyId == companyId)
            .GroupBy(value => value.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToArrayAsync(cancellationToken);
        var orderRows = await db.WorkOrders.AsNoTracking()
            .Where(value => value.CompanyId == companyId
                && value.Status != WorkOrderStatuses.Cancelled
                && value.Status != WorkOrderStatuses.Rejected)
            .OrderBy(value => value.WorkOrderNumber).Take(1000)
            .Select(value => new
            {
                value.Id, value.WorkOrderNumber, value.PlannedQuantity, value.Status,
            }).ToArrayAsync(cancellationToken);
        var productionActual = new List<object>(orderRows.Length);
        foreach (var order in orderRows)
        {
            var finalOperation = await db.WorkOrderOperations.AsNoTracking()
                .Where(value => value.CompanyId == companyId && value.WorkOrderId == order.Id)
                .OrderByDescending(value => value.Sequence)
                .Select(value => new { value.Id, value.Sequence })
                .FirstOrDefaultAsync(cancellationToken);
            decimal actual = 0m;
            if (finalOperation is not null)
            {
                var cards = await db.JobCards.AsNoTracking().Where(value =>
                    value.CompanyId == companyId && value.WorkOrderId == order.Id
                    && value.WorkOrderOperationId == finalOperation.Id)
                    .Select(value => value.Id).ToArrayAsync(cancellationToken);
                actual = await db.ProductionConfirmations.AsNoTracking()
                    .Where(value => value.CompanyId == companyId && cards.Contains(value.JobCardId)
                        && value.Status == ProductionConfirmationStatuses.Approved)
                    .SumAsync(value => (decimal?)(value.GoodQuantity + value.RejectedQuantity
                        + value.ReworkQuantity + value.ScrapQuantity), cancellationToken) ?? 0m;
            }
            productionActual.Add(new
            {
                order.WorkOrderNumber, order.Status, PlannedQuantity = order.PlannedQuantity,
                ActualProcessedQuantity = actual,
                RemainingQuantity = Math.Max(0m, order.PlannedQuantity - actual),
            });
        }

        var qualityPerformance = await db.QualityInspections.AsNoTracking()
            .Where(value => value.CompanyId == companyId)
            .GroupBy(value => new { value.Stage, value.Status })
            .Select(group => new
            {
                group.Key.Stage, group.Key.Status, InspectionCount = group.Count(),
                InspectedQuantity = group.Sum(value => value.InspectedQuantity),
                AcceptedQuantity = group.Sum(value => value.AcceptedQuantity),
                RejectedQuantity = group.Sum(value => value.RejectedQuantity),
                ReworkQuantity = group.Sum(value => value.ReworkQuantity),
                HoldQuantity = group.Sum(value => value.HoldQuantity),
            }).ToArrayAsync(cancellationToken);

        var downtimeQuery = db.MachineDowntimes.AsNoTracking()
            .Where(value => value.CompanyId == companyId);
        var downtimeRecords = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? (await downtimeQuery.Take(2000).ToListAsync(cancellationToken))
                .OrderByDescending(value => value.StartedAt).ToList()
            : await downtimeQuery.OrderByDescending(value => value.StartedAt).Take(2000)
                .ToListAsync(cancellationToken);
        var machineNames = await db.Machines.AsNoTracking()
            .Where(value => value.CompanyId == companyId)
            .ToDictionaryAsync(value => value.Id, value => value.Code, cancellationToken);
        var machineDowntime = downtimeRecords.Select(value => new
        {
            value.Id, value.MachineId, MachineCode = machineNames.GetValueOrDefault(value.MachineId, "unknown"),
            value.WorkOrderId, value.Reason, value.StartedAt, value.EndedAt,
            DowntimeHours = Math.Max(0d, ((value.EndedAt ?? DateTimeOffset.UtcNow)
                - value.StartedAt).TotalHours),
        }).ToArray();

        var finishedGoods = await (
            from balance in db.InventoryStockBalances.AsNoTracking()
            join item in db.Items.AsNoTracking()
                on new { balance.CompanyId, balance.ItemId }
                equals new { item.CompanyId, ItemId = item.Id }
            join category in db.ItemCategories.AsNoTracking()
                on new { item.CompanyId, item.CategoryId }
                equals new { category.CompanyId, CategoryId = category.Id }
            join variant in db.ProductVariants.AsNoTracking()
                on new { balance.CompanyId, ItemId = balance.ItemId }
                equals new { variant.CompanyId, variant.ItemId }
                into matchingVariants
            from variant in matchingVariants.DefaultIfEmpty()
            where balance.CompanyId == companyId
                && category.CategoryType == "finished_good"
                && balance.StockStatus == InventoryStockStatuses.Available
            orderby item.Code, variant.SizeCode, variant.ColorCode
            select new
            {
                balance.ItemId, ItemCode = item.Code, ItemName = item.Name,
                VariantId = variant == null ? (Guid?)null : variant.Id,
                VariantCode = variant == null ? null : variant.Code,
                SizeCode = variant == null ? null : variant.SizeCode,
                ColorCode = variant == null ? null : variant.ColorCode,
                ColorName = variant == null ? null : variant.ColorName,
                balance.WarehouseId, balance.OnHandQuantity, balance.ReservedQuantity,
                AvailableQuantity = balance.OnHandQuantity - balance.ReservedQuantity,
            }).Take(2000).ToArrayAsync(cancellationToken);

        var dispatchStatus = await db.DispatchShipments.AsNoTracking()
            .Where(value => value.CompanyId == companyId)
            .GroupBy(value => value.Status)
            .Select(group => new { Status = group.Key, ShipmentCount = group.Count() })
            .ToArrayAsync(cancellationToken);
        var dispatchedQuantity = await db.SalesOrderFulfillments.AsNoTracking()
            .Where(value => value.CompanyId == companyId)
            .SumAsync(value => (decimal?)value.Quantity, cancellationToken) ?? 0m;

        return Results.Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            salesOrderStatus = salesOrders,
            purchaseOrderStatus = purchaseOrders,
            inventoryBalances,
            inventoryLedger,
            materialShortages,
            workOrderStatus = workOrders,
            productionPlanVsActual = productionActual,
            qualityPerformance,
            machineDowntime,
            finishedGoodsStock = finishedGoods,
            dispatchPerformance = new
            {
                ShipmentStatuses = dispatchStatus,
                DispatchedQuantity = dispatchedQuantity,
            },
        });
    }
}
