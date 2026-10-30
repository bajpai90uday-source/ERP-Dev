using Erp.Api.MasterData;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Commerce;

internal sealed partial class SalesPurchaseService : IQualityJobWorkMaintenanceService
{
    public async Task<IReadOnlyList<QualityInspectionResponse>> ListInspectionsAsync(
        Guid companyId, string? stage, CancellationToken cancellationToken)
    {
        var query = db.QualityInspections.AsNoTracking().Where(value => value.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(stage))
        {
            query = query.Where(value => value.Stage == stage);
        }

        var inspectionQuery = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? query.Take(250)
            : query.OrderByDescending(value => value.CreatedAt).Take(250);
        var inspections = (await inspectionQuery.ToListAsync(cancellationToken))
            .OrderByDescending(value => value.CreatedAt).ToList();
        var responses = new List<QualityInspectionResponse>(inspections.Count);
        foreach (var inspection in inspections)
        {
            responses.Add(await MapInspectionAsync(companyId, inspection, cancellationToken));
        }
        return responses;
    }

    public Task<MasterDataResult<QualityInspectionResponse>> CreateInspectionAsync(
        Guid companyId, MasterDataActor actor, string key, QualityInspectionRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "quality.inspection.create",
            request, async () =>
            {
                var errors = Validate(request);
                var stage = request.Stage?.Trim().ToLowerInvariant() ?? string.Empty;
                var sourceType = request.SourceType?.Trim().ToLowerInvariant() ?? string.Empty;
                var defects = request.Defects ?? [];
                if (request.AcceptedQuantity + request.RejectedQuantity
                    + request.ReworkQuantity + request.HoldQuantity != request.InspectedQuantity)
                {
                    errors["quantities"] = ["Accepted, rejected, rework, and hold quantities must total the inspected quantity."];
                }
                if (defects.Any(value => value.Quantity <= 0m
                    || value.DefectCodeId == Guid.Empty
                    || value.Notes is { Length: > 500 })
                    || defects.Select(value => value.DefectCodeId).Distinct().Count()
                        != defects.Count
                    || defects.Any(value => value.Quantity > request.RejectedQuantity
                        + request.ReworkQuantity))
                {
                    errors["defects"] = ["Defect rows must use unique active codes, have positive quantities, and not exceed rejected plus rework quantity."];
                }

                if (stage is not ("incoming" or "in_process" or "final" or "jobwork"))
                {
                    errors["stage"] = ["Stage must be incoming, in_process, final, or jobwork."];
                }

                var existing = await db.QualityInspections.AnyAsync(value =>
                    value.CompanyId == companyId && value.SourceType == sourceType
                    && value.SourceId == request.SourceId && value.SourceLineId == request.SourceLineId
                    && value.Stage == stage && value.Status != QualityStatuses.Rejected,
                    cancellationToken);
                if (existing)
                {
                    return Conflict<QualityInspectionResponse>(
                        "An active inspection already exists for this source and stage.");
                }

                if (stage == "incoming" && sourceType == "purchase_receipt_line")
                {
                    var source = await (
                        from receipt in db.GoodsReceipts
                        join receiptLine in db.GoodsReceiptLines
                            on new { receipt.CompanyId, ReceiptId = receipt.Id }
                            equals new { receiptLine.CompanyId, ReceiptId = receiptLine.GoodsReceiptId }
                        join orderLine in db.PurchaseOrderLineRevisions
                            on new { receipt.CompanyId, receipt.PurchaseOrderRevisionId, receiptLine.PurchaseOrderLineId }
                            equals new { orderLine.CompanyId, orderLine.PurchaseOrderRevisionId, orderLine.PurchaseOrderLineId }
                        where receipt.CompanyId == companyId && receipt.Id == request.SourceId
                            && receiptLine.Id == request.SourceLineId
                        select new { receipt.WarehouseId, receiptLine.Quantity, orderLine.ItemId })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (source is null || source.WarehouseId != request.WarehouseId
                        || source.ItemId != request.ItemId || source.Quantity != request.InspectedQuantity)
                    {
                        errors["sourceId"] = ["Inspection source must match a received purchase-order line, item, warehouse, and quantity."];
                    }
                }
                else if (stage is "in_process" or "final" && sourceType == "job_card")
                {
                    var source = await (
                        from card in db.JobCards
                        join workOrder in db.WorkOrders
                            on new { card.CompanyId, Id = card.WorkOrderId }
                            equals new { workOrder.CompanyId, workOrder.Id }
                        join salesLine in db.SalesOrderLines
                            on new { workOrder.CompanyId, Id = workOrder.SalesOrderLineId }
                            equals new { salesLine.CompanyId, salesLine.Id }
                        join variant in db.ProductVariants
                            on new { salesLine.CompanyId, Id = salesLine.ProductVariantId }
                            equals new { variant.CompanyId, variant.Id }
                        join operation in db.WorkOrderOperations
                            on new { card.CompanyId, Id = card.WorkOrderOperationId }
                            equals new { operation.CompanyId, operation.Id }
                        where card.CompanyId == companyId && card.Id == request.SourceId
                            && request.SourceLineId == card.Id
                        select new
                        {
                            card.Id,
                            card.WorkOrderId,
                            workOrder.OutputWarehouseId,
                            variant.ItemId,
                            operation.Sequence,
                        }).SingleOrDefaultAsync(cancellationToken);
                    var goodQuantity = source is null ? 0m : await db.ProductionConfirmations
                        .Where(value => value.CompanyId == companyId && value.JobCardId == source.Id
                            && value.Status == ProductionConfirmationStatuses.Approved)
                        .SumAsync(value => value.GoodQuantity, cancellationToken);
                    var lastSequence = source is null ? 0 : await db.WorkOrderOperations
                        .Where(value => value.CompanyId == companyId
                            && value.WorkOrderId == source.WorkOrderId)
                        .MaxAsync(value => (int?)value.Sequence, cancellationToken) ?? 0;
                    if (source is null || source.ItemId != request.ItemId
                        || source.OutputWarehouseId != request.WarehouseId
                        || source.Sequence == lastSequence != (stage == "final")
                        || request.InspectedQuantity != goodQuantity)
                    {
                        errors["sourceId"] = ["Inspection must match approved good output for the Job Card and correct operation stage."];
                    }
                }
                else if (stage == "jobwork" && sourceType == "jobwork_receipt")
                {
                    var source = await (
                        from receipt in db.JobWorkReceipts
                        join order in db.JobWorkOrders
                            on new { receipt.CompanyId, Id = receipt.JobWorkOrderId }
                            equals new { order.CompanyId, order.Id }
                        where receipt.CompanyId == companyId && receipt.Id == request.SourceId
                            && receipt.Id == request.SourceLineId
                        select new
                        {
                            order.OutputItemId,
                            order.OutputWarehouseId,
                            Total = receipt.ProcessedQuantity + receipt.RejectedQuantity,
                        }).SingleOrDefaultAsync(cancellationToken);
                    if (source is null || source.OutputItemId != request.ItemId
                        || source.OutputWarehouseId != request.WarehouseId
                        || source.Total != request.InspectedQuantity)
                    {
                        errors["sourceId"] = ["Inspection must match the Job Work receipt output, item, warehouse, and quantity."];
                    }
                }
                else
                {
                    errors["sourceType"] = ["The source type is not valid for the selected inspection stage."];
                }

                foreach (var defect in defects)
                {
                    if (!await db.QualityDefectCodes.AnyAsync(value =>
                        value.CompanyId == companyId && value.Id == defect.DefectCodeId
                        && value.IsActive, cancellationToken))
                    {
                        errors["defects"] = ["Defect code was not found or is inactive for this company."];
                        break;
                    }
                }

                if (errors.Count > 0)
                {
                    return Invalid<QualityInspectionResponse>(errors);
                }

                var inspection = new QualityInspection
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    InspectionNumber = $"QI-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..23],
                    Stage = stage,
                    SourceType = sourceType,
                    SourceId = request.SourceId,
                    SourceLineId = request.SourceLineId,
                    ItemId = request.ItemId,
                    WarehouseId = request.WarehouseId,
                    JobCardId = request.JobCardId,
                    InspectedQuantity = request.InspectedQuantity,
                    AcceptedQuantity = request.AcceptedQuantity,
                    RejectedQuantity = request.RejectedQuantity,
                    ReworkQuantity = request.ReworkQuantity,
                    HoldQuantity = request.HoldQuantity,
                    CreatedBySubject = actor.Subject,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
                };
                if (stage is "in_process" or "final" && inspection.JobCardId != request.SourceId)
                {
                    return Invalid<QualityInspectionResponse>(
                        "jobCardId", "The production inspection must reference the inspected Job Card.");
                }

                db.QualityInspections.Add(inspection);
                foreach (var defect in defects)
                {
                    db.QualityInspectionDefects.Add(new QualityInspectionDefect
                    {
                        Id = Guid.NewGuid(), CompanyId = companyId,
                        InspectionId = inspection.Id, DefectCodeId = defect.DefectCodeId,
                        Quantity = defect.Quantity, Notes = defect.Notes?.Trim(),
                    });
                }
                AddEvent(companyId, actor, "quality-inspection", inspection.Id, null,
                    "inspection_created", new
                    {
                        inspection.Stage, inspection.SourceType, inspection.SourceId,
                        inspection.SourceLineId, inspection.InspectedQuantity,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<QualityInspectionResponse>(
                    await MapInspectionAsync(companyId, inspection, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<QualityInspectionResponse>> ApproveInspectionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "quality.inspection.approve",
            new { id }, async () =>
            {
                var inspection = await db.QualityInspections.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (inspection is null)
                {
                    return NotFound<QualityInspectionResponse>();
                }

                if (inspection.Status != QualityStatuses.PendingApproval)
                {
                    return Conflict<QualityInspectionResponse>("Only a pending inspection can be approved.");
                }

                if (inspection.CreatedBySubject == actor.Subject)
                {
                    return Conflict<QualityInspectionResponse>("The inspector cannot approve their own inspection.");
                }

                if (inspection.Stage is "incoming" or "jobwork" or "final")
                {
                    var availableQuantity = inspection.AcceptedQuantity;
                    if (availableQuantity > 0m)
                    {
                        var quarantined = await db.InventoryStockBalances.SingleOrDefaultAsync(value =>
                            value.CompanyId == companyId && value.ItemId == inspection.ItemId
                            && value.WarehouseId == inspection.WarehouseId
                            && value.StockStatus == InventoryStockStatuses.Quarantine,
                            cancellationToken);
                        if (quarantined is null || quarantined.OnHandQuantity < availableQuantity)
                        {
                            return Conflict<QualityInspectionResponse>(
                                "Insufficient quarantined inventory remains for this quality disposition.");
                        }

                        await MoveStockAsync(companyId, actor, inspection.ItemId, inspection.WarehouseId,
                            InventoryStockStatuses.Quarantine, InventoryStockStatuses.Available,
                            availableQuantity, "quality-inspection", inspection.Id, inspection.SourceLineId,
                            cancellationToken);
                    }
                }

                inspection.Status = QualityStatuses.Approved;
                inspection.ConcurrencyVersion++;
                inspection.ApprovedBySubject = actor.Subject;
                inspection.ApprovedAt = DateTimeOffset.UtcNow;
                AddEvent(companyId, actor, "quality-inspection", inspection.Id, null,
                    "inspection_approved",
                    new
                    {
                        inspection.AcceptedQuantity, inspection.RejectedQuantity,
                        inspection.ReworkQuantity, inspection.HoldQuantity,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<QualityInspectionResponse>(
                    await MapInspectionAsync(companyId, inspection, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<bool>> CreateDefectCodeAsync(
        Guid companyId, MasterDataActor actor, string key, DefectCodeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "quality.defect-code.create",
            request, async () =>
            {
                var errors = Validate(request);
                if (errors.Count > 0)
                {
                    return Invalid<bool>(errors);
                }

                var code = request.Code.Trim().ToUpperInvariant();
                if (await db.QualityDefectCodes.AnyAsync(value =>
                    value.CompanyId == companyId && value.Code == code, cancellationToken))
                {
                    return Conflict<bool>("A defect code with this code already exists.");
                }

                db.QualityDefectCodes.Add(new QualityDefectCode
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Code = code,
                    Description = request.Description.Trim(),
                });
                AddEvent(companyId, actor, "quality-defect-code", Guid.NewGuid(), null,
                    "defect_code_created", new { Code = code });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<bool>(true, StatusCodes.Status201Created);
            }, cancellationToken);

    public async Task<IReadOnlyList<DefectCodeResponse>> ListDefectCodesAsync(
        Guid companyId, CancellationToken cancellationToken) =>
        await db.QualityDefectCodes.AsNoTracking().Where(value =>
                value.CompanyId == companyId && value.IsActive)
            .OrderBy(value => value.Code)
            .Select(value => new DefectCodeResponse(
                value.Id, value.Code, value.Description, value.IsActive))
            .ToArrayAsync(cancellationToken);

    public Task<MasterDataResult<Guid>> RegisterSubcontractorAsync(
        Guid companyId, MasterDataActor actor, string key, SubcontractorRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "jobwork.subcontractor.register",
            request, async () =>
            {
                if (string.IsNullOrWhiteSpace(request.RegistrationCode)
                    || request.RegistrationCode.Length > 80)
                {
                    return Invalid<Guid>("registrationCode", "A registration code of 1-80 characters is required.");
                }

                if (!await db.Suppliers.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.SupplierId && value.IsActive,
                    cancellationToken))
                {
                    return Invalid<Guid>("supplierId", "Select an active supplier belonging to this company.");
                }

                if (await db.SubcontractorProfiles.AnyAsync(value =>
                    value.CompanyId == companyId && value.SupplierId == request.SupplierId,
                    cancellationToken))
                {
                    return Conflict<Guid>("This supplier is already registered as a subcontractor.");
                }

                var profile = new SubcontractorProfile
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, SupplierId = request.SupplierId,
                    RegistrationCode = request.RegistrationCode.Trim(),
                };
                db.SubcontractorProfiles.Add(profile);
                AddEvent(companyId, actor, "subcontractor", profile.Id, null,
                    "subcontractor_registered", new { profile.SupplierId, profile.RegistrationCode });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<Guid>(profile.Id, StatusCodes.Status201Created);
            }, cancellationToken);

    public Task<MasterDataResult<JobWorkOrderResponse>> CreateJobWorkOrderAsync(
        Guid companyId, MasterDataActor actor, string key, JobWorkOrderRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "jobwork.order.create",
            request, async () =>
            {
                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
                if (request.PlannedQuantity <= 0m || request.UnitCharge < 0m)
                {
                    errors["plannedQuantity"] = ["Planned quantity must be positive and unit charge cannot be negative."];
                }
                if (request.ExpectedReturnDate < DateOnly.FromDateTime(DateTime.UtcNow))
                {
                    errors["expectedReturnDate"] = ["Expected return date cannot be in the past."];
                }
                if (string.IsNullOrWhiteSpace(request.CurrencyCode)
                    || request.CurrencyCode.Length != 3 || !request.CurrencyCode.All(char.IsAsciiLetter))
                {
                    errors["currencyCode"] = ["Provide a three-letter currency code."];
                }
                var requestedMaterials = request.Materials ?? [];
                if (requestedMaterials.Count == 0)
                {
                    errors["materials"] = ["At least one material must be specified for subcontractor custody."];
                }
                if (!await db.SubcontractorProfiles.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.SubcontractorId
                    && value.IsActive, cancellationToken))
                {
                    errors["subcontractorId"] = ["Select an active company subcontractor profile."];
                }
                if (!await db.Items.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.OutputItemId && value.IsActive,
                    cancellationToken)
                    || !await db.Warehouses.AnyAsync(value =>
                        value.CompanyId == companyId && value.Id == request.OutputWarehouseId && value.IsActive,
                        cancellationToken))
                {
                    errors["outputItemId"] = ["Select an active output item and warehouse belonging to this company."];
                }
                if (request.WorkOrderOperationId.HasValue && !await db.WorkOrderOperations.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.WorkOrderOperationId
                    && db.WorkOrders.Any(order => order.CompanyId == companyId
                        && order.Id == value.WorkOrderId && order.Status == WorkOrderStatuses.Released),
                    cancellationToken))
                {
                    errors["workOrderOperationId"] = ["A linked operation must belong to a released Work Order."];
                }
                foreach (var material in requestedMaterials)
                {
                    if (material.Quantity <= 0m
                        || !await db.Items.AnyAsync(value =>
                            value.CompanyId == companyId && value.Id == material.ItemId && value.IsActive,
                            cancellationToken)
                        || !await db.Warehouses.AnyAsync(value =>
                            value.CompanyId == companyId && value.Id == material.WarehouseId && value.IsActive,
                            cancellationToken))
                    {
                        errors["materials"] = ["Each material must have an active company item and warehouse, and a positive quantity."];
                        break;
                    }
                }
                if (errors.Count > 0)
                {
                    return Invalid<JobWorkOrderResponse>(errors);
                }

                var order = new JobWorkOrder
                {
                    Id = Guid.NewGuid(), CompanyId = companyId,
                    OrderNumber = $"JWO-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..24],
                    SubcontractorId = request.SubcontractorId,
                    WorkOrderOperationId = request.WorkOrderOperationId,
                    OutputItemId = request.OutputItemId,
                    OutputWarehouseId = request.OutputWarehouseId,
                    PlannedQuantity = request.PlannedQuantity,
                    UnitCharge = request.UnitCharge,
                    CurrencyCode = request.CurrencyCode.ToUpperInvariant(),
                    ExpectedReturnDate = request.ExpectedReturnDate,
                    CreatedBySubject = actor.Subject,
                };
                db.JobWorkOrders.Add(order);
                foreach (var material in requestedMaterials)
                {
                    db.JobWorkMaterialIssues.Add(new JobWorkMaterialIssue
                    {
                        Id = Guid.NewGuid(), CompanyId = companyId, JobWorkOrderId = order.Id,
                        ItemId = material.ItemId, WarehouseId = material.WarehouseId,
                    });
                }
                AddEvent(companyId, actor, "job-work-order", order.Id, null,
                    "job_work_order_created", new
                    {
                        order.OrderNumber, order.SubcontractorId, order.WorkOrderOperationId,
                        order.PlannedQuantity, materials = requestedMaterials,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<JobWorkOrderResponse>(
                    await MapJobWorkOrderAsync(companyId, order, cancellationToken),
                    StatusCodes.Status201Created);
            }, cancellationToken);

    public Task<MasterDataResult<JobWorkOrderResponse>> DispatchJobWorkAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key,
        IReadOnlyList<JobWorkDispatchRequest> requests, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "jobwork.dispatch",
            new { id, requests }, async () =>
            {
                if (requests.Count == 0 || requests.Any(value => value.Quantity <= 0m))
                {
                    return Invalid<JobWorkOrderResponse>("materials", "Dispatch at least one positive material quantity.");
                }

                var order = await db.JobWorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (order is null)
                {
                    return NotFound<JobWorkOrderResponse>();
                }
                if (order.Status is "completed" or "cancelled")
                {
                    return Conflict<JobWorkOrderResponse>("Closed Job Work Orders cannot be dispatched.");
                }

                foreach (var input in requests)
                {
                    var issue = await db.JobWorkMaterialIssues.SingleOrDefaultAsync(value =>
                        value.CompanyId == companyId && value.JobWorkOrderId == id
                        && value.Id == input.MaterialIssueId, cancellationToken);
                    if (issue is null)
                    {
                        return Invalid<JobWorkOrderResponse>("materialIssueId", "Material issue does not belong to this order.");
                    }

                    var balance = await GetOrCreateStockBalanceAsync(companyId, issue.ItemId,
                        issue.WarehouseId, InventoryStockStatuses.Available, cancellationToken);
                    var outstanding = await GetCustodyOutstandingAsync(companyId, issue.Id, cancellationToken);
                    if (balance.OnHandQuantity - balance.ReservedQuantity < input.Quantity)
                    {
                        return Conflict<JobWorkOrderResponse>("Insufficient unreserved available stock for dispatch.");
                    }

                    balance.OnHandQuantity -= input.Quantity;
                    balance.ConcurrencyVersion++;
                    issue.IssuedQuantity += input.Quantity;
                    issue.ConcurrencyVersion++;
                    var custodyId = Guid.NewGuid();
                    AddInventoryLedger(companyId, actor, issue.ItemId, issue.WarehouseId,
                        InventoryStockStatuses.Available, "issue", -input.Quantity,
                        "job-work-dispatch", order.Id, custodyId);
                    db.SubcontractorCustodyTransactions.Add(new SubcontractorCustodyTransaction
                    {
                        Id = custodyId, CompanyId = companyId, JobWorkOrderId = id,
                        ItemId = issue.ItemId, JobWorkMaterialIssueId = issue.Id,
                        TransactionType = "dispatch", Quantity = input.Quantity,
                        ActorSubject = actor.Subject,
                    });
                }
                order.Status = "dispatched";
                order.ConcurrencyVersion++;
                AddEvent(companyId, actor, "job-work-order", order.Id, null,
                    "materials_dispatched", new { requests });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<JobWorkOrderResponse>(
                    await MapJobWorkOrderAsync(companyId, order, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<JobWorkOrderResponse>> ReconcileJobWorkMaterialAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key,
        JobWorkMaterialReturnRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "jobwork.material.reconcile",
            new { id, request }, async () =>
            {
                if (request.Quantity <= 0m)
                {
                    return Invalid<JobWorkOrderResponse>("quantity", "Returned quantity must be positive.");
                }

                var issue = await db.JobWorkMaterialIssues.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.JobWorkOrderId == id
                    && value.Id == request.MaterialIssueId, cancellationToken);
                if (issue is null)
                {
                    return NotFound<JobWorkOrderResponse>();
                }
                var availableToReturn = await GetCustodyOutstandingAsync(companyId, issue.Id, cancellationToken);
                if (request.Quantity > availableToReturn)
                {
                    return Conflict<JobWorkOrderResponse>("Returned quantity exceeds material currently held by the subcontractor.");
                }

                var order = await db.JobWorkOrders.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                issue.ReturnedQuantity += request.Quantity;
                issue.ConcurrencyVersion++;
                db.SubcontractorCustodyTransactions.Add(new SubcontractorCustodyTransaction
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, JobWorkOrderId = id,
                    ItemId = issue.ItemId, JobWorkMaterialIssueId = issue.Id,
                    TransactionType = "return", Quantity = request.Quantity,
                    ActorSubject = actor.Subject,
                });
                var quarantine = await GetOrCreateStockBalanceAsync(companyId, issue.ItemId,
                    issue.WarehouseId, InventoryStockStatuses.Quarantine, cancellationToken);
                quarantine.OnHandQuantity += request.Quantity;
                quarantine.ConcurrencyVersion++;
                AddInventoryLedger(companyId, actor, issue.ItemId, issue.WarehouseId,
                    InventoryStockStatuses.Quarantine, "receipt", request.Quantity,
                    "job-work-material-return", order.Id, Guid.NewGuid());
                if (order.ProcessedQuantity + order.RejectedQuantity >= order.PlannedQuantity)
                {
                    var issues = await db.JobWorkMaterialIssues.AsNoTracking().Where(value =>
                        value.CompanyId == companyId && value.JobWorkOrderId == id)
                        .Select(value => value.Id).ToListAsync(cancellationToken);
                    var outstanding = 0m;
                    foreach (var otherIssueId in issues)
                    {
                        outstanding += otherIssueId == issue.Id
                            ? availableToReturn - request.Quantity
                            : await GetCustodyOutstandingAsync(companyId, otherIssueId, cancellationToken);
                    }
                    if (outstanding == 0m)
                    {
                        order.Status = "completed";
                    }
                }
                order.ConcurrencyVersion++;
                AddEvent(companyId, actor, "job-work-order", order.Id, null,
                    "materials_reconciled", new { issue.Id, request.Quantity });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<JobWorkOrderResponse>(
                    await MapJobWorkOrderAsync(companyId, order, cancellationToken));
            }, cancellationToken);

    public Task<MasterDataResult<JobWorkOrderResponse>> ReceiveJobWorkAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key,
        JobWorkReceiptRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "jobwork.receive",
            new { id, request }, async () =>
            {
                if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Length > 80
                    || request.ProcessedQuantity < 0m || request.RejectedQuantity < 0m
                    || request.ProcessedQuantity + request.RejectedQuantity <= 0m)
                {
                    return Invalid<JobWorkOrderResponse>("receipt", "Provide a reference and positive processed or rejected quantities.");
                }
                var order = await db.JobWorkOrders.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (order is null)
                {
                    return NotFound<JobWorkOrderResponse>();
                }
                if (order.Status is not ("dispatched" or "partially_received"))
                {
                    return Conflict<JobWorkOrderResponse>("Job Work receipt requires a dispatched order.");
                }
                var returnedSoFar = await db.JobWorkReceipts.Where(value =>
                    value.CompanyId == companyId && value.JobWorkOrderId == id)
                    .SumAsync(value => value.ProcessedQuantity + value.RejectedQuantity, cancellationToken);
                var incoming = request.ProcessedQuantity + request.RejectedQuantity;
                if (returnedSoFar + incoming > order.PlannedQuantity)
                {
                    return Conflict<JobWorkOrderResponse>("Received quantity exceeds the Job Work Order plan.");
                }

                var receipt = new JobWorkReceipt
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, JobWorkOrderId = id,
                    ProcessedQuantity = request.ProcessedQuantity,
                    RejectedQuantity = request.RejectedQuantity,
                    ChargeAmount = request.ProcessedQuantity * order.UnitCharge,
                    Reference = request.Reference.Trim(), CreatedBySubject = actor.Subject,
                };
                db.JobWorkReceipts.Add(receipt);
                order.ProcessedQuantity += request.ProcessedQuantity;
                order.RejectedQuantity += request.RejectedQuantity;
                var quarantine = await GetOrCreateStockBalanceAsync(companyId, order.OutputItemId,
                    order.OutputWarehouseId, InventoryStockStatuses.Quarantine, cancellationToken);
                quarantine.OnHandQuantity += incoming;
                quarantine.ConcurrencyVersion++;
                AddInventoryLedger(companyId, actor, order.OutputItemId, order.OutputWarehouseId,
                    InventoryStockStatuses.Quarantine, "receipt", incoming,
                    "job-work-receipt", receipt.Id, receipt.Id);
                var custody = await db.JobWorkMaterialIssues.AnyAsync(value =>
                    value.CompanyId == companyId && value.JobWorkOrderId == id
                    && value.IssuedQuantity > value.ReturnedQuantity, cancellationToken);
                order.Status = returnedSoFar + incoming == order.PlannedQuantity && !custody
                    ? "completed"
                    : "partially_received";
                order.ConcurrencyVersion++;
                AddEvent(companyId, actor, "job-work-order", order.Id, null,
                    "job_work_received", new
                    {
                        receipt.Id, receipt.Reference, receipt.ProcessedQuantity,
                        receipt.RejectedQuantity, receipt.ChargeAmount,
                    });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<JobWorkOrderResponse>(
                    await MapJobWorkOrderAsync(companyId, order, cancellationToken));
            }, cancellationToken);

    public async Task<IReadOnlyList<JobWorkOrderResponse>> ListJobWorkOrdersAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var orderQuery = db.JobWorkOrders.AsNoTracking().Where(value =>
            value.CompanyId == companyId);
        var orders = (await (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
                ? orderQuery.Take(250)
                : orderQuery.OrderByDescending(value => value.CreatedAt).Take(250))
            .ToListAsync(cancellationToken)).OrderByDescending(value => value.CreatedAt).ToList();
        var result = new List<JobWorkOrderResponse>(orders.Count);
        foreach (var order in orders)
        {
            result.Add(await MapJobWorkOrderAsync(companyId, order, cancellationToken));
        }
        return result;
    }

    public Task<MasterDataResult<MaintenanceRequestResponse>> CreateMaintenanceRequestAsync(
        Guid companyId, MasterDataActor actor, string key, MaintenanceRequestRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "maintenance.request.create",
            request, async () =>
            {
                if (string.IsNullOrWhiteSpace(request.RequestNumber)
                    || string.IsNullOrWhiteSpace(request.Description)
                    || string.IsNullOrWhiteSpace(request.AssignedTeam)
                    || request.RequestNumber.Length > 40 || request.Description.Length > 1000
                    || request.AssignedTeam.Length > 160)
                {
                    return Invalid<MaintenanceRequestResponse>("request", "Request number, description, and assigned team are required and must be within length limits.");
                }
                if (request.Priority is not ("low" or "normal" or "high" or "critical"))
                {
                    return Invalid<MaintenanceRequestResponse>("priority", "Priority must be low, normal, high, or critical.");
                }
                if (!await db.Machines.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.MachineId && value.IsActive,
                    cancellationToken))
                {
                    return Invalid<MaintenanceRequestResponse>("machineId", "Select an active machine from this company.");
                }
                if (request.WorkOrderId.HasValue && !await db.WorkOrders.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.WorkOrderId, cancellationToken))
                {
                    return Invalid<MaintenanceRequestResponse>("workOrderId", "Linked Work Order was not found.");
                }
                if (await db.MaintenanceRequests.AnyAsync(value =>
                    value.CompanyId == companyId && value.RequestNumber == request.RequestNumber.Trim(),
                    cancellationToken))
                {
                    return Conflict<MaintenanceRequestResponse>("A maintenance request with this number already exists.");
                }
                var maintenance = new MaintenanceRequest
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, MachineId = request.MachineId,
                    RequestNumber = request.RequestNumber.Trim(),
                    Description = request.Description.Trim(), Priority = request.Priority,
                    CreatedBySubject = actor.Subject,
                };
                var card = new MaintenanceJobCard
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, MaintenanceRequestId = maintenance.Id,
                    CardNumber = $"MJC-{maintenance.Id:N}"[..36],
                    AssignedTeam = request.AssignedTeam.Trim(),
                };
                db.MaintenanceRequests.Add(maintenance);
                db.MaintenanceJobCards.Add(card);
                if (request.WorkOrderId is { } workOrderId)
                {
                    db.MachineDowntimes.Add(new MachineDowntime
                    {
                        Id = Guid.NewGuid(), CompanyId = companyId, MachineId = request.MachineId,
                        WorkOrderId = workOrderId, MaintenanceJobCardId = card.Id,
                        Reason = maintenance.Description, StartedAt = DateTimeOffset.UtcNow,
                        RecordedBySubject = actor.Subject,
                    });
                }
                AddEvent(companyId, actor, "maintenance-request", maintenance.Id, null,
                    "maintenance_request_created", new { request.MachineId, request.Priority, card.Id });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<MaintenanceRequestResponse>(
                    MapMaintenanceRequest(maintenance, card.Id), StatusCodes.Status201Created);
            }, cancellationToken);

    public async Task<IReadOnlyList<MaintenanceRequestResponse>> ListMaintenanceRequestsAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var query = (
            from request in db.MaintenanceRequests.AsNoTracking()
            join card in db.MaintenanceJobCards.AsNoTracking()
                on new { request.CompanyId, MaintenanceRequestId = request.Id }
                equals new { card.CompanyId, card.MaintenanceRequestId }
            where request.CompanyId == companyId
            select new { Request = request, CardId = card.Id });
        var records = await (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
                ? query.Take(250)
                : query.OrderByDescending(value => value.Request.CreatedAt).Take(250))
            .ToListAsync(cancellationToken);
        return records.OrderByDescending(value => value.Request.CreatedAt)
            .Select(value => MapMaintenanceRequest(value.Request, value.CardId)).ToArray();
    }

    public Task<MasterDataResult<MaintenanceRequestResponse>> CompleteMaintenanceCardAsync(
        Guid companyId, MasterDataActor actor, Guid cardId, string key,
        MaintenanceCompletionRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "maintenance.card.complete",
            new { cardId, request }, async () =>
            {
                if (string.IsNullOrWhiteSpace(request.Summary) || request.Summary.Length > 1000)
                {
                    return Invalid<MaintenanceRequestResponse>("summary", "Provide a completion summary up to 1000 characters.");
                }
                var card = await db.MaintenanceJobCards.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == cardId, cancellationToken);
                if (card is null)
                {
                    return NotFound<MaintenanceRequestResponse>();
                }
                if (card.Status != "open")
                {
                    return Conflict<MaintenanceRequestResponse>("Only an open maintenance card can be completed.");
                }
                var maintenance = await db.MaintenanceRequests.SingleAsync(value =>
                    value.CompanyId == companyId && value.Id == card.MaintenanceRequestId, cancellationToken);
                card.Status = "completed";
                card.ConcurrencyVersion++;
                card.CompletedAt = DateTimeOffset.UtcNow;
                card.CompletedBySubject = actor.Subject;
                maintenance.Status = "completed";
                var downtime = await db.MachineDowntimes.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.MaintenanceJobCardId == cardId
                    && value.EndedAt == null, cancellationToken);
                if (downtime is not null)
                {
                    downtime.EndedAt = card.CompletedAt;
                }
                AddEvent(companyId, actor, "maintenance-job-card", card.Id, null,
                    "maintenance_completed", new { Summary = request.Summary.Trim() });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<MaintenanceRequestResponse>(
                    MapMaintenanceRequest(maintenance, card.Id));
            }, cancellationToken);

    public Task<MasterDataResult<bool>> IssueMaintenanceSpareAsync(
        Guid companyId, MasterDataActor actor, Guid cardId, string key,
        MaintenanceSpareIssueRequest request, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "maintenance.spare.issue",
            new { cardId, request }, async () =>
            {
                if (request.Quantity <= 0m)
                {
                    return Invalid<bool>("quantity", "Spare issue quantity must be positive.");
                }
                var card = await db.MaintenanceJobCards.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == cardId
                    && value.Status == "open", cancellationToken);
                if (card is null)
                {
                    return Conflict<bool>("Spare parts can only be issued to an open maintenance card.");
                }
                var balance = await GetOrCreateStockBalanceAsync(companyId, request.ItemId,
                    request.WarehouseId, InventoryStockStatuses.Available, cancellationToken);
                if (balance.OnHandQuantity - balance.ReservedQuantity < request.Quantity)
                {
                    return Conflict<bool>("Insufficient unreserved available stock.");
                }
                balance.OnHandQuantity -= request.Quantity;
                balance.ConcurrencyVersion++;
                var spare = new MaintenanceSpareTransaction
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, MaintenanceJobCardId = cardId,
                    ItemId = request.ItemId, WarehouseId = request.WarehouseId,
                    Quantity = request.Quantity, ActorSubject = actor.Subject,
                };
                db.MaintenanceSpareTransactions.Add(spare);
                AddInventoryLedger(companyId, actor, spare.ItemId, spare.WarehouseId,
                    InventoryStockStatuses.Available, "issue", -spare.Quantity,
                    "maintenance-spare", cardId, spare.Id);
                AddEvent(companyId, actor, "maintenance-job-card", cardId, null,
                    "spare_parts_issued", new { spare.ItemId, spare.WarehouseId, spare.Quantity });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<bool>(true, StatusCodes.Status201Created);
            }, cancellationToken);

    public Task<MasterDataResult<bool>> CreatePreventiveScheduleAsync(
        Guid companyId, MasterDataActor actor, string key, PreventiveMaintenanceRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "maintenance.schedule.create",
            request, async () =>
            {
                if (request.IntervalDays <= 0 || string.IsNullOrWhiteSpace(request.Name)
                    || request.Name.Length > 160
                    || !await db.Machines.AnyAsync(value =>
                        value.CompanyId == companyId && value.Id == request.MachineId && value.IsActive,
                        cancellationToken))
                {
                    return Invalid<bool>("schedule", "Select an active machine, provide a name up to 160 characters, and a positive interval.");
                }
                var schedule = new PreventiveMaintenanceSchedule
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, MachineId = request.MachineId,
                    Name = request.Name.Trim(), IntervalDays = request.IntervalDays,
                    NextDueDate = request.NextDueDate,
                };
                db.PreventiveMaintenanceSchedules.Add(schedule);
                AddEvent(companyId, actor, "preventive-maintenance-schedule", schedule.Id, null,
                    "schedule_created", new { schedule.MachineId, schedule.Name, schedule.IntervalDays, schedule.NextDueDate });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<bool>(true, StatusCodes.Status201Created);
            }, cancellationToken);

    public Task<MasterDataResult<MachineDowntimeResponse>> StartDowntimeAsync(
        Guid companyId, MasterDataActor actor, string key, DowntimeRequest request,
        CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor, key, "maintenance.downtime.start",
            request, async () =>
            {
                var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
                if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
                {
                    errors["reason"] = ["Provide a downtime reason up to 500 characters."];
                }
                if (request.StartedAt > DateTimeOffset.UtcNow.AddMinutes(5))
                {
                    errors["startedAt"] = ["Downtime cannot start in the future."];
                }
                if (!await db.Machines.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.MachineId, cancellationToken))
                {
                    errors["machineId"] = ["Machine was not found in this company."];
                }
                if (request.WorkOrderId.HasValue && !await db.WorkOrders.AnyAsync(value =>
                    value.CompanyId == companyId && value.Id == request.WorkOrderId, cancellationToken))
                {
                    errors["workOrderId"] = ["Linked Work Order was not found."];
                }
                if (await db.MachineDowntimes.AnyAsync(value =>
                    value.CompanyId == companyId && value.MachineId == request.MachineId
                    && value.EndedAt == null, cancellationToken))
                {
                    return Conflict<MachineDowntimeResponse>("This machine already has an open downtime event.");
                }
                if (errors.Count > 0)
                {
                    return Invalid<MachineDowntimeResponse>(errors);
                }
                var downtime = new MachineDowntime
                {
                    Id = Guid.NewGuid(), CompanyId = companyId, MachineId = request.MachineId,
                    WorkOrderId = request.WorkOrderId, Reason = request.Reason.Trim(),
                    StartedAt = request.StartedAt, RecordedBySubject = actor.Subject,
                };
                db.MachineDowntimes.Add(downtime);
                AddEvent(companyId, actor, "machine-downtime", downtime.Id, null,
                    "downtime_started", new { downtime.MachineId, downtime.WorkOrderId, downtime.Reason });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<MachineDowntimeResponse>(MapDowntime(downtime),
                    StatusCodes.Status201Created);
            }, cancellationToken);

    public Task<MasterDataResult<MachineDowntimeResponse>> EndDowntimeAsync(
        Guid companyId, MasterDataActor actor, Guid id, string key, CancellationToken cancellationToken) =>
        ExecuteIdempotentAsync(companyId, actor,
            key, "maintenance.downtime.end", new { id }, async () =>
            {
                var downtime = await db.MachineDowntimes.SingleOrDefaultAsync(value =>
                    value.CompanyId == companyId && value.Id == id, cancellationToken);
                if (downtime is null)
                {
                    return NotFound<MachineDowntimeResponse>();
                }
                if (downtime.EndedAt.HasValue)
                {
                    return Conflict<MachineDowntimeResponse>("Downtime has already been ended.");
                }
                downtime.EndedAt = DateTimeOffset.UtcNow;
                downtime.ConcurrencyVersion++;
                AddEvent(companyId, actor, "machine-downtime", downtime.Id, null, "downtime_ended",
                    new { downtime.EndedAt });
                await db.SaveChangesAsync(cancellationToken);
                return new MasterDataResult<MachineDowntimeResponse>(MapDowntime(downtime));
            }, cancellationToken);

    public async Task<IReadOnlyList<MachineDowntimeResponse>> ListDowntimeAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var query = db.MachineDowntimes.AsNoTracking().Where(value => value.CompanyId == companyId);
        var records = await (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? query.Take(250)
            : query.OrderByDescending(value => value.StartedAt).Take(250))
            .ToListAsync(cancellationToken);
        return records.OrderByDescending(value => value.StartedAt).Select(MapDowntime).ToArray();
    }

    public async Task<MasterDataResult<IReadOnlyList<TransactionHistoryResponse>>> GetWorkflowHistoryAsync(
        Guid companyId, string aggregateType, Guid id, CancellationToken cancellationToken)
    {
        var exists = aggregateType switch
        {
            "quality-inspection" => await db.QualityInspections.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "quality-defect-code" => await db.QualityDefectCodes.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "subcontractor" => await db.SubcontractorProfiles.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "job-work-order" => await db.JobWorkOrders.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "maintenance-request" => await db.MaintenanceRequests.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "maintenance-job-card" => await db.MaintenanceJobCards.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "preventive-maintenance-schedule" => await db.PreventiveMaintenanceSchedules.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            "machine-downtime" => await db.MachineDowntimes.AnyAsync(value =>
                value.CompanyId == companyId && value.Id == id, cancellationToken),
            _ => false,
        };
        return exists
            ? new MasterDataResult<IReadOnlyList<TransactionHistoryResponse>>(
                await GetHistoryAsync(companyId, aggregateType, id, cancellationToken))
            : NotFound<IReadOnlyList<TransactionHistoryResponse>>();
    }

    private async Task MoveStockAsync(
        Guid companyId, MasterDataActor actor, Guid itemId, Guid warehouseId,
        string fromStatus, string toStatus, decimal quantity, string referenceType,
        Guid referenceId, Guid referenceLineId, CancellationToken cancellationToken)
    {
        var from = await GetOrCreateStockBalanceAsync(companyId, itemId, warehouseId,
            fromStatus, cancellationToken);
        if (from.OnHandQuantity < quantity)
        {
            throw new InvalidOperationException(
                $"Insufficient {fromStatus} quantity to approve quality inspection {referenceId}.");
        }
        from.OnHandQuantity -= quantity;
        from.ConcurrencyVersion++;
        var to = await GetOrCreateStockBalanceAsync(companyId, itemId, warehouseId,
            toStatus, cancellationToken);
        to.OnHandQuantity += quantity;
        to.ConcurrencyVersion++;
        AddInventoryLedger(companyId, actor, itemId, warehouseId,
            fromStatus, "issue", -quantity, referenceType, referenceId, referenceLineId);
        AddInventoryLedger(companyId, actor, itemId, warehouseId,
            toStatus, "receipt", quantity, referenceType, referenceId, referenceLineId);
    }

    private void AddInventoryLedger(
        Guid companyId, MasterDataActor actor, Guid itemId, Guid warehouseId,
        string stockStatus, string transactionType, decimal quantity,
        string referenceType, Guid referenceId, Guid referenceLineId) =>
        db.InventoryLedgerEntries.Add(new InventoryLedgerEntry
        {
            Id = Guid.NewGuid(), CompanyId = companyId, ItemId = itemId,
            WarehouseId = warehouseId, StockStatus = stockStatus,
            TransactionType = transactionType, Quantity = quantity,
            ReferenceType = referenceType, ReferenceId = referenceId,
            ReferenceLineId = referenceLineId, ActorSubject = actor.Subject,
        });

    private async Task<decimal> GetCustodyOutstandingAsync(
        Guid companyId, Guid issueId, CancellationToken cancellationToken)
    {
        var transactions = db.SubcontractorCustodyTransactions.Where(value =>
            value.CompanyId == companyId && value.JobWorkMaterialIssueId == issueId);
        var dispatched = await transactions.Where(value => value.TransactionType == "dispatch")
            .SumAsync(value => (decimal?)value.Quantity, cancellationToken) ?? 0m;
        var returned = await transactions.Where(value => value.TransactionType == "return")
            .SumAsync(value => (decimal?)value.Quantity, cancellationToken) ?? 0m;
        return dispatched - returned;
    }

    private async Task<JobWorkOrderResponse> MapJobWorkOrderAsync(
        Guid companyId, JobWorkOrder order, CancellationToken cancellationToken)
    {
        var issues = await db.JobWorkMaterialIssues.AsNoTracking().Where(value =>
            value.CompanyId == companyId && value.JobWorkOrderId == order.Id)
            .OrderBy(value => value.Id).ToListAsync(cancellationToken);
        var materials = new List<JobWorkMaterialResponse>(issues.Count);
        decimal totalCustody = 0m;
        foreach (var issue in issues)
        {
            var outstanding = await GetCustodyOutstandingAsync(companyId, issue.Id, cancellationToken);
            totalCustody += outstanding;
            materials.Add(new JobWorkMaterialResponse(issue.Id, issue.ItemId, issue.WarehouseId,
                issue.IssuedQuantity, issue.ReturnedQuantity, outstanding));
        }
        var receiptRows = await db.JobWorkReceipts.AsNoTracking().Where(value =>
                value.CompanyId == companyId && value.JobWorkOrderId == order.Id)
            .ToListAsync(cancellationToken);
        var receipts = receiptRows.OrderBy(value => value.ReceivedAt)
            .Select(value => new JobWorkReceiptResponse(value.Id, value.Reference,
                value.ProcessedQuantity, value.RejectedQuantity, value.ChargeAmount, value.ReceivedAt))
            .ToArray();
        return new JobWorkOrderResponse(order.Id, order.OrderNumber, order.SubcontractorId,
            order.WorkOrderOperationId, order.OutputItemId, order.PlannedQuantity,
            order.ProcessedQuantity, order.RejectedQuantity, order.Status,
            order.ExpectedReturnDate, totalCustody, materials, receipts);
    }

    private async Task<QualityInspectionResponse> MapInspectionAsync(
        Guid companyId, QualityInspection value, CancellationToken cancellationToken)
    {
        var defects = await (
            from inspectionDefect in db.QualityInspectionDefects.AsNoTracking()
            join code in db.QualityDefectCodes.AsNoTracking()
                on new { inspectionDefect.CompanyId, Id = inspectionDefect.DefectCodeId }
                equals new { code.CompanyId, code.Id }
            where inspectionDefect.CompanyId == companyId
                && inspectionDefect.InspectionId == value.Id
            select new QualityDefectResponse(
                code.Id, code.Code, inspectionDefect.Quantity, inspectionDefect.Notes))
            .ToArrayAsync(cancellationToken);
        return new QualityInspectionResponse(value.Id, value.InspectionNumber,
            value.Stage, value.SourceType, value.SourceId, value.SourceLineId,
            value.ItemId, value.JobCardId, value.InspectedQuantity,
            value.AcceptedQuantity, value.RejectedQuantity, value.ReworkQuantity,
            value.HoldQuantity, value.Status, value.CreatedBySubject,
            value.ApprovedBySubject, value.CreatedAt, value.ApprovedAt, defects);
    }

    private static MaintenanceRequestResponse MapMaintenanceRequest(
        MaintenanceRequest value, Guid? cardId) =>
        new(value.Id, value.RequestNumber, value.MachineId, value.Description,
            value.Priority, value.Status, cardId);

    private static MachineDowntimeResponse MapDowntime(MachineDowntime value) =>
        new(value.Id, value.MachineId, value.WorkOrderId, value.Reason,
            value.StartedAt, value.EndedAt);
}
