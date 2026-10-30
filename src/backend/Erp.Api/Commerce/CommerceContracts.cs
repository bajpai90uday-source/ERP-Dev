using System.ComponentModel.DataAnnotations;
using Erp.Domain.Commerce;

namespace Erp.Api.Commerce;

/// <summary>One product/variant request on a sales order.</summary>
internal sealed record SalesOrderLineRequest
{
    [Required]
    public Guid ProductId { get; init; }

    [Required]
    public Guid ProductVariantId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal UnitPrice { get; init; }

    public DateOnly RequestedDeliveryDate { get; init; }
}

/// <summary>Sales order header and lines to create, amend, or revise while draft.</summary>
internal sealed record SalesOrderRequest
{
    [Required]
    public Guid CustomerId { get; init; }

    [Required, StringLength(80, MinimumLength = 1)]
    public required string CustomerPurchaseOrderReference { get; init; }

    public DateOnly CustomerPurchaseOrderDate { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<SalesOrderLineRequest> Lines { get; init; }
}

/// <summary>One persisted sales order line and its fulfillment totals.</summary>
internal sealed record SalesOrderLineResponse(
    Guid Id,
    Guid ProductId,
    string ProductCode,
    string ProductName,
    Guid ProductVariantId,
    string VariantCode,
    string VariantName,
    string StockUnitCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly RequestedDeliveryDate,
    decimal FulfilledQuantity,
    decimal OutstandingQuantity);

/// <summary>A versioned sales order with approval and fulfillment state.</summary>
internal sealed record SalesOrderResponse(
    Guid Id,
    string OrderNumber,
    int RevisionNumber,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string CustomerPurchaseOrderReference,
    DateOnly CustomerPurchaseOrderDate,
    string CurrencyCode,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBySubject,
    DateTimeOffset? ConfirmedAt,
    string? RejectionReason,
    IReadOnlyList<SalesOrderLineResponse> Lines);

/// <summary>Quantity fulfillment posted against a confirmed sales-order revision.</summary>
internal sealed record SalesFulfillmentRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public required string Reference { get; init; }

    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<SalesFulfillmentLineRequest> Lines { get; init; }
}

/// <summary>Fulfillment quantity for one sales order line.</summary>
internal sealed record SalesFulfillmentLineRequest
{
    [Required]
    public Guid SalesOrderLineId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }
}

/// <summary>A timestamped event in a transaction's audit history.</summary>
internal sealed record TransactionHistoryResponse(
    string AggregateType,
    Guid AggregateId,
    int? RevisionNumber,
    string Action,
    string ActorSubject,
    string? Reason,
    string DetailsJson,
    DateTimeOffset OccurredAt);

/// <summary>One requested item on a purchase requisition.</summary>
internal sealed record PurchaseRequisitionLineRequest
{
    [Required]
    public Guid ItemId { get; init; }

    [Required]
    public Guid UnitOfMeasureId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }

    public DateOnly RequiredByDate { get; init; }
}

/// <summary>Purchase requisition lines to create or revise while draft.</summary>
internal sealed record PurchaseRequisitionRequest
{
    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<PurchaseRequisitionLineRequest> Lines { get; init; }
}

/// <summary>A persisted purchase requisition line.</summary>
internal sealed record PurchaseRequisitionLineResponse(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode,
    decimal Quantity,
    DateOnly RequiredByDate);

/// <summary>A purchase requisition and its approval state.</summary>
internal sealed record PurchaseRequisitionResponse(
    Guid Id,
    string RequisitionNumber,
    string Status,
    DateTimeOffset CreatedAt,
    string CreatedBySubject,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBySubject,
    string? RejectionReason,
    IReadOnlyList<PurchaseRequisitionLineResponse> Lines);

/// <summary>One supplier commitment line on a purchase order.</summary>
internal sealed record PurchaseOrderLineRequest
{
    public Guid? LineId { get; init; }

    public Guid? PurchaseRequisitionLineId { get; init; }

    [Required]
    public Guid ItemId { get; init; }

    [Required]
    public Guid UnitOfMeasureId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal UnitPrice { get; init; }

    public DateOnly ExpectedDeliveryDate { get; init; }
}

/// <summary>Supplier and line commitments to create or amend a purchase order.</summary>
internal sealed record PurchaseOrderRequest
{
    [Required]
    public Guid SupplierId { get; init; }

    [Required, StringLength(3, MinimumLength = 3)]
    public required string CurrencyCode { get; init; }

    public Guid? PurchaseRequisitionId { get; init; }

    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<PurchaseOrderLineRequest> Lines { get; init; }
}

/// <summary>A purchase order line with received and outstanding quantities.</summary>
internal sealed record PurchaseOrderLineResponse(
    Guid LineId,
    Guid? PurchaseRequisitionLineId,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid UnitOfMeasureId,
    string UnitOfMeasureCode,
    decimal Quantity,
    decimal UnitPrice,
    DateOnly ExpectedDeliveryDate,
    decimal ReceivedQuantity,
    decimal OutstandingQuantity);

/// <summary>A versioned purchase order and its current status.</summary>
internal sealed record PurchaseOrderResponse(
    Guid Id,
    string OrderNumber,
    int RevisionNumber,
    Guid SupplierId,
    string SupplierCode,
    string SupplierName,
    Guid? PurchaseRequisitionId,
    string CurrencyCode,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ApprovedAt,
    string? ApprovedBySubject,
    DateTimeOffset? IssuedAt,
    string? RejectionReason,
    IReadOnlyList<PurchaseOrderLineResponse> Lines);

/// <summary>One received quantity against an issued purchase order line.</summary>
internal sealed record GoodsReceiptLineRequest
{
    [Required]
    public Guid PurchaseOrderLineId { get; init; }

    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }
}

/// <summary>Supplier delivery quantities to record against an issued purchase order.</summary>
internal sealed record GoodsReceiptRequest
{
    [Required, StringLength(80, MinimumLength = 1)]
    public required string SupplierDeliveryReference { get; init; }

    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<GoodsReceiptLineRequest> Lines { get; init; }
}

/// <summary>A persisted supplier receipt and its purchase-order source.</summary>
internal sealed record GoodsReceiptResponse(
    Guid Id,
    string ReceiptNumber,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    string SupplierDeliveryReference,
    DateTimeOffset ReceivedAt,
    IReadOnlyList<GoodsReceiptLineResponse> Lines);

/// <summary>One received purchase-order line.</summary>
internal sealed record GoodsReceiptLineResponse(
    Guid PurchaseOrderLineId,
    decimal Quantity);

/// <summary>A rejection reason for an approval decision.</summary>
internal sealed record RejectionRequest
{
    [Required, StringLength(500, MinimumLength = 1)]
    public required string Reason { get; init; }
}

internal static class CommerceStatus
{
    public static string PurchaseOrderStatus(
        string status,
        decimal receivedQuantity,
        decimal orderedQuantity)
    {
        if (status is not (OrderStatuses.Issued or OrderStatuses.PartiallyReceived
            or OrderStatuses.Received))
        {
            return status;
        }

        if (receivedQuantity == 0)
        {
            return OrderStatuses.Issued;
        }

        return receivedQuantity >= orderedQuantity
            ? OrderStatuses.Received
            : OrderStatuses.PartiallyReceived;
    }
}
