using Erp.Api.MasterData;

namespace Erp.Api.Commerce;

internal interface ISalesPurchaseService
{
    Task<PageResponse<SalesOrderResponse>> ListSalesOrdersAsync(
        Guid companyId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> GetSalesOrderAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> CreateSalesOrderAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey, SalesOrderRequest request,
        CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> UpdateSalesDraftAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        SalesOrderRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> SubmitSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> ApproveSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> RejectSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        RejectionRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> ConfirmSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> AmendSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        SalesOrderRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<SalesOrderResponse>> FulfillSalesOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        SalesFulfillmentRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionHistoryResponse>> SalesOrderHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);

    Task<PageResponse<PurchaseRequisitionResponse>> ListPurchaseRequisitionsAsync(
        Guid companyId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseRequisitionResponse>> GetPurchaseRequisitionAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseRequisitionResponse>> CreatePurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        PurchaseRequisitionRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseRequisitionResponse>> UpdatePurchaseRequisitionDraftAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        PurchaseRequisitionRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseRequisitionResponse>> SubmitPurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseRequisitionResponse>> ApprovePurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseRequisitionResponse>> RejectPurchaseRequisitionAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        RejectionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionHistoryResponse>> PurchaseRequisitionHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);

    Task<PageResponse<PurchaseOrderResponse>> ListPurchaseOrdersAsync(
        Guid companyId, string? search, int page, int pageSize, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> GetPurchaseOrderAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> CreatePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, string idempotencyKey,
        PurchaseOrderRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> UpdatePurchaseOrderDraftAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        PurchaseOrderRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> SubmitPurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> ApprovePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> RejectPurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        RejectionRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> IssuePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey, CancellationToken cancellationToken);
    Task<MasterDataResult<PurchaseOrderResponse>> AmendPurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        PurchaseOrderRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<GoodsReceiptResponse>> ReceivePurchaseOrderAsync(
        Guid companyId, MasterDataActor actor, Guid id, string idempotencyKey,
        GoodsReceiptRequest request, CancellationToken cancellationToken);
    Task<MasterDataResult<GoodsReceiptResponse>> GetGoodsReceiptAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionHistoryResponse>> PurchaseOrderHistoryAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken);
}
