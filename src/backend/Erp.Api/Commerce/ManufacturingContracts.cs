using System.ComponentModel.DataAnnotations;
using Erp.Api.MasterData;

namespace Erp.Api.Commerce;

/// <summary>Reasoned stock adjustment posted to the inventory ledger.</summary>
internal sealed record InventoryAdjustmentRequest
{
    [Required]
    public Guid ItemId { get; init; }

    [Required]
    public Guid WarehouseId { get; init; }

    [Required, RegularExpression("^(available|hold|quarantine)$")]
    public required string StockStatus { get; init; }

    [Range(typeof(decimal), "-99999999999999", "99999999999999")]
    public decimal Quantity { get; init; }

    [Required, StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

/// <summary>Inventory balance for an item, active warehouse, and stock status.</summary>
internal sealed record InventoryBalanceResponse(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid WarehouseId,
    string WarehouseCode,
    string StockStatus,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

/// <summary>Planning horizon for a material-requirements calculation.</summary>
internal sealed record MrpRequest
{
    public DateOnly FromDate { get; init; }
    public DateOnly ThroughDate { get; init; }
}

/// <summary>Gross, available, reserved, and net quantities for one planned material.</summary>
internal sealed record MrpMaterialRequirementResponse(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string StockUnitCode,
    decimal GrossRequirement,
    decimal EligibleOnHand,
    decimal AlreadyReserved,
    decimal OpenPurchaseOrderReceipts,
    decimal NetRequirement,
    decimal PurchaseSuggestionQuantity,
    DateOnly NeedByDate,
    IReadOnlyList<string> ContributingOrders);

/// <summary>Material requirements and proposal-only purchasing suggestions for a date range.</summary>
internal sealed record MrpRunResponse(
    DateOnly FromDate,
    DateOnly ThroughDate,
    DateTimeOffset CalculatedAt,
    IReadOnlyList<MrpMaterialRequirementResponse> Materials);

/// <summary>Production operation and planned dates for a new Work Order.</summary>
internal sealed record WorkOrderOperationRequest
{
    [Required]
    public Guid ProductionOperationId { get; init; }

    [Range(1, int.MaxValue)]
    public int Sequence { get; init; }

    public DateOnly PlannedStartDate { get; init; }
    public DateOnly PlannedEndDate { get; init; }
}

/// <summary>Sales-linked Work Order using an approved BOM version and output warehouse.</summary>
internal sealed record WorkOrderRequest
{
    [Required]
    public Guid SalesOrderId { get; init; }

    [Required]
    public Guid SalesOrderLineId { get; init; }

    [Required]
    public Guid BomVersionId { get; init; }

    [Required]
    public Guid OutputWarehouseId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal PlannedQuantity { get; init; }

    public DateOnly PlannedStartDate { get; init; }
    public DateOnly PlannedEndDate { get; init; }

    [Required, MinLength(1), MaxLength(100)]
    public required IReadOnlyList<WorkOrderOperationRequest> Operations { get; init; }
}

/// <summary>Production operation snapshot and completion state for a Work Order.</summary>
internal sealed record WorkOrderOperationResponse(
    Guid Id,
    int Sequence,
    Guid ProductionOperationId,
    string Code,
    string Name,
    DateOnly PlannedStartDate,
    DateOnly PlannedEndDate,
    decimal PlannedTargetQuantity,
    string? ProductionLine,
    Guid? MachineId,
    string? AssignedTeam,
    string Status,
    DateTimeOffset? CompletedAt,
    string? CompletedBySubject);

/// <summary>BOM-derived material snapshot expressed in the item's stock unit.</summary>
internal sealed record WorkOrderMaterialResponse(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode,
    string SourcePath,
    decimal QuantityPerFinishedUnit,
    decimal WastePercent,
    decimal RequiredQuantity);

/// <summary>Work Order, its frozen engineering references, operations, and materials.</summary>
internal sealed record WorkOrderResponse(
    Guid Id,
    string WorkOrderNumber,
    Guid SalesOrderId,
    string SalesOrderNumber,
    Guid SalesOrderLineId,
    Guid ProductId,
    Guid ProductVariantId,
    string ProductCode,
    string VariantCode,
    Guid BomVersionId,
    int BomVersionNumber,
    decimal PlannedQuantity,
    DateOnly PlannedStartDate,
    DateOnly PlannedEndDate,
    Guid OutputWarehouseId,
    int Priority,
    string Status,
    string CreatedBySubject,
    string? ApprovedBySubject,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ReleasedAt,
    DateTimeOffset? CompletedAt,
    string? RejectionReason,
    IReadOnlyList<WorkOrderOperationResponse> Operations,
    IReadOnlyList<WorkOrderMaterialResponse> Materials);

/// <summary>Required, available, and incoming quantity for a Work Order material.</summary>
internal sealed record WorkOrderMaterialAvailabilityLine(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    string StockUnitCode,
    decimal RequiredQuantity,
    decimal EligibleOnHand,
    decimal ReservedForOtherWork,
    decimal ExpectedReceiptsByStart,
    decimal AvailableForWorkOrder,
    decimal Shortage);

/// <summary>Readiness assessment for the materials required by a Work Order.</summary>
internal sealed record WorkOrderMaterialAvailabilityResponse(
    Guid WorkOrderId,
    string WorkOrderNumber,
    DateOnly NeedByDate,
    bool IsReady,
    IReadOnlyList<WorkOrderMaterialAvailabilityLine> Materials);

/// <summary>Reason required for a Work Order rejection, hold, or cancellation.</summary>
internal sealed record WorkOrderReasonRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

/// <summary>Planned resources and target for one Work Order operation.</summary>
internal sealed record ProductionPlanOperationRequest
{
    [Required]
    public Guid OperationId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal TargetQuantity { get; init; }

    [StringLength(100)]
    public string? ProductionLine { get; init; }

    public Guid? MachineId { get; init; }

    [StringLength(100)]
    public string? AssignedTeam { get; init; }

    public DateOnly PlannedStartDate { get; init; }
    public DateOnly PlannedEndDate { get; init; }
}

/// <summary>Authorized planning updates for an approved, unreleased Work Order.</summary>
internal sealed record ProductionPlanRequest
{
    [Range(1, 5)]
    public int Priority { get; init; } = 3;

    public DateOnly PlannedStartDate { get; init; }
    public DateOnly PlannedEndDate { get; init; }

    [Required, MinLength(1), MaxLength(100)]
    public required IReadOnlyList<ProductionPlanOperationRequest> Operations { get; init; }
}

/// <summary>Production plan, schedule risk, and planned-versus-actual output.</summary>
internal sealed record ProductionPlanResponse(
    Guid WorkOrderId,
    string WorkOrderNumber,
    int Priority,
    DateOnly PlannedStartDate,
    DateOnly PlannedEndDate,
    decimal PlannedQuantity,
    decimal ActualQuantity,
    decimal AcceptedGoodQuantity,
    decimal OutstandingQuantity,
    string ScheduleStatus,
    string WorkOrderStatus,
    int DowntimeEventCount,
    decimal DowntimeHours,
    IReadOnlyList<WorkOrderOperationResponse> Operations);

/// <summary>Job Card generated from a released Work Order operation.</summary>
internal sealed record JobCardResponse(
    Guid Id,
    string JobCardNumber,
    Guid WorkOrderId,
    string WorkOrderNumber,
    Guid WorkOrderOperationId,
    int OperationSequence,
    string OperationCode,
    string OperationName,
    decimal PlannedQuantity,
    decimal ProcessedQuantity,
    decimal OutstandingQuantity,
    string? ProductionLine,
    Guid? MachineId,
    string? AssignedTeam,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Production output classification and shop-floor context for one confirmation.</summary>
internal sealed record ProductionConfirmationRequest
{
    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal GoodQuantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal RejectedQuantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal ReworkQuantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal ScrapQuantity { get; init; }

    [StringLength(80)]
    public string? Shift { get; init; }

    [StringLength(100)]
    public string? ProductionLine { get; init; }

    public Guid? MachineId { get; init; }

    [StringLength(100)]
    public string? Team { get; init; }

    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}

/// <summary>Requested replacement quantities and reason for an authorized correction.</summary>
internal sealed record ProductionConfirmationCorrectionRequest
{
    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal GoodQuantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal RejectedQuantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal ReworkQuantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal ScrapQuantity { get; init; }

    [Required, StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

/// <summary>Immutable production confirmation with approval and correction state.</summary>
internal sealed record ProductionConfirmationResponse(
    Guid Id,
    Guid WorkOrderId,
    Guid JobCardId,
    Guid? CorrectsConfirmationId,
    decimal GoodQuantity,
    decimal RejectedQuantity,
    decimal ReworkQuantity,
    decimal ScrapQuantity,
    string? Shift,
    string? ProductionLine,
    Guid? MachineId,
    string? Team,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Status,
    string CreatedBySubject,
    DateTimeOffset CreatedAt,
    string? ApprovedBySubject,
    DateTimeOffset? ApprovedAt,
    string? CorrectionReason,
    string? RejectionReason);

internal interface IManufacturingService
{
    Task<IReadOnlyList<InventoryBalanceResponse>> GetInventoryBalancesAsync(
        Guid companyId, Guid? itemId, Guid? warehouseId, CancellationToken cancellationToken);

    Task<MasterDataResult<InventoryBalanceResponse>> AdjustInventoryAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        InventoryAdjustmentRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<MrpRunResponse>> RunMrpAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        MrpRequest request, CancellationToken cancellationToken);

    Task<PageResponse<WorkOrderResponse>> ListWorkOrdersAsync(
        Guid companyId, int page, int pageSize, string? search, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> GetWorkOrderAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> CreateWorkOrderAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        WorkOrderRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> SubmitWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> ApproveWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> RejectWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> ReleaseWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> HoldWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> CompleteWorkOrderOperationAsync(
        Guid companyId, MasterDataActor actor, Guid id, Guid operationId, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> CompleteWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderResponse>> CancelWorkOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<WorkOrderMaterialAvailabilityResponse>> GetWorkOrderMaterialsAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TransactionHistoryResponse>> GetWorkOrderHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);

    Task<PageResponse<ProductionPlanResponse>> ListProductionPlansAsync(
        Guid companyId, int page, int pageSize, CancellationToken cancellationToken);

    Task<MasterDataResult<ProductionPlanResponse>> UpdateProductionPlanAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        ProductionPlanRequest request, CancellationToken cancellationToken);

    Task<PageResponse<JobCardResponse>> ListJobCardsAsync(
        Guid companyId, int page, int pageSize, Guid? workOrderId, CancellationToken cancellationToken);

    Task<MasterDataResult<IReadOnlyList<JobCardResponse>>> GenerateJobCardsAsync(
        Guid companyId, MasterDataActor actor, Guid workOrderId, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<MasterDataResult<JobCardResponse>> ChangeJobCardStatusAsync(
        Guid companyId, MasterDataActor actor, Guid jobCardId, string idempotencyKey,
        string action, WorkOrderReasonRequest? request, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductionConfirmationResponse>> ListProductionConfirmationsAsync(
        Guid companyId, Guid jobCardId, CancellationToken cancellationToken);

    Task<MasterDataResult<ProductionConfirmationResponse>> GetProductionConfirmationAsync(
        Guid companyId, Guid confirmationId, CancellationToken cancellationToken);

    Task<MasterDataResult<ProductionConfirmationResponse>> CreateProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid jobCardId, string idempotencyKey,
        ProductionConfirmationRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<ProductionConfirmationResponse>> ApproveProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid confirmationId, string idempotencyKey,
        CancellationToken cancellationToken);

    Task<MasterDataResult<ProductionConfirmationResponse>> RejectProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid confirmationId, string idempotencyKey,
        WorkOrderReasonRequest request, CancellationToken cancellationToken);

    Task<MasterDataResult<ProductionConfirmationResponse>> CorrectProductionConfirmationAsync(
        Guid companyId, MasterDataActor actor, Guid confirmationId, string idempotencyKey,
        ProductionConfirmationCorrectionRequest request, CancellationToken cancellationToken);
}
