using System.Security.Claims;
using Erp.Api.MasterData;
using Erp.Api.Security;

namespace Erp.Api.Commerce;

internal static class CommerceEndpoints
{
    public static void MapCommerceEndpoints(this WebApplication app)
    {
        MapSalesOrders(app);
        MapPurchaseRequisitions(app);
        MapPurchaseOrders(app);
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
