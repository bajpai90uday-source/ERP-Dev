using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Erp.Domain.MasterData;
using Erp.Domain.Products;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
            warehouseId = master.WarehouseId,
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
                warehouseId = master.WarehouseId,
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
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.InventoryStockBalances.Add(new InventoryStockBalance
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                ItemId = master.ItemId,
                WarehouseId = master.WarehouseId,
                StockStatus = InventoryStockStatuses.Available,
                OnHandQuantity = 6m,
            });
            await db.SaveChangesAsync();
        }
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
        Assert.Equal(HttpStatusCode.Conflict, fulfillment.StatusCode);

        var firstDispatchBody = new
        {
            salesOrderId = orderId,
            lines = new[]
            {
                new { salesOrderLineId = amendedLineId, warehouseId = master.WarehouseId, quantity = 3m },
            },
            cartons = new[]
            {
                new
                {
                    cartonNumber = "CARTON-001",
                    weight = 1.5m,
                    contents = new[] { new { salesOrderLineId = amendedLineId, quantity = 3m } },
                },
            },
        };
        using var planned = await PostWithCsrfAsync(client, "/api/v1/dispatch/shipments",
            firstDispatchBody, "sales-dispatch-plan-1");
        Assert.Equal(HttpStatusCode.Created, planned.StatusCode);
        using var plannedJson = JsonDocument.Parse(await planned.Content.ReadAsStringAsync());
        var shipmentId = plannedJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("S", plannedJson.RootElement.GetProperty("lines")[0]
            .GetProperty("sizeCode").GetString());
        Assert.Equal("BLK", plannedJson.RootElement.GetProperty("lines")[0]
            .GetProperty("colorCode").GetString());

        using var authorized = await PostWithCsrfAsync(client,
            $"/api/v1/dispatch/shipments/{shipmentId}/authorize", new { }, "sales-dispatch-authorize-1");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        using var dispatched = await PostWithCsrfAsync(client,
            $"/api/v1/dispatch/shipments/{shipmentId}/confirm", new { }, "sales-dispatch-confirm-1");
        Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);

        using var overFulfillment = await PostWithCsrfAsync(client,
            "/api/v1/dispatch/shipments",
            new
            {
                salesOrderId = orderId,
                lines = new[]
                {
                    new { salesOrderLineId = amendedLineId, warehouseId = master.WarehouseId, quantity = 5m },
                },
                cartons = new[]
                {
                    new
                    {
                        cartonNumber = "CARTON-002",
                        weight = 1m,
                        contents = new[] { new { salesOrderLineId = amendedLineId, quantity = 5m } },
                    },
                },
            }, "sales-dispatch-plan-over-order");
        Assert.Equal(HttpStatusCode.Conflict, overFulfillment.StatusCode);

        var finalDispatchBody = new
        {
            salesOrderId = orderId,
            lines = new[]
            {
                new { salesOrderLineId = amendedLineId, warehouseId = master.WarehouseId, quantity = 4m },
            },
            cartons = new[]
            {
                new
                {
                    cartonNumber = "CARTON-003",
                    weight = 1m,
                    contents = new[] { new { salesOrderLineId = amendedLineId, quantity = 4m } },
                },
            },
        };
        using var finalPlan = await PostWithCsrfAsync(client, "/api/v1/dispatch/shipments",
            finalDispatchBody, "sales-dispatch-plan-2");
        Assert.Equal(HttpStatusCode.Created, finalPlan.StatusCode);
        using var finalPlanJson = JsonDocument.Parse(await finalPlan.Content.ReadAsStringAsync());
        var finalShipmentId = finalPlanJson.RootElement.GetProperty("id").GetGuid();
        using var insufficientStock = await PostWithCsrfAsync(client,
            $"/api/v1/dispatch/shipments/{finalShipmentId}/authorize", new { },
            "sales-dispatch-authorize-insufficient-stock");
        Assert.Equal(HttpStatusCode.Conflict, insufficientStock.StatusCode);
        using (var replenishScope = factory.Services.CreateScope())
        {
            var db = replenishScope.ServiceProvider.GetRequiredService<ErpDbContext>();
            var balance = await db.InventoryStockBalances.SingleAsync(value =>
                value.CompanyId == companyId && value.ItemId == master.ItemId
                && value.WarehouseId == master.WarehouseId);
            balance.OnHandQuantity += 1m;
            await db.SaveChangesAsync();
        }
        using var finalAuthorized = await PostWithCsrfAsync(client,
            $"/api/v1/dispatch/shipments/{finalShipmentId}/authorize", new { }, "sales-dispatch-authorize-2");
        Assert.Equal(HttpStatusCode.OK, finalAuthorized.StatusCode);
        using var finalDispatched = await PostWithCsrfAsync(client,
            $"/api/v1/dispatch/shipments/{finalShipmentId}/confirm", new { }, "sales-dispatch-confirm-2");
        Assert.Equal(HttpStatusCode.OK, finalDispatched.StatusCode);

        using var report = await client.GetAsync("/api/v1/reports/operations");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        using var reportJson = JsonDocument.Parse(await report.Content.ReadAsStringAsync());
        Assert.Equal(7m, reportJson.RootElement.GetProperty("dispatchPerformance")
            .GetProperty("dispatchedQuantity").GetDecimal());
        Assert.Equal(2, reportJson.RootElement.GetProperty("dispatchPerformance")
            .GetProperty("shipmentStatuses").EnumerateArray()
            .Single(value => value.GetProperty("status").GetString() == "dispatched")
            .GetProperty("shipmentCount").GetInt32());
        Assert.Equal(2, reportJson.RootElement.GetProperty("inventoryLedger")
            .EnumerateArray()
            .Count(value => value.GetProperty("referenceType").GetString() == "dispatch-shipment"));
        using var notificationsResponse = await client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.OK, notificationsResponse.StatusCode);
        using var notificationsJson = JsonDocument.Parse(
            await notificationsResponse.Content.ReadAsStringAsync());
        var dispatchNotification = notificationsJson.RootElement.EnumerateArray()
            .First(value => value.GetProperty("title").GetString() == "Shipment dispatched");
        using var notificationRead = await PostWithCsrfAsync(client,
            $"/api/v1/notifications/{dispatchNotification.GetProperty("id").GetGuid()}/read",
            new { }, "dispatch-notification-read");
        Assert.Equal(HttpStatusCode.NoContent, notificationRead.StatusCode);
        using var scopeAfterDispatch = factory.Services.CreateScope();
        var finalDb = scopeAfterDispatch.ServiceProvider.GetRequiredService<ErpDbContext>();
        Assert.Equal(0m, await finalDb.InventoryStockBalances
            .Where(value => value.CompanyId == companyId && value.ItemId == master.ItemId
                && value.WarehouseId == master.WarehouseId)
            .Select(value => value.OnHandQuantity)
            .SingleAsync());
        Assert.Equal(7m, await finalDb.SalesOrderFulfillments
            .Where(value => value.CompanyId == companyId && value.SalesOrderLineId == amendedLineId)
            .SumAsync(value => value.Quantity));
        Assert.Equal(2, await finalDb.InventoryLedgerEntries
            .CountAsync(value => value.CompanyId == companyId
                && value.ReferenceType == "dispatch-shipment"));
    }

    [Fact]
    public async Task HrEmployeeDirectoryOmitsConfidentialFieldsFromOrdinaryReads()
    {
        using var departmentResponse = await PostWithCsrfAsync(client,
            "/api/v1/hr/departments", new { code = "OPS", name = "Operations" }, "hr-department-create");
        Assert.Equal(HttpStatusCode.Created, departmentResponse.StatusCode);
        using var departmentJson = JsonDocument.Parse(await departmentResponse.Content.ReadAsStringAsync());
        var departmentId = departmentJson.RootElement.GetProperty("id").GetGuid();

        using var designationResponse = await PostWithCsrfAsync(client,
            "/api/v1/hr/designations", new { code = "OPR", name = "Operator" }, "hr-designation-create");
        Assert.Equal(HttpStatusCode.Created, designationResponse.StatusCode);
        using var designationJson = JsonDocument.Parse(await designationResponse.Content.ReadAsStringAsync());
        var designationId = designationJson.RootElement.GetProperty("id").GetGuid();

        using var employeeResponse = await PostWithCsrfAsync(client,
            "/api/v1/hr/employees",
            new
            {
                employeeNumber = "EMP-001",
                firstName = "Test",
                lastName = "Operator",
                departmentId,
                designationId,
                hireDate = "2025-01-01",
                personalEmail = "private@example.test",
                phone = "+1-555-0100",
                birthDate = "1990-01-01",
            }, "hr-employee-create");
        Assert.Equal(HttpStatusCode.Created, employeeResponse.StatusCode);
        using var employeeJson = JsonDocument.Parse(await employeeResponse.Content.ReadAsStringAsync());
        var employeeId = employeeJson.RootElement.GetProperty("id").GetGuid();

        using var employeesResponse = await client.GetAsync("/api/v1/hr/employees");
        Assert.Equal(HttpStatusCode.OK, employeesResponse.StatusCode);
        using var employeesJson = JsonDocument.Parse(await employeesResponse.Content.ReadAsStringAsync());
        var employee = employeesJson.RootElement.EnumerateArray()
            .Single(value => value.GetProperty("id").GetGuid() == employeeId);
        Assert.False(employee.TryGetProperty("personalEmail", out _));
        Assert.False(employee.TryGetProperty("phone", out _));
        Assert.False(employee.TryGetProperty("birthDate", out _));

        using var rolesResponse = await client.GetAsync("/api/v1/admin/roles");
        rolesResponse.EnsureSuccessStatusCode();
        using var rolesJson = JsonDocument.Parse(await rolesResponse.Content.ReadAsStringAsync());
        var hrRoleId = rolesJson.RootElement.EnumerateArray()
            .Single(value => value.GetProperty("code").GetString() == "HR")
            .GetProperty("id").GetGuid();
        using var hrUserResponse = await PostWithCsrfAsync(client, "/api/v1/admin/users",
            new
            {
                email = "hr-user@example.test",
                displayName = "HR User",
                password = "Human-resources-Password-03!",
                roleIds = new[] { hrRoleId },
            }, "hr-user-create");
        Assert.Equal(HttpStatusCode.Created, hrUserResponse.StatusCode);
        using var hrClient = CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(hrClient, "hr-user@example.test",
            "Human-resources-Password-03!", companyId)).StatusCode);
        using var confidentialDenied = await hrClient.GetAsync(
            $"/api/v1/hr/employees/{employeeId}/confidential");
        Assert.Equal(HttpStatusCode.Forbidden, confidentialDenied.StatusCode);

        using var confidentialResponse = await approverClient.GetAsync(
            $"/api/v1/hr/employees/{employeeId}/confidential");
        Assert.Equal(HttpStatusCode.OK, confidentialResponse.StatusCode);
        using var confidentialJson = JsonDocument.Parse(
            await confidentialResponse.Content.ReadAsStringAsync());
        Assert.Equal("private@example.test",
            confidentialJson.RootElement.GetProperty("personalEmail").GetString());
    }

    [Fact]
    public async Task ManufacturingWorkflowPlansReservesConsumesAndProducesInventory()
    {
        var salesMaster = await CreateSalesMasterDataAsync();
        var purchaseMaster = await CreatePurchaseMasterDataAsync();
        var operationId = await CreateMasterAsync("/api/v1/master-data/operations", new
        {
            code = "CUT-TEST",
            name = "Cutting",
        });
        var bomId = await CreateMasterAsync("/api/v1/master-data/boms", new
        {
            productId = salesMaster.ProductId,
            code = "BOM-TEST",
            name = "Test product BOM",
        });
        using var createdBomVersion = await PostWithCsrfAsync(
            client, $"/api/v1/master-data/boms/{bomId}/versions",
            new { effectiveFrom = "2029-01-01", effectiveTo = (string?)null },
            "manufacturing-bom-version-create");
        Assert.Equal(HttpStatusCode.Created, createdBomVersion.StatusCode);
        using var bomVersionJson = JsonDocument.Parse(
            await createdBomVersion.Content.ReadAsStringAsync());
        var bomVersionId = bomVersionJson.RootElement.GetProperty("id").GetGuid();

        using var csrfResponse = await client.GetAsync("/api/v1/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        using var csrfJson = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        using var saveComponentsRequest = new HttpRequestMessage(
            HttpMethod.Put, $"/api/v1/master-data/bom-versions/{bomVersionId}/components")
        {
            Content = JsonContent.Create(new[]
            {
                new
                {
                    itemId = purchaseMaster.ItemId,
                    unitOfMeasureId = purchaseMaster.UnitId,
                    lineNumber = 1,
                    quantity = 2m,
                    wastePercent = 10m,
                    alternativeGroupCode = (string?)null,
                    isOptional = false,
                },
            }),
        };
        saveComponentsRequest.Headers.Add(
            "X-ERP-CSRF", csrfJson.RootElement.GetProperty("requestToken").GetString()!);
        using var savedComponents = await client.SendAsync(saveComponentsRequest);
        Assert.Equal(HttpStatusCode.OK, savedComponents.StatusCode);
        using var approvedBom = await PostWithCsrfAsync(
            client, $"/api/v1/master-data/bom-versions/{bomVersionId}/approve",
            new { }, "manufacturing-bom-version-approve");
        Assert.Equal(HttpStatusCode.OK, approvedBom.StatusCode);

        var salesOrderRequest = new
        {
            customerId = salesMaster.CustomerId,
            customerPurchaseOrderReference = "buyer-po-manufacturing",
            customerPurchaseOrderDate = "2030-01-01",
            currencyCode = "USD",
            lines = new[]
            {
                new
                {
                    productId = salesMaster.ProductId,
                    productVariantId = salesMaster.VariantId,
                    quantity = 5m,
                    unitPrice = 20m,
                    requestedDeliveryDate = "2030-02-01",
                },
            },
        };
        using var createdOrder = await PostWithCsrfAsync(
            client, "/api/v1/sales/orders", salesOrderRequest,
            "manufacturing-sales-create");
        Assert.Equal(HttpStatusCode.Created, createdOrder.StatusCode);
        using var orderJson = JsonDocument.Parse(await createdOrder.Content.ReadAsStringAsync());
        var salesOrderId = orderJson.RootElement.GetProperty("id").GetGuid();
        var salesLineId = orderJson.RootElement.GetProperty("lines")[0].GetProperty("id").GetGuid();
        using var submittedOrder = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{salesOrderId}/submit", new { },
            "manufacturing-sales-submit");
        Assert.Equal(HttpStatusCode.OK, submittedOrder.StatusCode);
        using var approvedOrder = await PostWithCsrfAsync(
            approverClient, $"/api/v1/sales/orders/{salesOrderId}/approve", new { },
            "manufacturing-sales-approve");
        Assert.Equal(HttpStatusCode.OK, approvedOrder.StatusCode);
        using var confirmedOrder = await PostWithCsrfAsync(
            client, $"/api/v1/sales/orders/{salesOrderId}/confirm", new { },
            "manufacturing-sales-confirm");
        Assert.Equal(HttpStatusCode.OK, confirmedOrder.StatusCode);

        using var createdPurchaseOrder = await PostWithCsrfAsync(client,
            "/api/v1/purchase/orders",
            new
            {
                supplierId = purchaseMaster.SupplierId,
                currencyCode = "USD",
                lines = new[]
                {
                    new
                    {
                        itemId = purchaseMaster.ItemId,
                        unitOfMeasureId = purchaseMaster.UnitId,
                        quantity = 5m,
                        unitPrice = 1m,
                        expectedDeliveryDate = "2030-01-25",
                    },
                },
            }, "manufacturing-purchase-order-create");
        Assert.Equal(HttpStatusCode.Created, createdPurchaseOrder.StatusCode);
        using var purchaseOrderJson = JsonDocument.Parse(
            await createdPurchaseOrder.Content.ReadAsStringAsync());
        var purchaseOrderId = purchaseOrderJson.RootElement.GetProperty("id").GetGuid();
        using var submittedPurchaseOrder = await PostWithCsrfAsync(client,
            $"/api/v1/purchase/orders/{purchaseOrderId}/submit", new { },
            "manufacturing-purchase-order-submit");
        Assert.Equal(HttpStatusCode.OK, submittedPurchaseOrder.StatusCode);
        using var approvedPurchaseOrder = await PostWithCsrfAsync(approverClient,
            $"/api/v1/purchase/orders/{purchaseOrderId}/approve", new { },
            "manufacturing-purchase-order-approve");
        Assert.Equal(HttpStatusCode.OK, approvedPurchaseOrder.StatusCode);
        using var issuedPurchaseOrder = await PostWithCsrfAsync(client,
            $"/api/v1/purchase/orders/{purchaseOrderId}/issue", new { },
            "manufacturing-purchase-order-issue");
        Assert.Equal(HttpStatusCode.OK, issuedPurchaseOrder.StatusCode);

        using var heldStock = await PostWithCsrfAsync(client, "/api/v1/inventory/adjustments",
            new
            {
                itemId = purchaseMaster.ItemId,
                warehouseId = purchaseMaster.WarehouseId,
                stockStatus = "hold",
                quantity = 100m,
                reason = "Held stock is not available for production.",
            }, "manufacturing-held-stock");
        Assert.Equal(HttpStatusCode.OK, heldStock.StatusCode);
        using var mrp = await PostWithCsrfAsync(client, "/api/v1/planning/mrp/requirements",
            new { fromDate = "2030-02-01", throughDate = "2030-02-01" },
            "manufacturing-mrp");
        Assert.Equal(HttpStatusCode.OK, mrp.StatusCode);
        using var mrpJson = JsonDocument.Parse(await mrp.Content.ReadAsStringAsync());
        var mrpMaterial = Assert.Single(mrpJson.RootElement.GetProperty("materials").EnumerateArray());
        Assert.Equal(11m, mrpMaterial.GetProperty("grossRequirement").GetDecimal());
        Assert.Equal(5m, mrpMaterial.GetProperty("openPurchaseOrderReceipts").GetDecimal());
        Assert.Equal(6m, mrpMaterial.GetProperty("netRequirement").GetDecimal());
        Assert.Equal(6m, mrpMaterial.GetProperty("purchaseSuggestionQuantity").GetDecimal());

        using var variantList = await client.GetAsync(
            $"/api/v1/master-data/variants?productId={salesMaster.ProductId}&page=1&pageSize=10");
        variantList.EnsureSuccessStatusCode();
        using var variantJson = JsonDocument.Parse(await variantList.Content.ReadAsStringAsync());
        var finishedItemId = variantJson.RootElement.GetProperty("items")[0]
            .GetProperty("itemId").GetGuid();
        var workOrderRequest = new
        {
            salesOrderId,
            salesOrderLineId = salesLineId,
            bomVersionId,
            outputWarehouseId = purchaseMaster.WarehouseId,
            plannedQuantity = 5m,
            plannedStartDate = "2030-01-20",
            plannedEndDate = "2030-01-31",
            operations = new[]
            {
                new
                {
                    productionOperationId = operationId,
                    sequence = 1,
                    plannedStartDate = "2030-01-20",
                    plannedEndDate = "2030-01-31",
                },
            },
        };
        using var createdWorkOrder = await PostWithCsrfAsync(client,
            "/api/v1/production/work-orders", workOrderRequest,
            "manufacturing-work-order-create");
        Assert.Equal(HttpStatusCode.Created, createdWorkOrder.StatusCode);
        using var workOrderJson = JsonDocument.Parse(await createdWorkOrder.Content.ReadAsStringAsync());
        var workOrderId = workOrderJson.RootElement.GetProperty("id").GetGuid();
        using var replayedWorkOrder = await PostWithCsrfAsync(client,
            "/api/v1/production/work-orders", workOrderRequest,
            "manufacturing-work-order-create");
        Assert.Equal(HttpStatusCode.Created, replayedWorkOrder.StatusCode);
        using var replayJson = JsonDocument.Parse(await replayedWorkOrder.Content.ReadAsStringAsync());
        Assert.Equal(workOrderId, replayJson.RootElement.GetProperty("id").GetGuid());

        using var unreadyMaterials = await client.GetAsync(
            $"/api/v1/production/work-orders/{workOrderId}/materials");
        unreadyMaterials.EnsureSuccessStatusCode();
        using var unreadyJson = JsonDocument.Parse(await unreadyMaterials.Content.ReadAsStringAsync());
        Assert.False(unreadyJson.RootElement.GetProperty("isReady").GetBoolean());
        Assert.Equal(11m, unreadyJson.RootElement.GetProperty("materials")[0]
            .GetProperty("shortage").GetDecimal());

        using var linkedAmendment = await PostWithCsrfAsync(client,
            $"/api/v1/sales/orders/{salesOrderId}/amend", salesOrderRequest,
            "manufacturing-linked-order-amend");
        Assert.Equal(HttpStatusCode.Conflict, linkedAmendment.StatusCode);
        using var submittedWorkOrder = await PostWithCsrfAsync(client,
            $"/api/v1/production/work-orders/{workOrderId}/submit", new { },
            "manufacturing-work-order-submit");
        Assert.Equal(HttpStatusCode.OK, submittedWorkOrder.StatusCode);
        using var selfApproval = await PostWithCsrfAsync(client,
            $"/api/v1/production/work-orders/{workOrderId}/approve", new { },
            "manufacturing-work-order-self-approve");
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);
        using var approvedWorkOrder = await PostWithCsrfAsync(approverClient,
            $"/api/v1/production/work-orders/{workOrderId}/approve", new { },
            "manufacturing-work-order-approve");
        Assert.Equal(HttpStatusCode.OK, approvedWorkOrder.StatusCode);

        using var approvedJson = JsonDocument.Parse(
            await approvedWorkOrder.Content.ReadAsStringAsync());
        var plannedOperationId = approvedJson.RootElement.GetProperty("operations")[0]
            .GetProperty("id").GetGuid();
        using var plannedOrder = await PutWithCsrfAsync(client,
            $"/api/v1/planning/production/orders/{workOrderId}",
            new
            {
                priority = 2,
                plannedStartDate = "2030-01-20",
                plannedEndDate = "2030-01-31",
                operations = new[]
                {
                    new
                    {
                        operationId = plannedOperationId,
                        targetQuantity = 5m,
                        productionLine = "line-a",
                        assignedTeam = "team-a",
                        plannedStartDate = "2030-01-20",
                        plannedEndDate = "2030-01-31",
                    },
                },
            }, "manufacturing-plan-update");
        Assert.Equal(HttpStatusCode.OK, plannedOrder.StatusCode);

        using var insufficientStock = await PostWithCsrfAsync(client,
            $"/api/v1/production/work-orders/{workOrderId}/release", new { },
            "manufacturing-work-order-release-short");
        Assert.Equal(HttpStatusCode.Conflict, insufficientStock.StatusCode);

        using var availableStock = await PostWithCsrfAsync(client, "/api/v1/inventory/adjustments",
            new
            {
                itemId = purchaseMaster.ItemId,
                warehouseId = purchaseMaster.WarehouseId,
                stockStatus = "available",
                quantity = 11m,
                reason = "Production stock receipt for workflow test.",
            }, "manufacturing-available-stock");
        Assert.Equal(HttpStatusCode.OK, availableStock.StatusCode);
        using var readyMaterials = await client.GetAsync(
            $"/api/v1/production/work-orders/{workOrderId}/materials");
        readyMaterials.EnsureSuccessStatusCode();
        using var readyJson = JsonDocument.Parse(await readyMaterials.Content.ReadAsStringAsync());
        Assert.True(readyJson.RootElement.GetProperty("isReady").GetBoolean());
        using var releasedWorkOrder = await PostWithCsrfAsync(client,
            $"/api/v1/production/work-orders/{workOrderId}/release", new { },
            "manufacturing-work-order-release");
        Assert.Equal(HttpStatusCode.OK, releasedWorkOrder.StatusCode);
        using var generatedCards = await PostWithCsrfAsync(client,
            $"/api/v1/production/work-orders/{workOrderId}/job-cards", new { },
            "manufacturing-job-card-generate");
        Assert.Equal(HttpStatusCode.OK, generatedCards.StatusCode);
        using var cardsJson = JsonDocument.Parse(await generatedCards.Content.ReadAsStringAsync());
        var jobCardId = Assert.Single(cardsJson.RootElement.EnumerateArray())
            .GetProperty("id").GetGuid();
        using var startedCard = await PostWithCsrfAsync(client,
            $"/api/v1/production/job-cards/{jobCardId}/start", new { },
            "manufacturing-job-card-start");
        Assert.Equal(HttpStatusCode.OK, startedCard.StatusCode);

        using var overTargetConfirmation = await PostWithCsrfAsync(client,
            $"/api/v1/production/job-cards/{jobCardId}/confirmations",
            new
            {
                goodQuantity = 6m,
                rejectedQuantity = 0m,
                reworkQuantity = 0m,
                scrapQuantity = 0m,
                shift = "day",
                startedAt = "2026-10-30T06:00:00Z",
                completedAt = "2026-10-30T07:00:00Z",
            }, "manufacturing-confirmation-over-target");
        Assert.Equal(HttpStatusCode.BadRequest, overTargetConfirmation.StatusCode);

        var confirmationRequest = new
        {
            goodQuantity = 2m,
            rejectedQuantity = 1m,
            reworkQuantity = 0m,
            scrapQuantity = 0m,
            shift = "day",
            productionLine = "line-a",
            team = "team-a",
            startedAt = "2026-10-30T06:00:00Z",
            completedAt = "2026-10-30T07:00:00Z",
        };
        using var submittedConfirmation = await PostWithCsrfAsync(client,
            $"/api/v1/production/job-cards/{jobCardId}/confirmations",
            confirmationRequest, "manufacturing-confirmation-create");
        Assert.Equal(HttpStatusCode.Created, submittedConfirmation.StatusCode);
        using var confirmationJson = JsonDocument.Parse(
            await submittedConfirmation.Content.ReadAsStringAsync());
        var confirmationId = confirmationJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("pending_approval",
            confirmationJson.RootElement.GetProperty("status").GetString());
        using var pendingConfirmationBalances = await client.GetAsync("/api/v1/inventory/balances");
        pendingConfirmationBalances.EnsureSuccessStatusCode();
        using var pendingBalancesJson = JsonDocument.Parse(
            await pendingConfirmationBalances.Content.ReadAsStringAsync());
        Assert.DoesNotContain(pendingBalancesJson.RootElement.EnumerateArray(),
            value => value.GetProperty("itemId").GetGuid() == finishedItemId);

        using var repeatedConfirmation = await PostWithCsrfAsync(client,
            $"/api/v1/production/job-cards/{jobCardId}/confirmations",
            confirmationRequest, "manufacturing-confirmation-create");
        Assert.Equal(HttpStatusCode.Created, repeatedConfirmation.StatusCode);
        Assert.Equal(await submittedConfirmation.Content.ReadAsStringAsync(),
            await repeatedConfirmation.Content.ReadAsStringAsync());

        using var selfApprovedConfirmation = await PostWithCsrfAsync(client,
            $"/api/v1/production/confirmations/{confirmationId}/approve", new { },
            "manufacturing-confirmation-self-approve");
        Assert.Equal(HttpStatusCode.Conflict, selfApprovedConfirmation.StatusCode);
        using var approvedConfirmation = await PostWithCsrfAsync(approverClient,
            $"/api/v1/production/confirmations/{confirmationId}/approve", new { },
            "manufacturing-confirmation-approve");
        Assert.Equal(HttpStatusCode.OK, approvedConfirmation.StatusCode);

        using var partialCardsResponse = await client.GetAsync(
            $"/api/v1/production/job-cards?workOrderId={workOrderId}");
        partialCardsResponse.EnsureSuccessStatusCode();
        using var partialCardsJson = JsonDocument.Parse(
            await partialCardsResponse.Content.ReadAsStringAsync());
        var partialCard = Assert.Single(partialCardsJson.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("partially_completed", partialCard.GetProperty("status").GetString());
        Assert.Equal(2m, partialCard.GetProperty("outstandingQuantity").GetDecimal());

        using var restartedCard = await PostWithCsrfAsync(client,
            $"/api/v1/production/job-cards/{jobCardId}/start", new { },
            "manufacturing-job-card-restart");
        Assert.Equal(HttpStatusCode.OK, restartedCard.StatusCode);
        using var finalConfirmation = await PostWithCsrfAsync(client,
            $"/api/v1/production/job-cards/{jobCardId}/confirmations",
            new
            {
                goodQuantity = 1m,
                rejectedQuantity = 0m,
                reworkQuantity = 1m,
                scrapQuantity = 0m,
                shift = "day",
                startedAt = "2026-10-30T07:00:00Z",
                completedAt = "2026-10-30T08:00:00Z",
            }, "manufacturing-confirmation-final");
        Assert.Equal(HttpStatusCode.Created, finalConfirmation.StatusCode);
        using var finalConfirmationJson = JsonDocument.Parse(
            await finalConfirmation.Content.ReadAsStringAsync());
        var finalConfirmationId = finalConfirmationJson.RootElement.GetProperty("id").GetGuid();
        using var approvedFinalConfirmation = await PostWithCsrfAsync(approverClient,
            $"/api/v1/production/confirmations/{finalConfirmationId}/approve", new { },
            "manufacturing-confirmation-final-approve");
        Assert.Equal(HttpStatusCode.OK, approvedFinalConfirmation.StatusCode);

        using var correction = await PostWithCsrfAsync(client,
            $"/api/v1/production/confirmations/{confirmationId}/corrections",
            new
            {
                goodQuantity = 1m,
                rejectedQuantity = 2m,
                reworkQuantity = 0m,
                scrapQuantity = 0m,
                reason = "Reclassify one accepted piece as rejected.",
            }, "manufacturing-confirmation-correction");
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        using var correctionJson = JsonDocument.Parse(await correction.Content.ReadAsStringAsync());
        var correctionId = correctionJson.RootElement.GetProperty("id").GetGuid();
        using var approvedCorrection = await PostWithCsrfAsync(approverClient,
            $"/api/v1/production/confirmations/{correctionId}/approve", new { },
            "manufacturing-confirmation-correction-approve");
        Assert.Equal(HttpStatusCode.OK, approvedCorrection.StatusCode);

        using var completedWorkOrder = await PostWithCsrfAsync(client,
            $"/api/v1/production/work-orders/{workOrderId}/complete", new { },
            "manufacturing-work-order-complete");
        Assert.Equal(HttpStatusCode.OK, completedWorkOrder.StatusCode);

        using var balances = await client.GetAsync("/api/v1/inventory/balances");
        balances.EnsureSuccessStatusCode();
        using var balancesJson = JsonDocument.Parse(await balances.Content.ReadAsStringAsync());
        var balanceRows = balancesJson.RootElement.EnumerateArray().ToArray();
        var finishedBalance = Assert.Single(balanceRows,
            value => value.GetProperty("itemId").GetGuid() == finishedItemId);
        Assert.Equal(2m, finishedBalance.GetProperty("onHandQuantity").GetDecimal());
        Assert.DoesNotContain(balanceRows, value =>
            value.GetProperty("itemId").GetGuid() == purchaseMaster.ItemId
            && value.GetProperty("stockStatus").GetString() == "available");
        var heldBalance = Assert.Single(balanceRows,
            value => value.GetProperty("itemId").GetGuid() == purchaseMaster.ItemId
                && value.GetProperty("stockStatus").GetString() == "hold");
        Assert.Equal(100m, heldBalance.GetProperty("onHandQuantity").GetDecimal());

        using var history = await client.GetAsync(
            $"/api/v1/production/work-orders/{workOrderId}/history");
        history.EnsureSuccessStatusCode();
        using var historyJson = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        Assert.Contains(historyJson.RootElement.EnumerateArray(),
            value => value.GetProperty("action").GetString() == "completed");
    }

    [Fact]
    public async Task MasterDataEndpointsReturnValidationProblemForMalformedJson()
    {
        using var tokenResponse = await client.GetAsync("/api/v1/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "/api/v1/master-data/customers")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(
            "X-ERP-CSRF",
            tokenJson.RootElement.GetProperty("requestToken").GetString()!);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("request", problem.RootElement.GetProperty("errors").EnumerateObject()
            .Select(error => error.Name));
    }

    [Fact]
    public async Task QualityJobWorkAndMaintenanceWorkflowsPreserveCustodyAndAudit()
    {
        var master = await CreatePurchaseMasterDataAsync();
        var machineId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            db.Machines.Add(new Machine
            {
                Id = machineId,
                CompanyId = companyId,
                Code = $"M-{Guid.NewGuid():N}"[..10],
                Name = "Test machine",
            });
            db.InventoryStockBalances.Add(new InventoryStockBalance
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                ItemId = master.ItemId,
                WarehouseId = master.WarehouseId,
                StockStatus = InventoryStockStatuses.Available,
                OnHandQuantity = 10m,
            });
            await db.SaveChangesAsync();
        }

        using var subcontractorResponse = await PostWithCsrfAsync(client,
            "/api/v1/job-work/subcontractors",
            new { supplierId = master.SupplierId, registrationCode = "SUB-TEST" },
            "subcontractor-register");
        Assert.Equal(HttpStatusCode.Created, subcontractorResponse.StatusCode);
        using var subcontractorJson = JsonDocument.Parse(
            await subcontractorResponse.Content.ReadAsStringAsync());
        var subcontractorId = subcontractorJson.RootElement.GetGuid();

        using var orderResponse = await PostWithCsrfAsync(client, "/api/v1/job-work/orders",
            new
            {
                subcontractorId,
                outputItemId = master.ItemId,
                outputWarehouseId = master.WarehouseId,
                plannedQuantity = 3m,
                unitCharge = 2m,
                currencyCode = "USD",
                expectedReturnDate = "2030-01-15",
                materials = new[]
                {
                    new { itemId = master.ItemId, warehouseId = master.WarehouseId, quantity = 3m },
                },
            },
            "jobwork-order-create");
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);
        using var orderJson = JsonDocument.Parse(await orderResponse.Content.ReadAsStringAsync());
        var orderId = orderJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(3m, orderJson.RootElement.GetProperty("materials")[0]
            .GetProperty("plannedQuantity").GetDecimal());
        var materialIssueId = orderJson.RootElement.GetProperty("materials")[0]
            .GetProperty("issueId").GetGuid();
        var dispatchBody = new[]
        {
            new { materialIssueId, quantity = 3m },
        };
        using var overDispatch = await PostWithCsrfAsync(client,
            $"/api/v1/job-work/orders/{orderId}/dispatch",
            new[] { new { materialIssueId, quantity = 4m } }, "jobwork-dispatch-over-plan");
        Assert.Equal(HttpStatusCode.Conflict, overDispatch.StatusCode);
        using var dispatch = await PostWithCsrfAsync(client,
            $"/api/v1/job-work/orders/{orderId}/dispatch", dispatchBody, "jobwork-dispatch");
        Assert.Equal(HttpStatusCode.OK, dispatch.StatusCode);
        using var replay = await PostWithCsrfAsync(client,
            $"/api/v1/job-work/orders/{orderId}/dispatch", dispatchBody, "jobwork-dispatch");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var insufficientReturn = await PostWithCsrfAsync(client,
            $"/api/v1/job-work/orders/{orderId}/reconcile",
            new { materialIssueId, quantity = 4m }, "jobwork-return-invalid");
        Assert.Equal(HttpStatusCode.Conflict, insufficientReturn.StatusCode);
        using var returnedMaterial = await PostWithCsrfAsync(client,
            $"/api/v1/job-work/orders/{orderId}/reconcile",
            new { materialIssueId, quantity = 3m }, "jobwork-return-valid");
        Assert.Equal(HttpStatusCode.OK, returnedMaterial.StatusCode);

        using var receiptResponse = await PostWithCsrfAsync(client,
            $"/api/v1/job-work/orders/{orderId}/receipts",
            new { reference = "SUB-RECEIPT-1", processedQuantity = 2m, rejectedQuantity = 1m },
            "jobwork-receipt");
        Assert.Equal(HttpStatusCode.OK, receiptResponse.StatusCode);
        using var receiptJson = JsonDocument.Parse(await receiptResponse.Content.ReadAsStringAsync());
        var receiptId = receiptJson.RootElement.GetProperty("receipts")[0].GetProperty("id").GetGuid();

        using var defectResponse = await PostWithCsrfAsync(client, "/api/v1/quality/defect-codes",
            new { code = "DEF-TEST", description = "Test defect" }, "defect-code-create");
        Assert.Equal(HttpStatusCode.Created, defectResponse.StatusCode);
        using var defectList = await client.GetAsync("/api/v1/quality/defect-codes");
        defectList.EnsureSuccessStatusCode();
        using var defectJson = JsonDocument.Parse(await defectList.Content.ReadAsStringAsync());
        var defectId = Assert.Single(defectJson.RootElement.EnumerateArray())
            .GetProperty("id").GetGuid();
        var inspectionRequest = new
        {
            stage = "jobwork",
            sourceType = "jobwork_receipt",
            sourceId = receiptId,
            sourceLineId = receiptId,
            itemId = master.ItemId,
            warehouseId = master.WarehouseId,
            inspectedQuantity = 3m,
            acceptedQuantity = 2m,
            rejectedQuantity = 1m,
            reworkQuantity = 0m,
            holdQuantity = 0m,
            defects = new[] { new { defectCodeId = defectId, quantity = 1m, notes = "Surface issue" } },
        };
        using var inspectionResponse = await PostWithCsrfAsync(client,
            "/api/v1/quality/inspections", inspectionRequest, "quality-inspection");
        Assert.Equal(HttpStatusCode.Created, inspectionResponse.StatusCode);
        using var inspectionJson = JsonDocument.Parse(
            await inspectionResponse.Content.ReadAsStringAsync());
        var inspectionId = inspectionJson.RootElement.GetProperty("id").GetGuid();
        using var selfApproval = await PostWithCsrfAsync(client,
            $"/api/v1/quality/inspections/{inspectionId}/approve", new { },
            "quality-inspection-self-approve");
        Assert.Equal(HttpStatusCode.Conflict, selfApproval.StatusCode);
        using var approval = await PostWithCsrfAsync(approverClient,
            $"/api/v1/quality/inspections/{inspectionId}/approve", new { },
            "quality-inspection-approve");
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        using var inspectionHistory = await client.GetAsync(
            $"/api/v1/quality/inspections/{inspectionId}/history");
        inspectionHistory.EnsureSuccessStatusCode();

        using var maintenanceResponse = await PostWithCsrfAsync(client,
            "/api/v1/maintenance/requests",
            new
            {
                machineId,
                requestNumber = $"MR-{Guid.NewGuid():N}"[..20],
                description = "Inspect machine",
                priority = "high",
                assignedTeam = "Maintenance team",
                workOrderId = (Guid?)null,
            },
            "maintenance-request-create");
        Assert.Equal(HttpStatusCode.Created, maintenanceResponse.StatusCode);
        using var maintenanceJson = JsonDocument.Parse(
            await maintenanceResponse.Content.ReadAsStringAsync());
        var maintenanceCardId = maintenanceJson.RootElement.GetProperty("jobCardId").GetGuid();
        using var spareResponse = await PostWithCsrfAsync(client,
            $"/api/v1/maintenance/job-cards/{maintenanceCardId}/spares",
            new { itemId = master.ItemId, warehouseId = master.WarehouseId, quantity = 1m },
            "maintenance-spare-issue");
        Assert.Equal(HttpStatusCode.Created, spareResponse.StatusCode);
        using var completion = await PostWithCsrfAsync(client,
            $"/api/v1/maintenance/job-cards/{maintenanceCardId}/complete",
            new { summary = "Inspection complete" }, "maintenance-complete");
        Assert.Equal(HttpStatusCode.OK, completion.StatusCode);
        using var scheduleResponse = await PostWithCsrfAsync(client,
            "/api/v1/maintenance/preventive-schedules",
            new
            {
                machineId,
                name = "Monthly inspection",
                intervalDays = 30,
                nextDueDate = "2030-01-15",
            },
            "maintenance-schedule-create");
        Assert.Equal(HttpStatusCode.Created, scheduleResponse.StatusCode);

        using var downtimeResponse = await PostWithCsrfAsync(client,
            "/api/v1/maintenance/downtime",
            new
            {
                machineId,
                workOrderId = (Guid?)null,
                reason = "Test downtime",
                startedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
            "maintenance-downtime-start");
        Assert.Equal(HttpStatusCode.Created, downtimeResponse.StatusCode);
        using var downtimeJson = JsonDocument.Parse(
            await downtimeResponse.Content.ReadAsStringAsync());
        var downtimeId = downtimeJson.RootElement.GetProperty("id").GetGuid();
        using var endDowntime = await PostWithCsrfAsync(client,
            $"/api/v1/maintenance/downtime/{downtimeId}/end", new { },
            "maintenance-downtime-end");
        Assert.Equal(HttpStatusCode.OK, endDowntime.StatusCode);

        using var orderHistory = await client.GetAsync(
            $"/api/v1/job-work/orders/{orderId}/history");
        orderHistory.EnsureSuccessStatusCode();
        using var maintenanceHistory = await client.GetAsync(
            $"/api/v1/maintenance/requests/{maintenanceJson.RootElement.GetProperty("id").GetGuid()}/history");
        maintenanceHistory.EnsureSuccessStatusCode();
    }

    private async Task<(Guid SupplierId, Guid ItemId, Guid UnitId, Guid WarehouseId)> CreatePurchaseMasterDataAsync()
    {
        var supplier = await CreateMasterAsync("/api/v1/master-data/suppliers", new
        {
            code = "SUP-TEST",
            name = "Commerce Supplier",
        });
        var warehouse = await CreateMasterAsync("/api/v1/master-data/warehouses", new
        {
            code = "WH-TEST",
            name = "Test Warehouse",
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
        return (supplier, item, unit, warehouse);
    }

    private async Task<(Guid CustomerId, Guid ProductId, Guid VariantId, Guid ItemId, Guid WarehouseId)> CreateSalesMasterDataAsync()
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
        var warehouse = await CreateMasterAsync("/api/v1/master-data/warehouses", new
        {
            code = "WH-SALES",
            name = "Finished Goods Warehouse",
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
        return (customer, product, variant, item, warehouse);
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

    private static async Task<HttpResponseMessage> PutWithCsrfAsync(
        HttpClient target,
        string path,
        object body,
        string idempotencyKey)
    {
        using var tokenResponse = await target.GetAsync("/api/v1/auth/csrf");
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(
            "X-ERP-CSRF",
            tokenJson.RootElement.GetProperty("requestToken").GetString()!);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await target.SendAsync(request);
    }
}
