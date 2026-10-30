using System.Security.Claims;
using Erp.Api.MasterData;
using Erp.Api.Security;

namespace Erp.Api.Commerce;

internal static class CommerceEndpoints
{
    public static void MapCommerceEndpoints(this WebApplication app)
    {
        MapSalesOrders(app);
        MapDispatch(app);
        MapPurchaseRequisitions(app);
        MapPurchaseOrders(app);
        MapManufacturing(app);
        MapQualityJobWorkMaintenance(app);
    }

    private static void MapSalesOrders(WebApplication app)
    {
        var orders = app.MapGroup("/api/v1/sales/orders");

        orders.MapGet("", async (
            HttpContext context,
            ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var (page, pageSize, search, errors) = ParseQuery(context.Request);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await service.ListSalesOrdersAsync(
                CompanyId(context.User), search, page, pageSize, cancellationToken);
            return Results.Ok(result);
        })
            .RequireAuthorization(PermissionPolicies.For("sales.read"))
            .WithName("ListSalesOrders")
            .WithSummary("Search and page company sales orders")
            .Produces<PageResponse<SalesOrderResponse>>()
            .Produces(StatusCodes.Status403Forbidden);

        orders.MapGet("/{id:guid}", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetSalesOrderAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.read"))
            .WithName("GetSalesOrder")
            .WithSummary("Get a sales order and its current revision")
            .Produces<SalesOrderResponse>()
            .Produces(StatusCodes.Status404NotFound);

        orders.MapPost("", async (
            HttpContext context, SalesOrderRequest request, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateSalesOrderAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id ? $"/api/v1/sales/orders/{id}" : null);
        })
            .RequireAuthorization(PermissionPolicies.For("sales.create"))
            .WithName("CreateSalesOrder")
            .WithSummary("Create a draft sales order with customer PO and product lines")
            .Produces<SalesOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPut("/{id:guid}/draft", async (
            Guid id, HttpContext context, SalesOrderRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.UpdateSalesDraftAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.update"))
            .WithName("UpdateSalesOrderDraft")
            .WithSummary("Update an unconfirmed sales-order draft")
            .Produces<SalesOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/submit", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.SubmitSalesOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.submit"))
            .WithName("SubmitSalesOrder")
            .WithSummary("Submit a sales-order revision for approval")
            .Produces<SalesOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/approve", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ApproveSalesOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.approve"))
            .WithName("ApproveSalesOrder")
            .WithSummary("Approve a submitted sales-order revision")
            .Produces<SalesOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/reject", async (
            Guid id, HttpContext context, RejectionRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.RejectSalesOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.approve"))
            .WithName("RejectSalesOrder")
            .WithSummary("Reject a submitted sales-order revision with a reason")
            .Produces<SalesOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/confirm", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ConfirmSalesOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.confirm"))
            .WithName("ConfirmSalesOrder")
            .WithSummary("Confirm an approved sales-order revision")
            .Produces<SalesOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/amend", async (
            Guid id, HttpContext context, SalesOrderRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.AmendSalesOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.amend"))
            .WithName("AmendSalesOrder")
            .WithSummary("Create a new draft amendment to a confirmed sales order")
            .Produces<SalesOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/fulfillments", async (
            Guid id, HttpContext context, SalesFulfillmentRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.FulfillSalesOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("sales.fulfill"))
            .WithName("RecordSalesOrderFulfillment")
            .WithSummary("Record partial fulfillment without posting inventory")
            .Produces<SalesOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapGet("/{id:guid}/history", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var order = await service.GetSalesOrderAsync(
                CompanyId(context.User), id, cancellationToken);
            return order.StatusCode == StatusCodes.Status404NotFound
                ? Results.NotFound()
                : Results.Ok(await service.SalesOrderHistoryAsync(
                    CompanyId(context.User), id, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("sales.read"))
            .WithName("GetSalesOrderHistory")
            .WithSummary("Get the audit history of a sales order")
            .Produces<IReadOnlyList<TransactionHistoryResponse>>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapDispatch(WebApplication app)
    {
        var dispatch = app.MapGroup("/api/v1/dispatch");
        dispatch.MapGet("/shipments", async (
            HttpContext context, IDispatchService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListDispatchShipmentsAsync(
                CompanyId(context.User), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("dispatch.read"));
        dispatch.MapPost("/shipments", async (
            HttpContext context, DispatchPlanRequest request, IDispatchService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateDispatchPlanAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id
                ? $"/api/v1/dispatch/shipments/{id}" : null);
        })
            .RequireAuthorization(PermissionPolicies.For("dispatch.create"))
            .Produces<DispatchShipmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        dispatch.MapPost("/shipments/{id:guid}/authorize", async (
            Guid id, HttpContext context, IDispatchService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.AuthorizeDispatchAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("dispatch.ship"))
            .Produces<DispatchShipmentResponse>()
            .Produces(StatusCodes.Status409Conflict);
        dispatch.MapPost("/shipments/{id:guid}/confirm", async (
            Guid id, HttpContext context, IDispatchService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ConfirmDispatchAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("dispatch.ship"))
            .Produces<DispatchShipmentResponse>()
            .Produces(StatusCodes.Status409Conflict);
    }

    private static void MapPurchaseRequisitions(WebApplication app)
    {
        var requisitions = app.MapGroup("/api/v1/purchase/requisitions");

        requisitions.MapGet("", async (
            HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var (page, pageSize, search, errors) = ParseQuery(context.Request);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            return Results.Ok(await service.ListPurchaseRequisitionsAsync(
                CompanyId(context.User), search, page, pageSize, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("ListPurchaseRequisitions")
            .WithSummary("Search and page purchase requisitions")
            .Produces<PageResponse<PurchaseRequisitionResponse>>();

        requisitions.MapGet("/{id:guid}", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetPurchaseRequisitionAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("GetPurchaseRequisition")
            .WithSummary("Get a purchase requisition and its lines")
            .Produces<PurchaseRequisitionResponse>()
            .Produces(StatusCodes.Status404NotFound);

        requisitions.MapPost("", async (
            HttpContext context, PurchaseRequisitionRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreatePurchaseRequisitionAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id
                ? $"/api/v1/purchase/requisitions/{id}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.create"))
            .WithName("CreatePurchaseRequisition")
            .WithSummary("Create a draft purchase requisition")
            .Produces<PurchaseRequisitionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        requisitions.MapPut("/{id:guid}/draft", async (
            Guid id, HttpContext context, PurchaseRequisitionRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.UpdatePurchaseRequisitionDraftAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.update"))
            .WithName("UpdatePurchaseRequisitionDraft")
            .WithSummary("Update an unapproved requisition draft")
            .Produces<PurchaseRequisitionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/submit", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.SubmitPurchaseRequisitionAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.submit"))
            .WithName("SubmitPurchaseRequisition")
            .WithSummary("Submit a requisition for approval")
            .Produces<PurchaseRequisitionResponse>()
            .Produces(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/approve", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ApprovePurchaseRequisitionAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.approve"))
            .WithName("ApprovePurchaseRequisition")
            .WithSummary("Approve a submitted purchase requisition")
            .Produces<PurchaseRequisitionResponse>()
            .Produces(StatusCodes.Status409Conflict);

        requisitions.MapPost("/{id:guid}/reject", async (
            Guid id, HttpContext context, RejectionRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.RejectPurchaseRequisitionAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.approve"))
            .WithName("RejectPurchaseRequisition")
            .WithSummary("Reject a submitted requisition with a reason")
            .Produces<PurchaseRequisitionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        requisitions.MapGet("/{id:guid}/history", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var item = await service.GetPurchaseRequisitionAsync(
                CompanyId(context.User), id, cancellationToken);
            return item.StatusCode == StatusCodes.Status404NotFound
                ? Results.NotFound()
                : Results.Ok(await service.PurchaseRequisitionHistoryAsync(
                    CompanyId(context.User), id, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("GetPurchaseRequisitionHistory")
            .WithSummary("Get the audit history of a purchase requisition")
            .Produces<IReadOnlyList<TransactionHistoryResponse>>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapPurchaseOrders(WebApplication app)
    {
        var orders = app.MapGroup("/api/v1/purchase/orders");

        orders.MapGet("", async (
            HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var (page, pageSize, search, errors) = ParseQuery(context.Request);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            return Results.Ok(await service.ListPurchaseOrdersAsync(
                CompanyId(context.User), search, page, pageSize, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("ListPurchaseOrders")
            .WithSummary("Search and page purchase orders")
            .Produces<PageResponse<PurchaseOrderResponse>>();

        orders.MapGet("/{id:guid}", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetPurchaseOrderAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("GetPurchaseOrder")
            .WithSummary("Get a purchase order with received and outstanding quantities")
            .Produces<PurchaseOrderResponse>()
            .Produces(StatusCodes.Status404NotFound);

        orders.MapPost("", async (
            HttpContext context, PurchaseOrderRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreatePurchaseOrderAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id
                ? $"/api/v1/purchase/orders/{id}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.create"))
            .WithName("CreatePurchaseOrder")
            .WithSummary("Create a draft purchase order for an active supplier")
            .Produces<PurchaseOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        orders.MapPut("/{id:guid}/draft", async (
            Guid id, HttpContext context, PurchaseOrderRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.UpdatePurchaseOrderDraftAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.update"))
            .WithName("UpdatePurchaseOrderDraft")
            .WithSummary("Update an unissued purchase-order draft")
            .Produces<PurchaseOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/submit", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.SubmitPurchaseOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.submit"))
            .WithName("SubmitPurchaseOrder")
            .WithSummary("Submit a purchase order for approval")
            .Produces<PurchaseOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/approve", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ApprovePurchaseOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.approve"))
            .WithName("ApprovePurchaseOrder")
            .WithSummary("Approve a submitted purchase order")
            .Produces<PurchaseOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/reject", async (
            Guid id, HttpContext context, RejectionRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.RejectPurchaseOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.approve"))
            .WithName("RejectPurchaseOrder")
            .WithSummary("Reject a submitted purchase order with a reason")
            .Produces<PurchaseOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/issue", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.IssuePurchaseOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.issue"))
            .WithName("IssuePurchaseOrder")
            .WithSummary("Issue an approved purchase order to its supplier")
            .Produces<PurchaseOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/amend", async (
            Guid id, HttpContext context, PurchaseOrderRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
            ToResult(await service.AmendPurchaseOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.amend"))
            .WithName("AmendPurchaseOrder")
            .WithSummary("Create a numbered, auditable amendment for an issued purchase order")
            .Produces<PurchaseOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/receipts", async (
            Guid id, HttpContext context, GoodsReceiptRequest request,
            ISalesPurchaseService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ReceivePurchaseOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } receiptId
                ? $"/api/v1/purchase/receipts/{receiptId}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.receive"))
            .WithName("ReceivePurchaseOrder")
            .WithSummary("Record partial goods receipt against an issued purchase-order line")
            .Produces<GoodsReceiptResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        orders.MapGet("/{id:guid}/history", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
        {
            var order = await service.GetPurchaseOrderAsync(
                CompanyId(context.User), id, cancellationToken);
            return order.StatusCode == StatusCodes.Status404NotFound
                ? Results.NotFound()
                : Results.Ok(await service.PurchaseOrderHistoryAsync(
                    CompanyId(context.User), id, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("GetPurchaseOrderHistory")
            .WithSummary("Get the audit history of a purchase order")
            .Produces<IReadOnlyList<TransactionHistoryResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/purchase/receipts/{id:guid}", async (
            Guid id, HttpContext context, ISalesPurchaseService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetGoodsReceiptAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("purchase.read"))
            .WithName("GetGoodsReceipt")
            .WithSummary("Get a receipt and its purchase-order reference")
            .Produces<GoodsReceiptResponse>()
            .Produces(StatusCodes.Status404NotFound);
    }

    private static void MapManufacturing(WebApplication app)
    {
        var inventory = app.MapGroup("/api/v1/inventory");
        inventory.MapGet("/balances", async (
            HttpContext context,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var itemId = ParseOptionalGuid("itemId");
            var warehouseId = ParseOptionalGuid("warehouseId");
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            return Results.Ok(await service.GetInventoryBalancesAsync(
                CompanyId(context.User), itemId, warehouseId, cancellationToken));

            Guid? ParseOptionalGuid(string name)
            {
                if (!context.Request.Query.TryGetValue(name, out var value))
                {
                    return null;
                }

                if (Guid.TryParse(value, out var parsed))
                {
                    return parsed;
                }

                errors[name] = [$"{name} must be a valid GUID."];
                return null;
            }
        })
            .RequireAuthorization(PermissionPolicies.For("inventory.read"))
            .WithName("GetInventoryBalances")
            .WithSummary("List inventory balances by item and warehouse")
            .Produces<IReadOnlyList<InventoryBalanceResponse>>()
            .ProducesValidationProblem();

        inventory.MapPost("/adjustments", async (
            HttpContext context,
            InventoryAdjustmentRequest request,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.AdjustInventoryAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("inventory.adjust"))
            .WithName("AdjustInventory")
            .WithSummary("Post an auditable inventory adjustment")
            .Produces<InventoryBalanceResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        app.MapPost("/api/v1/planning/mrp/requirements", async (
            HttpContext context,
            MrpRequest request,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.RunMrpAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("planning.read"))
            .WithName("CalculateMaterialRequirements")
            .WithSummary("Calculate time-phased material requirements for confirmed sales")
            .Produces<MrpRunResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        var workOrders = app.MapGroup("/api/v1/production/work-orders");
        workOrders.MapGet("", async (
            HttpContext context,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
        {
            var (page, pageSize, search, errors) = ParseQuery(context.Request);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            return Results.Ok(await service.ListWorkOrdersAsync(
                CompanyId(context.User), page, pageSize, search, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("workorders.read"))
            .WithName("ListWorkOrders")
            .WithSummary("Search and page Work Orders")
            .Produces<PageResponse<WorkOrderResponse>>()
            .ProducesValidationProblem();

        workOrders.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkOrderAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.read"))
            .WithName("GetWorkOrder")
            .WithSummary("Get a Work Order with BOM snapshots, materials, and operations")
            .Produces<WorkOrderResponse>()
            .Produces(StatusCodes.Status404NotFound);

        workOrders.MapPost("", async (
            HttpContext context,
            WorkOrderRequest request,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateWorkOrderAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id
                ? $"/api/v1/production/work-orders/{id}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For("workorders.create"))
            .WithName("CreateWorkOrder")
            .WithSummary("Create a draft Work Order from a confirmed Sales Order and approved BOM")
            .Produces<WorkOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapGet("/{id:guid}/materials", async (
            Guid id,
            HttpContext context,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkOrderMaterialsAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.read"))
            .WithName("GetWorkOrderMaterialAvailability")
            .WithSummary("Check material readiness for a Work Order")
            .Produces<WorkOrderMaterialAvailabilityResponse>()
            .Produces(StatusCodes.Status404NotFound);

        workOrders.MapGet("/{id:guid}/history", async (
            Guid id,
            HttpContext context,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
        {
            var workOrder = await service.GetWorkOrderAsync(
                CompanyId(context.User), id, cancellationToken);
            return workOrder.StatusCode == StatusCodes.Status404NotFound
                ? Results.NotFound()
                : Results.Ok(await service.GetWorkOrderHistoryAsync(
                    CompanyId(context.User), id, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("workorders.read"))
            .WithName("GetWorkOrderHistory")
            .WithSummary("Get the audit history of a Work Order")
            .Produces<IReadOnlyList<TransactionHistoryResponse>>()
            .Produces(StatusCodes.Status404NotFound);

        workOrders.MapPost("/{id:guid}/submit", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.SubmitWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.submit"))
            .WithName("SubmitWorkOrder")
            .WithSummary("Submit a draft Work Order for approval")
            .Produces<WorkOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/approve", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ApproveWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.approve"))
            .WithName("ApproveWorkOrder")
            .WithSummary("Approve a submitted Work Order")
            .Produces<WorkOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/reject", async (
            Guid id, HttpContext context, WorkOrderReasonRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.RejectWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.approve"))
            .WithName("RejectWorkOrder")
            .WithSummary("Reject a submitted Work Order with a reason")
            .Produces<WorkOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/release", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ReleaseWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.release"))
            .WithName("ReleaseWorkOrder")
            .WithSummary("Validate prerequisites and reserve materials before release")
            .Produces<WorkOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/hold", async (
            Guid id, HttpContext context, WorkOrderReasonRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.HoldWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.hold"))
            .WithName("HoldWorkOrder")
            .WithSummary("Place a released Work Order on hold")
            .Produces<WorkOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/operations/{operationId:guid}/complete", async (
            Guid id, Guid operationId, HttpContext context,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.CompleteWorkOrderOperationAsync(
                CompanyId(context.User), Actor(context), id, operationId,
                IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.complete"))
            .WithName("CompleteWorkOrderOperation")
            .WithSummary("Complete the next eligible production operation")
            .Produces<WorkOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/complete", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.CompleteWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.complete"))
            .WithName("CompleteWorkOrder")
            .WithSummary("Consume reserved materials and post completed goods to inventory")
            .Produces<WorkOrderResponse>()
            .Produces(StatusCodes.Status409Conflict);

        workOrders.MapPost("/{id:guid}/cancel", async (
            Guid id, HttpContext context, WorkOrderReasonRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.CancelWorkOrderAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("workorders.cancel"))
            .WithName("CancelWorkOrder")
            .WithSummary("Cancel a Work Order and release its material reservations")
            .Produces<WorkOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        MapProductionExecution(app);
    }

    private static void MapProductionExecution(WebApplication app)
    {
        var plans = app.MapGroup("/api/v1/planning/production/orders");
        plans.MapGet("", async (
            HttpContext context,
            IManufacturingService service,
            CancellationToken cancellationToken) =>
        {
            var (page, pageSize, _, errors) = ParseQuery(context.Request);
            return errors is not null
                ? Results.ValidationProblem(errors)
                : Results.Ok(await service.ListProductionPlansAsync(
                    CompanyId(context.User), page, pageSize, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("planning.read"))
            .WithName("ListProductionPlans")
            .WithSummary("List planned Work Orders with schedule risk and output progress")
            .Produces<PageResponse<ProductionPlanResponse>>()
            .ProducesValidationProblem();

        plans.MapPut("/{id:guid}", async (
            Guid id, HttpContext context, ProductionPlanRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.UpdateProductionPlanAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("planning.schedule"))
            .WithName("UpdateProductionPlan")
            .WithSummary("Assign operation targets, lines, machines, teams, priority, and schedule")
            .Produces<ProductionPlanResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        var jobCards = app.MapGroup("/api/v1/production/job-cards");
        jobCards.MapGet("", async (
            HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
        {
            var (page, pageSize, _, errors) = ParseQuery(context.Request);
            Guid? workOrderId = null;
            if (context.Request.Query.TryGetValue("workOrderId", out var value))
            {
                if (!Guid.TryParse(value, out var parsed))
                {
                    errors ??= new Dictionary<string, string[]>(StringComparer.Ordinal);
                    errors["workOrderId"] = ["workOrderId must be a valid GUID."];
                }
                else
                {
                    workOrderId = parsed;
                }
            }

            return errors is not null
                ? Results.ValidationProblem(errors)
                : Results.Ok(await service.ListJobCardsAsync(
                    CompanyId(context.User), page, pageSize, workOrderId, cancellationToken));
        })
            .RequireAuthorization(PermissionPolicies.For("jobcards.read"))
            .WithName("ListJobCards")
            .WithSummary("List generated Job Cards and their progress")
            .Produces<PageResponse<JobCardResponse>>()
            .ProducesValidationProblem();

        app.MapPost("/api/v1/production/work-orders/{id:guid}/job-cards", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GenerateJobCardsAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobcards.assign"))
            .WithName("GenerateJobCards")
            .WithSummary("Generate one Job Card per operation from an approved Work Order")
            .Produces<IReadOnlyList<JobCardResponse>>()
            .Produces(StatusCodes.Status409Conflict);

        jobCards.MapPost("/{id:guid}/start", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ChangeJobCardStatusAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                "start", null, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobcards.assign"))
            .WithName("StartJobCard")
            .WithSummary("Start an eligible Job Card after earlier operations are complete")
            .Produces<JobCardResponse>()
            .Produces(StatusCodes.Status409Conflict);

        jobCards.MapPost("/{id:guid}/hold", async (
            Guid id, HttpContext context, WorkOrderReasonRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.ChangeJobCardStatusAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                "hold", request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobcards.assign"))
            .WithName("HoldJobCard")
            .WithSummary("Place an in-progress Job Card on hold with a reason")
            .Produces<JobCardResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        jobCards.MapPost("/{id:guid}/resume", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ChangeJobCardStatusAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                "resume", null, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobcards.assign"))
            .WithName("ResumeJobCard")
            .WithSummary("Resume a Job Card that is on hold")
            .Produces<JobCardResponse>()
            .Produces(StatusCodes.Status409Conflict);

        jobCards.MapGet("/{id:guid}/confirmations", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListProductionConfirmationsAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobcards.read"))
            .WithName("ListProductionConfirmations")
            .WithSummary("List production confirmations and their correction history")
            .Produces<IReadOnlyList<ProductionConfirmationResponse>>();

        jobCards.MapPost("/{id:guid}/confirmations", async (
            Guid id, HttpContext context, ProductionConfirmationRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateProductionConfirmationAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } confirmationId
                ? $"/api/v1/production/confirmations/{confirmationId}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For("jobcards.confirm"))
            .WithName("CreateProductionConfirmation")
            .WithSummary("Submit classified production quantities for supervisor approval")
            .Produces<ProductionConfirmationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        var confirmations = app.MapGroup("/api/v1/production/confirmations");
        confirmations.MapGet("/{id:guid}", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetProductionConfirmationAsync(
                CompanyId(context.User), id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("production.read"))
            .WithName("GetProductionConfirmation")
            .WithSummary("Get a production confirmation by ID")
            .Produces<ProductionConfirmationResponse>()
            .Produces(StatusCodes.Status404NotFound);

        confirmations.MapPost("/{id:guid}/approve", async (
            Guid id, HttpContext context, IManufacturingService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ApproveProductionConfirmationAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.approve"))
            .WithName("ApproveProductionConfirmation")
            .WithSummary("Approve a confirmation and post only its accepted good quantity to inventory")
            .Produces<ProductionConfirmationResponse>()
            .Produces(StatusCodes.Status409Conflict);

        confirmations.MapPost("/{id:guid}/reject", async (
            Guid id, HttpContext context, WorkOrderReasonRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
            ToResult(await service.RejectProductionConfirmationAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.approve"))
            .WithName("RejectProductionConfirmation")
            .WithSummary("Reject a pending confirmation with a reason")
            .Produces<ProductionConfirmationResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        confirmations.MapPost("/{id:guid}/corrections", async (
            Guid id, HttpContext context, ProductionConfirmationCorrectionRequest request,
            IManufacturingService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CorrectProductionConfirmationAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context),
                request, cancellationToken);
            return ToResult(result, result.ResourceId is { } correctionId
                ? $"/api/v1/production/confirmations/{correctionId}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For("production.correct"))
            .WithName("CorrectProductionConfirmation")
            .WithSummary("Submit an auditable replacement for supervisor approval")
            .Produces<ProductionConfirmationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
    }

    private static void MapQualityJobWorkMaintenance(WebApplication app)
    {
        var quality = app.MapGroup("/api/v1/quality");
        quality.MapGet("/inspections", async (
            HttpContext context, string? stage, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListInspectionsAsync(
                CompanyId(context.User), stage, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.read"));
        quality.MapGet("/defect-codes", async (
            HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListDefectCodesAsync(
                CompanyId(context.User), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.read"));
        quality.MapPost("/inspections", async (
            HttpContext context, QualityInspectionRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateInspectionAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id ? $"/api/v1/quality/inspections/{id}" : null);
        })
            .RequireAuthorization(PermissionPolicies.For("quality.inspect"))
            .Produces<QualityInspectionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        quality.MapPost("/inspections/{id:guid}/approve", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.ApproveInspectionAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.approve"))
            .Produces<QualityInspectionResponse>()
            .Produces(StatusCodes.Status409Conflict);
        quality.MapGet("/inspections/{id:guid}/history", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkflowHistoryAsync(
                CompanyId(context.User), "quality-inspection", id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.read"));
        quality.MapPost("/defect-codes", async (
            HttpContext context, DefectCodeRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.CreateDefectCodeAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("quality.inspect"))
            .Produces<bool>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        var jobWork = app.MapGroup("/api/v1/job-work");
        jobWork.MapPost("/subcontractors", async (
            HttpContext context, SubcontractorRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.RegisterSubcontractorAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobwork.create"))
            .Produces<Guid>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        jobWork.MapGet("/orders", async (
            HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListJobWorkOrdersAsync(
                CompanyId(context.User), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobwork.read"));
        jobWork.MapGet("/orders/{id:guid}/history", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkflowHistoryAsync(
                CompanyId(context.User), "job-work-order", id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobwork.read"));
        jobWork.MapPost("/orders", async (
            HttpContext context, JobWorkOrderRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateJobWorkOrderAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id ? $"/api/v1/job-work/orders/{id}" : null);
        })
            .RequireAuthorization(PermissionPolicies.For("jobwork.create"))
            .Produces<JobWorkOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        jobWork.MapPost("/orders/{id:guid}/dispatch", async (
            Guid id, HttpContext context, IReadOnlyList<JobWorkDispatchRequest> request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.DispatchJobWorkAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobwork.dispatch"))
            .Produces<JobWorkOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        jobWork.MapPost("/orders/{id:guid}/receipts", async (
            Guid id, HttpContext context, JobWorkReceiptRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.ReceiveJobWorkAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobwork.receive"))
            .Produces<JobWorkOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        jobWork.MapPost("/orders/{id:guid}/reconcile", async (
            Guid id, HttpContext context, JobWorkMaterialReturnRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.ReconcileJobWorkMaterialAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("jobwork.reconcile"))
            .Produces<JobWorkOrderResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        var maintenance = app.MapGroup("/api/v1/maintenance");
        maintenance.MapGet("/requests", async (
            HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListMaintenanceRequestsAsync(
                CompanyId(context.User), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.read"));
        maintenance.MapGet("/requests/{id:guid}/history", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkflowHistoryAsync(
                CompanyId(context.User), "maintenance-request", id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.read"));
        maintenance.MapGet("/job-cards/{id:guid}/history", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkflowHistoryAsync(
                CompanyId(context.User), "maintenance-job-card", id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.read"));
        maintenance.MapPost("/requests", async (
            HttpContext context, MaintenanceRequestRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateMaintenanceRequestAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id ? $"/api/v1/maintenance/requests/{id}" : null);
        })
            .RequireAuthorization(PermissionPolicies.For("maintenance.create"))
            .Produces<MaintenanceRequestResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        maintenance.MapPost("/job-cards/{id:guid}/complete", async (
            Guid id, HttpContext context, MaintenanceCompletionRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.CompleteMaintenanceCardAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.complete"))
            .Produces<MaintenanceRequestResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        maintenance.MapPost("/job-cards/{id:guid}/spares", async (
            Guid id, HttpContext context, MaintenanceSpareIssueRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.IssueMaintenanceSpareAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.update"))
            .Produces<bool>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        maintenance.MapPost("/preventive-schedules", async (
            HttpContext context, PreventiveMaintenanceRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
            ToResult(await service.CreatePreventiveScheduleAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.create"))
            .Produces<bool>(StatusCodes.Status201Created)
            .ProducesValidationProblem();
        maintenance.MapGet("/downtime", async (
            HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListDowntimeAsync(CompanyId(context.User), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.read"));
        maintenance.MapGet("/downtime/{id:guid}/history", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.GetWorkflowHistoryAsync(
                CompanyId(context.User), "machine-downtime", id, cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.read"));
        maintenance.MapPost("/downtime", async (
            HttpContext context, DowntimeRequest request,
            IQualityJobWorkMaintenanceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.StartDowntimeAsync(
                CompanyId(context.User), Actor(context), IdempotencyKey(context), request, cancellationToken);
            return ToResult(result, result.ResourceId is { } id ? $"/api/v1/maintenance/downtime/{id}" : null);
        })
            .RequireAuthorization(PermissionPolicies.For("maintenance.create"))
            .Produces<MachineDowntimeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);
        maintenance.MapPost("/downtime/{id:guid}/end", async (
            Guid id, HttpContext context, IQualityJobWorkMaintenanceService service,
            CancellationToken cancellationToken) =>
            ToResult(await service.EndDowntimeAsync(
                CompanyId(context.User), Actor(context), id, IdempotencyKey(context), cancellationToken)))
            .RequireAuthorization(PermissionPolicies.For("maintenance.update"))
            .Produces<MachineDowntimeResponse>()
            .Produces(StatusCodes.Status409Conflict);
    }

    private static IResult ToResult<T>(MasterDataResult<T> result, string? location = null)
    {
        if (result.ValidationErrors is not null)
        {
            return Results.ValidationProblem(result.ValidationErrors);
        }

        return result.StatusCode switch
        {
            StatusCodes.Status201Created => Results.Created(location ?? string.Empty, result.Value),
            StatusCodes.Status200OK => Results.Ok(result.Value),
            StatusCodes.Status404NotFound => Results.NotFound(),
            StatusCodes.Status409Conflict => Results.Conflict(new { error = result.Error }),
            _ => Results.StatusCode(result.StatusCode),
        };
    }

    private static (int Page, int PageSize, string? Search, Dictionary<string, string[]>? Errors)
        ParseQuery(HttpRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var page = Parse("page", 1, 1, int.MaxValue);
        var pageSize = Parse("pageSize", 25, 1, 100);
        var search = request.Query["search"].ToString();
        if (search.Length > 200)
        {
            errors["search"] = ["Search text cannot exceed 200 characters."];
        }

        return (page, pageSize, search, errors.Count == 0 ? null : errors);

        int Parse(string name, int fallback, int minimum, int maximum)
        {
            if (!request.Query.TryGetValue(name, out var value))
            {
                return fallback;
            }

            if (int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum)
            {
                return parsed;
            }

            errors[name] = [$"{name} must be between {minimum} and {maximum}."];
            return fallback;
        }
    }

    private static Guid CompanyId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(SecurityClaims.CompanyId)!);

    private static MasterDataActor Actor(HttpContext context) =>
        new(
            Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            context.User.FindFirstValue(ClaimTypes.Name)
                ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "unknown",
            context.TraceIdentifier);

    private static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers["Idempotency-Key"].ToString();
}
