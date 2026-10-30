namespace Erp.Domain.Products;

public static class WorkOrderStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Released = "released";
    public const string OnHold = "on_hold";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public static class JobCardStatuses
{
    public const string Ready = "ready";
    public const string InProgress = "in_progress";
    public const string OnHold = "on_hold";
    public const string PartiallyCompleted = "partially_completed";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public static class ProductionConfirmationStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";
}

public static class InventoryStockStatuses
{
    public const string Available = "available";
    public const string Hold = "hold";
    public const string Quarantine = "quarantine";
}

public sealed class InventoryLedgerEntry
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public string StockStatus { get; set; } = InventoryStockStatuses.Available;
    public string TransactionType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public Guid ReferenceLineId { get; set; }
    public string ActorSubject { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class InventoryStockBalance
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public string StockStatus { get; set; } = InventoryStockStatuses.Available;
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public int ConcurrencyVersion { get; set; }
}

public sealed class InventoryReservation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkOrderMaterialRequirementId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReleasedAt { get; set; }
}

public sealed class WorkOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string WorkOrderNumber { get; set; } = string.Empty;
    public Guid SalesOrderRevisionId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public Guid BomVersionId { get; set; }
    public Guid OutputWarehouseId { get; set; }
    public int BomVersionNumber { get; set; }
    public decimal PlannedQuantity { get; set; }
    public DateOnly PlannedStartDate { get; set; }
    public DateOnly PlannedEndDate { get; set; }
    public int Priority { get; set; } = 3;
    public string Status { get; set; } = WorkOrderStatuses.Draft;
    public int ConcurrencyVersion { get; set; }
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? SubmittedBySubject { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? ApprovedBySubject { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public ICollection<WorkOrderMaterialRequirement> Materials { get; set; } = [];
    public ICollection<WorkOrderOperation> Operations { get; set; } = [];
}

public sealed class WorkOrderMaterialRequirement
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid BomVersionId { get; set; }
    public Guid BomComponentId { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public Guid ItemId { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public decimal QuantityPerFinishedUnit { get; set; }
    public decimal WastePercent { get; set; }
    public decimal RequiredQuantity { get; set; }
}

public sealed class WorkOrderOperation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid ProductionOperationId { get; set; }
    public int Sequence { get; set; }
    public string OperationCodeSnapshot { get; set; } = string.Empty;
    public string OperationNameSnapshot { get; set; } = string.Empty;
    public DateOnly PlannedStartDate { get; set; }
    public DateOnly PlannedEndDate { get; set; }
    public decimal PlannedTargetQuantity { get; set; }
    public string? ProductionLine { get; set; }
    public Guid? MachineId { get; set; }
    public string? AssignedTeam { get; set; }
    public string Status { get; set; } = "pending";
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletedBySubject { get; set; }
}

public sealed class JobCard
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid WorkOrderOperationId { get; set; }
    public string JobCardNumber { get; set; } = string.Empty;
    public decimal PlannedQuantity { get; set; }
    public string? ProductionLine { get; set; }
    public Guid? MachineId { get; set; }
    public string? AssignedTeam { get; set; }
    public string Status { get; set; } = JobCardStatuses.Ready;
    public int ConcurrencyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class ProductionConfirmation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkOrderId { get; set; }
    public Guid JobCardId { get; set; }
    public Guid? CorrectsConfirmationId { get; set; }
    public decimal GoodQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal ReworkQuantity { get; set; }
    public decimal ScrapQuantity { get; set; }
    public string? Shift { get; set; }
    public string? ProductionLine { get; set; }
    public Guid? MachineId { get; set; }
    public string? Team { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string Status { get; set; } = ProductionConfirmationStatuses.PendingApproval;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ApprovedBySubject { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? CorrectionReason { get; set; }
    public string? RejectionReason { get; set; }
}

public static class QualityStatuses
{
    public const string PendingApproval = "pending_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Superseded = "superseded";
}

public sealed class QualityDefectCode
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class QualityInspection
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string InspectionNumber { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public Guid SourceLineId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? JobCardId { get; set; }
    public decimal InspectedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal ReworkQuantity { get; set; }
    public decimal HoldQuantity { get; set; }
    public string Status { get; set; } = QualityStatuses.PendingApproval;
    public int ConcurrencyVersion { get; set; }
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ApprovedBySubject { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? Reason { get; set; }
}

public sealed class QualityInspectionDefect
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid InspectionId { get; set; }
    public Guid DefectCodeId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public sealed class SubcontractorProfile
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SupplierId { get; set; }
    public string RegistrationCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JobWorkOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid SubcontractorId { get; set; }
    public Guid? WorkOrderOperationId { get; set; }
    public Guid OutputItemId { get; set; }
    public Guid OutputWarehouseId { get; set; }
    public decimal PlannedQuantity { get; set; }
    public decimal ProcessedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal UnitCharge { get; set; }
    public string CurrencyCode { get; set; } = "USD";
    public DateOnly ExpectedReturnDate { get; set; }
    public string Status { get; set; } = "draft";
    public int ConcurrencyVersion { get; set; }
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JobWorkMaterialIssue
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JobWorkOrderId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal IssuedQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public int ConcurrencyVersion { get; set; }
}

public sealed class SubcontractorCustodyTransaction
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JobWorkOrderId { get; set; }
    public Guid ItemId { get; set; }
    public Guid? JobWorkMaterialIssueId { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string ActorSubject { get; set; } = string.Empty;
}

public sealed class JobWorkReceipt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid JobWorkOrderId { get; set; }
    public decimal ProcessedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal ChargeAmount { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
}

public sealed class MaintenanceRequest
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MachineId { get; set; }
    public string RequestNumber { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = "open";
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MaintenanceJobCard
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MaintenanceRequestId { get; set; }
    public string CardNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "open";
    public int ConcurrencyVersion { get; set; }
    public string AssignedTeam { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletedBySubject { get; set; }
}

public sealed class PreventiveMaintenanceSchedule
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MachineId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int IntervalDays { get; set; }
    public DateOnly NextDueDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class MaintenanceSpareTransaction
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MaintenanceJobCardId { get; set; }
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public string ActorSubject { get; set; } = string.Empty;
}

public sealed class MachineDowntime
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid MachineId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public Guid? MaintenanceJobCardId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string RecordedBySubject { get; set; } = string.Empty;
    public int ConcurrencyVersion { get; set; }
}

public sealed class WorkOrderTransactionEvent
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid WorkOrderId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ActorSubject { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
