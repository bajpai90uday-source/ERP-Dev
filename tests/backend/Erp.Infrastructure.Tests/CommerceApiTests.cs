using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Erp.Infrastructure.Tests;

public sealed class CommerceApiTests : IAsyncLifetime
{
    private const string BootstrapToken = "test-bootstrap-token-with-at-least-32-characters";
    private const string AdministratorEmail = "commerce-admin@example.test";
    private const string AdministratorPassword = "Manufacturing-admin-Password-01!";
    private const string ApproverEmail = "commerce-approver@example.test";
    private const string ApproverPassword = "Commerce-approver-Password-02!";

    private readonly ErpApiFactory factory = new();
    private HttpClient client = null!;
    private HttpClient approverClient = null!;
    private Guid companyId;

    public async Task InitializeAsync()
    {
        await factory.InitializeDatabaseAsync();
        client = CreateClient();
        companyId = await BootstrapAsync();
        Assert.Equal(HttpStatusCode.OK,
            (await LoginAsync(client, AdministratorEmail, AdministratorPassword, companyId)).StatusCode);
        approverClient = CreateClient();
        await CreateAdministratorAsync();
        Assert.Equal(HttpStatusCode.OK,
            (await LoginAsync(approverClient, ApproverEmail, ApproverPassword, companyId)).StatusCode);
    }

    public async Task DisposeAsync()
    {
        approverClient.Dispose();
        client.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task PurchaseWorkflowEnforcesRequisitionCommitmentsAndTracksReceipts()
    {
        var master = await CreatePurchaseMasterDataAsync();
        var requisition = new
        {
            lines = new[]
            {
                new
                {
                    itemId = master.ItemId,
                    unitOfMeasureId = master.UnitId,
                    quantity = 10m,
                    requiredByDate = "2030-01-01",
                },
            },
        };
        using var createdRequisition = await PostWithCsrfAsync(
            client, "/api/v1/purchase/requisitions", requisition, "requisition-create");
        Assert.Equal(HttpStatusCode.Created, createdRequisition.StatusCode);
        using var requisitionJson = JsonDocument.Parse(
            await createdRequisition.Content.ReadAsStringAsync());
        var requisitionId = requisitionJson.RootElement.GetProperty("id").GetGuid();
        var requisitionLineId = requisitionJson.RootElement
            .GetProperty("lines")[0].GetProperty("id").GetGuid();

        using var submittedRequisition = await PostWithCsrfAsync(
            client, $"/api/v1/purchase/requisitions/{requisitionId}/submit", new { }, "requisition-submit");
        Assert.Equal(HttpStatusCode.OK, submittedRequisition.StatusCode);
        using var selfApproval = await PostWithCsrfAsync(
            client, $"/api/v1/purchase/requisitions/{requisitionId}/approve", new { }, "requisition-self-approve");
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);
        using var approvedRequisition = await PostWithCsrfAsync(
            approverClient, $"/api/v1/purchase/requisitions/{requisitionId}/approve", new { }, "requisition-approve");
        Assert.Equal(HttpStatusCode.OK, approvedRequisition.StatusCode);

        var purchaseOrder = new
        {
            supplierId = master.SupplierId,
            currencyCode = "USD",
            purchaseRequisitionId = requisitionId,
            lines = new[]
            {
                new
                {
                    purchaseRequisitionLineId = requisitionLineId,
                    itemId = master.ItemId,
                    unitOfMeasureId = master.UnitId,
                    quantity = 6m,
                    unitPrice = 2m,
                    expectedDeliveryDate = "2030-02-01",
                },
            },
        };
        using var createdOrder = await PostWithCsrfAsync(
            client, "/api/v1/purchase/orders", purchaseOrder, "purchase-order-create");
        Assert.Equal(HttpStatusCode.Created, createdOrder.StatusCode);
        using var orderJson = JsonDocument.Parse(await createdOrder.Content.ReadAsStringAsync());
        var orderId = orderJson.RootElement.GetProperty("id").GetGuid();
        var lineId = orderJson.RootElement.GetProperty("lines")[0].GetProperty("lineId").GetGuid();

        using var overCommitment = await PostWithCsrfAsync(client, "/api/v1/purchase/orders",
            new
            {
                supplierId = master.SupplierId,
                currencyCode = "USD",
                purchaseRequisitionId = requisitionId,
                lines = new[]
                {
                    new
                    {
                        purchaseRequisitionLineId = requisitionLineId,
                        itemId = master.ItemId,
                        unitOfMeasureId = master.UnitId,
                        quantity = 5m,
                        unitPrice = 2m,
                        expectedDeliveryDate = "2030-02-01",
                    },
                },
            }, "purchase-order-over-commit");
        Assert.Equal(HttpStatusCode.BadRequest, overCommitment.StatusCode);

        using var submittedOrder = await PostWithCsrfAsync(
            client, $"/api/v1/purchase/orders/{orderId}/submit", new { }, "purchase-order-submit");
        Assert.Equal(HttpStatusCode.OK, submittedOrder.StatusCode);
        using var approvedOrder = await PostWithCsrfAsync(
            approverClient, $"/api/v1/purchase/orders/{orderId}/approve", new { }, "purchase-order-approve");
        Assert.Equal(HttpStatusCode.OK, approvedOrder.StatusCode);
        using var issuedOrder = await PostWithCsrfAsync(
            client, $"/api/v1/purchase/orders/{orderId}/issue", new { }, "purchase-order-issue");
        Assert.Equal(HttpStatusCode.OK, issuedOrder.StatusCode);

        var partialReceiptRequest = new
        {
            supplierDeliveryReference = "delivery-001",
            lines = new[] { new { purchaseOrderLineId = lineId, quantity = 2m } },
        };
        using var receipt = await PostWithCsrfAsync(client,
            $"/api/v1/purchase/orders/{orderId}/receipts", partialReceiptRequest, "receipt-partial");
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        using var repeatedReceipt = await PostWithCsrfAsync(client,
            $"/api/v1/purchase/orders/{orderId}/receipts", partialReceiptRequest, "receipt-partial");
        Assert.Equal(HttpStatusCode.Created, repeatedReceipt.StatusCode);
        Assert.Equal(await receipt.Content.ReadAsStringAsync(),
            await repeatedReceipt.Content.ReadAsStringAsync());

        using var overReceipt = await PostWithCsrfAsync(client,
            $"/api/v1/purchase/orders/{orderId}/receipts",
            new
            {
                supplierDeliveryReference = "delivery-002",
                lines = new[] { new { purchaseOrderLineId = lineId, quantity = 5m } },
            }, "receipt-over");
        Assert.Equal(HttpStatusCode.BadRequest, overReceipt.StatusCode);

        using var getOrder = await client.GetAsync($"/api/v1/purchase/orders/{orderId}");
        getOrder.EnsureSuccessStatusCode();
        using var currentOrderJson = JsonDocument.Parse(await getOrder.Content.ReadAsStringAsync());
        var line = currentOrderJson.RootElement.GetProperty("lines")[0];
        Assert.Equal("partially_received", currentOrderJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(2m, line.GetProperty("receivedQuantity").GetDecimal());
        Assert.Equal(4m, line.GetProperty("outstandingQuantity").GetDecimal());

        using var changedSourceAmendment = await PostWithCsrfAsync(client,
            $"/api/v1/purchase/orders/{orderId}/amend",
            new
            {
                supplierId = master.SupplierId,
                currencyCode = "USD",
                purchaseRequisitionId = requisitionId,
                lines = new[]
                {
                    new
                    {
                        lineId,
                        itemId = master.ItemId,
                        unitOfMeasureId = master.UnitId,
                        quantity = 6m,
                        unitPrice = 2m,
                        expectedDeliveryDate = "2030-02-01",
                    },
                },
            }, "purchase-order-change-source");
        Assert.Equal(HttpStatusCode.BadRequest, changedSourceAmendment.StatusCode);

        using var history = await client.GetAsync(
            $"/api/v1/purchase/orders/{orderId}/history");
        history.EnsureSuccessStatusCode();
        using var historyJson = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Contains(historyJson.RootElement.EnumerateArray(),
            entry => entry.GetProperty("action").GetString() == "goods_received");
    }

    [Fact]
    public async Task SalesWorkflowRequiresIndependentApprovalAndRevisionsConfirmedOrders()
    {
        var master = await CreateSalesMasterDataAsync();
        var request = new
        {
            customerId = master.CustomerId,
            customerPurchaseOrderReference = "buyer-po-100",
            customerPurchaseOrderDate = "2030-01-01",
            currencyCode = "USD",
            lines = new[]
            {
                new
                {
                    productId = master.ProductId,
                    productVariantId = master.VariantId,
                    quantity = 5m,
                    unitPrice = 12m,
                    requestedDeliveryDate = "2030-02-01",
                },
            },
        };
        using var created = await PostWithCsrfAsync(
            client, "/api/v1/sales/orders", request, "sales-create");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var orderId = createdJson.RootElement.GetProperty("id").GetGuid();

        using var replay = await PostWithCsrfAsync(
            client, "/api/v1/sales/orders", request, "sales-create");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        using var keyReuse = await PostWithCsrfAsync(client, "/api/v1/sales/orders",
            new
            {
                customerId = master.CustomerId,
                customerPurchaseOrderReference = "buyer-po-100",
                customerPurchaseOrderDate = "2030-01-01",
                currencyCode = "USD",
                lines = new[]
                {
                    new
                    {
                        productId = master.ProductId,
                        productVariantId = master.VariantId,
                        quantity = 6m,
                        unitPrice = 12m,
                        requestedDeliveryDate = "2030-02-01",
                    },
                },
            }, "sales-create");
        Assert.Equal(HttpStatusCode.Conflict, keyReuse.StatusCode);

        using var submitted = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{orderId}/submit", new { }, "sales-submit");
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        using var selfApproval = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{orderId}/approve", new { }, "sales-self-approve");
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);
        using var approved = await PostWithCsrfAsync(
            approverClient, $"/api/v1/sales/orders/{orderId}/approve", new { }, "sales-approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        using var confirmed = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{orderId}/confirm", new { }, "sales-confirm");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        using var amendment = await PostWithCsrfAsync(client,
            $"/api/v1/sales/orders/{orderId}/amend",
            new
            {
                customerId = master.CustomerId,
                customerPurchaseOrderReference = "buyer-po-100",
                customerPurchaseOrderDate = "2030-01-01",
                currencyCode = "USD",
                lines = new[]
                {
                    new
                    {
                        productId = master.ProductId,
                        productVariantId = master.VariantId,
                        quantity = 7m,
                        unitPrice = 12m,
                        requestedDeliveryDate = "2030-02-01",
                    },
                },
            }, "sales-amend");
        Assert.Equal(HttpStatusCode.OK, amendment.StatusCode);
        using var amendmentJson = JsonDocument.Parse(await amendment.Content.ReadAsStringAsync());
        Assert.Equal(2, amendmentJson.RootElement.GetProperty("revisionNumber").GetInt32());
        Assert.Equal("draft", amendmentJson.RootElement.GetProperty("status").GetString());
        var amendedLineId = amendmentJson.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid();

        using var resubmitted = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{orderId}/submit", new { }, "sales-resubmit");
        Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
        using var reapproved = await PostWithCsrfAsync(
            approverClient, $"/api/v1/sales/orders/{orderId}/approve", new { }, "sales-reapprove");
        Assert.Equal(HttpStatusCode.OK, reapproved.StatusCode);
        using var reconfirmed = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{orderId}/confirm", new { }, "sales-reconfirm");
        Assert.Equal(HttpStatusCode.OK, reconfirmed.StatusCode);

        using var fulfillment = await PostWithCsrfAsync(client,
            $"/api/v1/sales/orders/{orderId}/fulfillments",
            new
            {
                reference = "shipment-001",
                lines = new[] { new { salesOrderLineId = amendedLineId, quantity = 3m } },
            }, "sales-fulfillment");
        Assert.Equal(HttpStatusCode.OK, fulfillment.StatusCode);

        using var overFulfillment = await PostWithCsrfAsync(client,
            $"/api/v1/sales/orders/{orderId}/fulfillments",
            new
            {
                reference = "shipment-002",
                lines = new[] { new { salesOrderLineId = amendedLineId, quantity = 5m } },
            }, "sales-over-fulfillment");
        Assert.Equal(HttpStatusCode.BadRequest, overFulfillment.StatusCode);
    }

    private async Task<(Guid SupplierId, Guid ItemId, Guid UnitId)> CreatePurchaseMasterDataAsync()
    {
        var supplier = await CreateMasterAsync("/api/v1/master-data/suppliers", new
        {
            code = "SUP-TEST",
            name = "Commerce Supplier",
        });
        var unit = await CreateMasterAsync("/api/v1/master-data/units", new
        {
            code = "EA-TEST",
            name = "Each",
            dimension = "count",
            decimalPlaces = 2,
        });
        var category = await CreateMasterAsync("/api/v1/master-data/item-categories", new
        {
            code = "GEN-TEST",
            name = "General Goods",
            trackingMode = "none",
            categoryType = "general",
        });
        var item = await CreateMasterAsync("/api/v1/master-data/items", new
        {
            name = "Test Component",
            categoryId = category,
            stockUnitId = unit,
            purchaseUnitId = unit,
            purchaseToStockFactor = 1m,
        });
        return (supplier, item, unit);
    }

    private async Task<(Guid CustomerId, Guid ProductId, Guid VariantId)> CreateSalesMasterDataAsync()
    {
        var customer = await CreateMasterAsync("/api/v1/master-data/customers", new
        {
            code = "BUY-TEST",
            name = "Commerce Buyer",
        });
        var unit = await CreateMasterAsync("/api/v1/master-data/units", new
        {
            code = "EA-SALES",
            name = "Sales Each",
            dimension = "count",
            decimalPlaces = 2,
        });
        var category = await CreateMasterAsync("/api/v1/master-data/item-categories", new
        {
            code = "SALE-TEST",
            name = "Sales Goods",
            trackingMode = "none",
            categoryType = "finished_good",
        });
        var item = await CreateMasterAsync("/api/v1/master-data/items", new
        {
            name = "Test Finished Good",
            categoryId = category,
            stockUnitId = unit,
            purchaseUnitId = unit,
            purchaseToStockFactor = 1m,
        });
        var product = await CreateMasterAsync("/api/v1/master-data/products", new
        {
            code = "PRODUCT-TEST",
            name = "Test Product",
            industryCode = "apparel",
        });
        var variant = await CreateMasterAsync("/api/v1/master-data/variants", new
        {
            productId = product,
            itemId = item,
            code = "PRODUCT-TEST-S",
            name = "Small",
            sizeCode = "S",
            colorCode = "BLK",
            colorName = "Black",
        });
        return (customer, product, variant);
    }

    private async Task<Guid> CreateMasterAsync(string path, object body)
    {
        using var response = await PostWithCsrfAsync(client, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> BootstrapAsync()
    {
        using var response = await PostWithCsrfAsync(client, "/api/v1/setup/bootstrap", new
        {
            companyCode = "COMM",
            companyName = "Commerce Test Company",
            adminEmail = AdministratorEmail,
            adminDisplayName = "Commerce Administrator",
            adminPassword = AdministratorPassword,
        }, additionalHeaders: new Dictionary<string, string>
        {
            ["X-ERP-Bootstrap-Token"] = BootstrapToken,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("companyId").GetGuid();
    }

    private async Task CreateAdministratorAsync()
    {
        using var roles = await client.GetAsync("/api/v1/admin/roles");
        roles.EnsureSuccessStatusCode();
        using var rolesJson = JsonDocument.Parse(await roles.Content.ReadAsStringAsync());
        var adminRole = rolesJson.RootElement.EnumerateArray()
            .Single(role => role.GetProperty("code").GetString() == "ADMINISTRATION")
            .GetProperty("id").GetGuid();
        using var response = await PostWithCsrfAsync(client, "/api/v1/admin/users", new
        {
            email = ApproverEmail,
            displayName = "Commerce Approver",
            password = ApproverPassword,
            roleIds = new[] { adminRole },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient target, string email, string password, Guid targetCompanyId) =>
        await PostWithCsrfAsync(target, "/api/v1/auth/login", new
        {
            email,
            password,
            companyId = targetCompanyId,
        });

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient target,
        string path,
        object body,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, string>? additionalHeaders = null)
    {
        using var tokenResponse = await target.GetAsync("/api/v1/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(
            "X-ERP-CSRF",
            tokenJson.RootElement.GetProperty("requestToken").GetString()!);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (additionalHeaders is not null)
        {
            foreach (var header in additionalHeaders)
            {
                request.Headers.Add(header.Key, header.Value);
            }
        }

        return await target.SendAsync(request);
    }
}
