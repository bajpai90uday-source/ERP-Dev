using System.ComponentModel.DataAnnotations;

namespace Erp.Api.Commerce;

internal sealed record DispatchPlanLineRequest
{
    [Required]
    public Guid SalesOrderLineId { get; init; }
    [Required]
    public Guid WarehouseId { get; init; }
    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }
}

internal sealed record DispatchCartonContentRequest
{
    [Required]
    public Guid SalesOrderLineId { get; init; }
    [Range(typeof(decimal), "0.000001", "99999999999999")]
    public decimal Quantity { get; init; }
}

internal sealed record DispatchCartonRequest
{
    [Required, StringLength(60, MinimumLength = 1)]
    public required string CartonNumber { get; init; }
    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal Weight { get; init; }
    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<DispatchCartonContentRequest> Contents { get; init; }
}

internal sealed record DispatchPlanRequest
{
    [Required]
    public Guid SalesOrderId { get; init; }
    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<DispatchPlanLineRequest> Lines { get; init; }
    [Required, MinLength(1), MaxLength(500)]
    public required IReadOnlyList<DispatchCartonRequest> Cartons { get; init; }
}

internal sealed record DispatchPlanLineResponse(
    Guid Id, Guid SalesOrderLineId, Guid ProductVariantId, string VariantCode,
    string SizeCode, string ColorCode, string ColorName, Guid ItemId,
    Guid WarehouseId, string WarehouseCode, decimal Quantity);

internal sealed record DispatchCartonContentResponse(
    Guid DispatchShipmentLineId, string VariantCode, string SizeCode,
    string ColorCode, decimal Quantity);

internal sealed record DispatchCartonResponse(
    Guid Id, string CartonNumber, decimal Weight,
    IReadOnlyList<DispatchCartonContentResponse> Contents);

internal sealed record DispatchShipmentResponse(
    Guid Id, string ShipmentNumber, Guid SalesOrderId, string SalesOrderNumber,
    string Status, DateTimeOffset CreatedAt, string CreatedBySubject,
    string? AuthorizedBySubject, DateTimeOffset? AuthorizedAt,
    string? DispatchedBySubject, DateTimeOffset? DispatchedAt,
    IReadOnlyList<DispatchPlanLineResponse> Lines,
    IReadOnlyList<DispatchCartonResponse> Cartons);

internal interface IDispatchService
{
    Task<IReadOnlyList<DispatchShipmentResponse>> ListDispatchShipmentsAsync(
        Guid companyId, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<DispatchShipmentResponse>> CreateDispatchPlanAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        DispatchPlanRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<DispatchShipmentResponse>> AuthorizeDispatchAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<DispatchShipmentResponse>> ConfirmDispatchAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken);
}
