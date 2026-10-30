namespace Erp.Domain.Commerce;

public static class OrderStatuses
{
    public const string Draft = "draft";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Confirmed = "confirmed";
    public const string Issued = "issued";
    public const string PartiallyReceived = "partially_received";
    public const string Received = "received";
}

public sealed class SalesOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string CustomerPurchaseOrderReference { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
}

public sealed class SalesOrderRevision
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderId { get; set; }
    public int RevisionNumber { get; set; }
    public int ConcurrencyVersion { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerPurchaseOrderReference { get; set; } = string.Empty;
    public DateOnly CustomerPurchaseOrderDate { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = OrderStatuses.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SubmittedBySubject { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBySubject { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? RejectionReason { get; set; }
    public ICollection<SalesOrderLine> Lines { get; set; } = [];
}

public sealed class SalesOrderLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderRevisionId { get; set; }
    public Guid ProductId { get; set; }
    public Guid ProductVariantId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateOnly RequestedDeliveryDate { get; set; }
}

public sealed class SalesOrderFulfillment
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SalesOrderRevisionId { get; set; }
    public Guid SalesOrderLineId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTimeOffset FulfilledAt { get; set; } = DateTimeOffset.UtcNow;
    public string ActorSubject { get; set; } = string.Empty;
}

public sealed class PurchaseRequisition
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string RequisitionNumber { get; set; } = string.Empty;
    public string Status { get; set; } = OrderStatuses.Draft;
    public int ConcurrencyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SubmittedBySubject { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBySubject { get; set; }
    public string? RejectionReason { get; set; }
    public ICollection<PurchaseRequisitionLine> Lines { get; set; } = [];
}

public sealed class PurchaseRequisitionLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseRequisitionId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly RequiredByDate { get; set; }
}

public sealed class PurchaseOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
}

public sealed class PurchaseOrderRevision
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public int RevisionNumber { get; set; }
    public int ConcurrencyVersion { get; set; }
    public Guid SupplierId { get; set; }
    public Guid? PurchaseRequisitionId { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string Status { get; set; } = OrderStatuses.Draft;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
    public DateTimeOffset? SubmittedAt { get; set; }
    public string? SubmittedBySubject { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBySubject { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public string? RejectionReason { get; set; }
    public ICollection<PurchaseOrderLineRevision> Lines { get; set; } = [];
}

public sealed class PurchaseOrderLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderId { get; set; }
    public Guid? PurchaseRequisitionLineId { get; set; }
}

public sealed class PurchaseOrderLineRevision
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PurchaseOrderRevisionId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitOfMeasureId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public DateOnly ExpectedDeliveryDate { get; set; }
}

public sealed class GoodsReceipt
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public Guid PurchaseOrderRevisionId { get; set; }
    public string SupplierDeliveryReference { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBySubject { get; set; } = string.Empty;
    public ICollection<GoodsReceiptLine> Lines { get; set; } = [];
}

public sealed class GoodsReceiptLine
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid GoodsReceiptId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class BusinessTransactionEvent
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public int? RevisionNumber { get; set; }
    public string Action { get; set; } = string.Empty;
    public Guid? ActorUserId { get; set; }
    public string ActorSubject { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string DetailsJson { get; set; } = "{}";
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TransactionIdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public int? StatusCode { get; set; }
    public string? Location { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
