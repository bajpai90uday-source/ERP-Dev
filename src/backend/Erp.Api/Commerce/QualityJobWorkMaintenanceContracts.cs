using System.ComponentModel.DataAnnotations;

namespace Erp.Api.Commerce;

internal sealed record QualityInspectionRequest
{
    [Required, StringLength(24, MinimumLength = 3)]
    public string Stage { get; init; } = string.Empty;
    [Required, StringLength(32, MinimumLength = 3)]
    public string SourceType { get; init; } = string.Empty;
    public Guid SourceId { get; init; }
    public Guid SourceLineId { get; init; }
    public Guid ItemId { get; init; }
    public Guid WarehouseId { get; init; }
    public Guid? JobCardId { get; init; }
    [Range(typeof(decimal), "0.000001", "999999999999")]
    public decimal InspectedQuantity { get; init; }
    [Range(typeof(decimal), "0", "999999999999")]
    public decimal AcceptedQuantity { get; init; }
    [Range(typeof(decimal), "0", "999999999999")]
    public decimal RejectedQuantity { get; init; }
    [Range(typeof(decimal), "0", "999999999999")]
    public decimal ReworkQuantity { get; init; }
    [Range(typeof(decimal), "0", "999999999999")]
    public decimal HoldQuantity { get; init; }
    [StringLength(500)]
    public string? Reason { get; init; }
    public IReadOnlyList<QualityDefectRequest> Defects { get; init; } = [];
}

internal sealed record QualityDefectRequest(Guid DefectCodeId, decimal Quantity, string? Notes);
internal sealed record QualityInspectionResponse(
    Guid Id, string InspectionNumber, string Stage, string SourceType, Guid SourceId,
    Guid SourceLineId, Guid ItemId, Guid? JobCardId, decimal InspectedQuantity,
    decimal AcceptedQuantity, decimal RejectedQuantity, decimal ReworkQuantity,
    decimal HoldQuantity, string Status, string CreatedBySubject,
    string? ApprovedBySubject, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt,
    IReadOnlyList<QualityDefectResponse> Defects);
internal sealed record QualityDefectResponse(
    Guid DefectCodeId, string Code, decimal Quantity, string? Notes);

internal sealed record DefectCodeRequest(
    [Required, StringLength(40, MinimumLength = 1)] string Code,
    [Required, StringLength(240, MinimumLength = 1)] string Description);
internal sealed record DefectCodeResponse(Guid Id, string Code, string Description, bool IsActive);

internal sealed record JobWorkMaterialRequest(Guid ItemId, Guid WarehouseId, decimal Quantity);
internal sealed record SubcontractorRequest(
    Guid SupplierId, string RegistrationCode);
internal sealed record JobWorkOrderRequest(
    Guid SubcontractorId, Guid? WorkOrderOperationId, Guid OutputItemId,
    Guid OutputWarehouseId, decimal PlannedQuantity, decimal UnitCharge,
    string CurrencyCode, DateOnly ExpectedReturnDate,
    IReadOnlyList<JobWorkMaterialRequest> Materials);
internal sealed record JobWorkDispatchRequest(Guid MaterialIssueId, decimal Quantity);
internal sealed record JobWorkMaterialReturnRequest(Guid MaterialIssueId, decimal Quantity);
internal sealed record JobWorkReceiptRequest(
    string Reference, decimal ProcessedQuantity, decimal RejectedQuantity);
internal sealed record JobWorkOrderResponse(
    Guid Id, string OrderNumber, Guid SubcontractorId, Guid? WorkOrderOperationId,
    Guid OutputItemId, decimal PlannedQuantity, decimal ProcessedQuantity,
    decimal RejectedQuantity, string Status, DateOnly ExpectedReturnDate,
    decimal OutstandingHeldMaterial, IReadOnlyList<JobWorkMaterialResponse> Materials,
    IReadOnlyList<JobWorkReceiptResponse> Receipts);
internal sealed record JobWorkMaterialResponse(
    Guid IssueId, Guid ItemId, Guid WarehouseId, decimal IssuedQuantity,
    decimal ReturnedQuantity, decimal OutstandingQuantity);
internal sealed record JobWorkReceiptResponse(
    Guid Id, string Reference, decimal ProcessedQuantity, decimal RejectedQuantity,
    decimal ChargeAmount, DateTimeOffset ReceivedAt);

internal sealed record MaintenanceRequestRequest(
    Guid MachineId, string RequestNumber, string Description, string Priority,
    string AssignedTeam, Guid? WorkOrderId);
internal sealed record MaintenanceSpareIssueRequest(
    Guid ItemId, Guid WarehouseId, decimal Quantity);
internal sealed record MaintenanceCompletionRequest(string Summary);
internal sealed record PreventiveMaintenanceRequest(
    Guid MachineId, string Name, int IntervalDays, DateOnly NextDueDate);
internal sealed record DowntimeRequest(
    Guid MachineId, Guid? WorkOrderId, string Reason, DateTimeOffset StartedAt);
internal sealed record MaintenanceRequestResponse(
    Guid Id, string RequestNumber, Guid MachineId, string Description,
    string Priority, string Status, Guid? JobCardId);
internal sealed record MachineDowntimeResponse(
    Guid Id, Guid MachineId, Guid? WorkOrderId, string Reason,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

internal interface IQualityJobWorkMaintenanceService
{
    Task<IReadOnlyList<QualityInspectionResponse>> ListInspectionsAsync(
        Guid companyId, string? stage, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<QualityInspectionResponse>> CreateInspectionAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        QualityInspectionRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<QualityInspectionResponse>> ApproveInspectionAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<bool>> CreateDefectCodeAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        DefectCodeRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<DefectCodeResponse>> ListDefectCodesAsync(
        Guid companyId, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<Guid>> RegisterSubcontractorAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        SubcontractorRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<JobWorkOrderResponse>> CreateJobWorkOrderAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        JobWorkOrderRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<JobWorkOrderResponse>> DispatchJobWorkAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        IReadOnlyList<JobWorkDispatchRequest> requests, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<JobWorkOrderResponse>> ReceiveJobWorkAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        JobWorkReceiptRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<JobWorkOrderResponse>> ReconcileJobWorkMaterialAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        JobWorkMaterialReturnRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobWorkOrderResponse>> ListJobWorkOrdersAsync(
        Guid companyId, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<MaintenanceRequestResponse>> CreateMaintenanceRequestAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        MaintenanceRequestRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<MaintenanceRequestResponse>> ListMaintenanceRequestsAsync(
        Guid companyId, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<MaintenanceRequestResponse>> CompleteMaintenanceCardAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid cardId, string key,
        MaintenanceCompletionRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<bool>> IssueMaintenanceSpareAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid cardId, string key,
        MaintenanceSpareIssueRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<bool>> CreatePreventiveScheduleAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        PreventiveMaintenanceRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<MachineDowntimeResponse>> StartDowntimeAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, string key,
        DowntimeRequest request, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<MachineDowntimeResponse>> EndDowntimeAsync(
        Guid companyId, Erp.Api.MasterData.MasterDataActor actor, Guid id, string key,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<MachineDowntimeResponse>> ListDowntimeAsync(
        Guid companyId, CancellationToken cancellationToken);
    Task<Erp.Api.MasterData.MasterDataResult<IReadOnlyList<TransactionHistoryResponse>>>
        GetWorkflowHistoryAsync(Guid companyId, string aggregateType, Guid id, CancellationToken cancellationToken);
}
