using System.Security.Claims;
using System.Text.Json;
using Erp.Api.Security;

namespace Erp.Api.MasterData;

internal static class MasterDataEndpoints
{
    public static void MapMasterDataEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1/master-data");

        MapResource<CustomerRequest, CustomerResponse>(api, "customers", "customers",
            static (service, companyId, query, cancellationToken) =>
                service.ListCustomersAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveCustomerAsync(companyId, actor, id, request, cancellationToken));
        MapResource<SupplierRequest, SupplierResponse>(api, "suppliers", "suppliers",
            static (service, companyId, query, cancellationToken) =>
                service.ListSuppliersAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveSupplierAsync(companyId, actor, id, request, cancellationToken));
        MapResource<ItemCategoryRequest, ItemCategoryResponse>(api, "item-categories", "itemcategories",
            static (service, companyId, query, cancellationToken) =>
                service.ListItemCategoriesAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveItemCategoryAsync(companyId, actor, id, request, cancellationToken));
        MapResource<UnitOfMeasureRequest, UnitOfMeasureResponse>(api, "units", "uom",
            static (service, companyId, query, cancellationToken) =>
                service.ListUnitsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveUnitAsync(companyId, actor, id, request, cancellationToken));
        MapResource<ItemRequest, ItemResponse>(api, "items", "items",
            static (service, companyId, query, cancellationToken) =>
                service.ListItemsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveItemAsync(companyId, actor, id, request, cancellationToken));
        MapResource<WarehouseRequest, WarehouseResponse>(api, "warehouses", "warehouses",
            static (service, companyId, query, cancellationToken) =>
                service.ListWarehousesAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveWarehouseAsync(companyId, actor, id, request, cancellationToken));
        MapResource<WarehouseLocationRequest, WarehouseLocationResponse>(api, "warehouse-locations", "warehouses",
            static (service, companyId, query, cancellationToken) =>
                service.ListWarehouseLocationsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveWarehouseLocationAsync(companyId, actor, id, request, cancellationToken));
        MapResource<ProductRequest, ProductResponse>(api, "products", "products",
            static (service, companyId, query, cancellationToken) =>
                service.ListProductsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveProductAsync(companyId, actor, id, request, cancellationToken));
        MapResource<ProductVariantRequest, ProductVariantResponse>(api, "variants", "products",
            static (service, companyId, query, cancellationToken) =>
                service.ListVariantsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveVariantAsync(companyId, actor, id, request, cancellationToken));
        MapResource<MachineRequest, MachineResponse>(api, "machines", "machines",
            static (service, companyId, query, cancellationToken) =>
                service.ListMachinesAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveMachineAsync(companyId, actor, id, request, cancellationToken));
        MapResource<ProductionOperationRequest, ProductionOperationResponse>(api, "operations", "operations",
            static (service, companyId, query, cancellationToken) =>
                service.ListOperationsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveOperationAsync(companyId, actor, id, request, cancellationToken));
        MapResource<BomRequest, BomResponse>(api, "boms", "boms",
            static (service, companyId, query, cancellationToken) =>
                service.ListBomsAsync(companyId, query, cancellationToken),
            static (service, companyId, actor, id, request, cancellationToken) =>
                service.SaveBomAsync(companyId, actor, id, request, cancellationToken));

        api.MapGet("/items/code-rule", async (
            HttpContext context,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var rule = await service.GetItemCodeRuleAsync(
                GetCompanyId(context.User), cancellationToken);
            return Results.Ok(rule);
        })
            .RequireAuthorization(PermissionPolicies.For("items.read"))
            .WithName("GetItemCodeRule")
            .WithSummary("Get the company's item-code generation rule")
            .Produces<ItemCodeRuleResponse>()
            .Produces(StatusCodes.Status403Forbidden);

        api.MapPut("/items/code-rule", async (
            HttpContext context,
            IMasterDataService service,
            ItemCodeRuleRequest request,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SaveItemCodeRuleAsync(
                GetCompanyId(context.User), GetActor(context), request, cancellationToken);
            return ToResult(result, "/api/v1/master-data/items/code-rule");
        })
            .RequireAuthorization(PermissionPolicies.For("items.update"))
            .WithName("SaveItemCodeRule")
            .WithSummary("Configure item-code generation")
            .Produces<ItemCodeRuleResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        api.MapGet("/boms/{bomId:guid}/versions", async (
            HttpContext context,
            Guid bomId,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var (query, validation) = ParseQuery(context.Request);
            if (validation is not null)
            {
                return Results.ValidationProblem(validation);
            }

            var versions = await service.ListBomVersionsAsync(
                GetCompanyId(context.User), bomId, query, cancellationToken);
            return Results.Ok(versions);
        })
            .RequireAuthorization(PermissionPolicies.For("boms.read"))
            .WithName("ListBomVersions")
            .WithSummary("List a BOM's immutable version history")
            .Produces<PageResponse<BomVersionResponse>>()
            .Produces(StatusCodes.Status403Forbidden);

        api.MapPost("/boms/{bomId:guid}/versions", async (
            HttpContext context,
            Guid bomId,
            BomVersionRequest request,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateBomVersionAsync(
                GetCompanyId(context.User), GetActor(context), bomId, request, cancellationToken);
            var location = result.Value is null
                ? null
                : $"/api/v1/master-data/boms/{bomId}/versions/{result.Value.Id}";
            return ToResult(result, location);
        })
            .RequireAuthorization(PermissionPolicies.For("boms.create"))
            .WithName("CreateBomVersion")
            .WithSummary("Create a draft BOM version from the latest revision")
            .Produces<BomVersionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        api.MapPut("/bom-versions/{versionId:guid}/components", async (
            HttpContext context,
            Guid versionId,
            IReadOnlyList<BomComponentRequest> request,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.SaveBomComponentsAsync(
                GetCompanyId(context.User), GetActor(context), versionId, request, cancellationToken);
            return ToResult(result, null);
        })
            .RequireAuthorization(PermissionPolicies.For("boms.update"))
            .WithName("SaveBomVersionComponents")
            .WithSummary("Replace components on a draft BOM version")
            .Produces<BomVersionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        api.MapPost("/bom-versions/{versionId:guid}/approve", async (
            HttpContext context,
            Guid versionId,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ApproveBomVersionAsync(
                GetCompanyId(context.User), GetActor(context), versionId, cancellationToken);
            return ToResult(result, null);
        })
            .RequireAuthorization(PermissionPolicies.For("boms.approve"))
            .WithName("ApproveBomVersion")
            .WithSummary("Approve a draft BOM version and lock its history")
            .Produces<BomVersionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        MapActivationEndpoints(api);
    }

    private static void MapResource<TRequest, TResponse>(
        RouteGroupBuilder api,
        string route,
        string permission,
        Func<IMasterDataService, Guid, MasterDataQuery, CancellationToken, Task<PageResponse<TResponse>>> list,
        Func<IMasterDataService, Guid, MasterDataActor, Guid?, TRequest, CancellationToken, Task<MasterDataResult<TResponse>>> save)
        where TRequest : class
    {
        var group = api.MapGroup($"/{route}");
        group.MapGet("", async (
            HttpContext context,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var (query, validation) = ParseQuery(context.Request);
            if (validation is not null)
            {
                return Results.ValidationProblem(validation);
            }

            var page = await list(
                service, GetCompanyId(context.User), query, cancellationToken);
            return Results.Ok(page);
        })
            .RequireAuthorization(PermissionPolicies.For($"{permission}.read"))
            .WithName($"List{ToPascalCase(route)}")
            .WithSummary($"Search and page {route}")
            .Produces<PageResponse<TResponse>>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden);

        group.MapPost("", async (
            HttpContext context,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var request = await ReadRequestAsync<TRequest>(context, cancellationToken);
            if (request is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Provide a valid JSON request body."],
                });
            }

            var result = await save(
                service, GetCompanyId(context.User), GetActor(context), null, request, cancellationToken);
            return ToResult(result, result.ResourceId is { } entityId
                ? $"/api/v1/master-data/{route}/{entityId}"
                : null);
        })
            .RequireAuthorization(PermissionPolicies.For($"{permission}.create"))
            .WithName($"Create{ToPascalCase(route)}")
            .WithSummary($"Create a {route} record")
            .Accepts<TRequest>("application/json")
            .Produces<TResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (
            HttpContext context,
            Guid id,
            IMasterDataService service,
            CancellationToken cancellationToken) =>
        {
            var request = await ReadRequestAsync<TRequest>(context, cancellationToken);
            if (request is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Provide a valid JSON request body."],
                });
            }

            var result = await save(
                service, GetCompanyId(context.User), GetActor(context), id, request, cancellationToken);
            return ToResult(result, null);
        })
            .RequireAuthorization(PermissionPolicies.For($"{permission}.update"))
            .WithName($"Update{ToPascalCase(route)}")
            .WithSummary($"Update a {route} record")
            .Accepts<TRequest>("application/json")
            .Produces<TResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    private static void MapActivationEndpoints(RouteGroupBuilder api)
    {
        var resources = new (string Route, string Permission, string ServiceKey)[]
        {
            ("customers", "customers", "customers"),
            ("suppliers", "suppliers", "suppliers"),
            ("item-categories", "itemcategories", "item-categories"),
            ("units", "uom", "units"),
            ("items", "items", "items"),
            ("warehouses", "warehouses", "warehouses"),
            ("warehouse-locations", "warehouses", "warehouse-locations"),
            ("products", "products", "products"),
            ("variants", "products", "variants"),
            ("machines", "machines", "machines"),
            ("operations", "operations", "operations"),
            ("boms", "boms", "boms"),
        };

        foreach (var resource in resources)
        {
            api.MapPatch($"/{resource.Route}/{{id:guid}}/active", async (
                HttpContext context,
                Guid id,
                SetActiveRequest request,
                IMasterDataService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.SetActiveAsync(
                    GetCompanyId(context.User), GetActor(context), resource.ServiceKey,
                    id, request.IsActive, cancellationToken);
                return ToResult(result, null);
            })
                .RequireAuthorization(PermissionPolicies.For($"{resource.Permission}.deactivate"))
                .WithName($"Set{ToPascalCase(resource.Route)}Active")
                .WithSummary($"Activate or deactivate a {resource.Route} record")
                .Produces(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound)
                .Produces(StatusCodes.Status409Conflict);
        }
    }

    private static async Task<TRequest?> ReadRequestAsync<TRequest>(
        HttpContext context,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        try
        {
            return await context.Request.ReadFromJsonAsync<TRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult ToResult<T>(MasterDataResult<T> result, string? location)
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

    private static (MasterDataQuery Query, Dictionary<string, string[]>? Errors) ParseQuery(
        HttpRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var page = ParseInt("page", 1, 1, int.MaxValue);
        var pageSize = ParseInt("pageSize", 25, 1, 100);
        bool? active = null;
        if (request.Query.TryGetValue("active", out var activeValue))
        {
            if (bool.TryParse(activeValue.ToString(), out var parsed))
            {
                active = parsed;
            }
            else
            {
                errors["active"] = ["Active must be true or false."];
            }
        }

        var parentId = ParseGuid("parentId");
        var productId = ParseGuid("productId");
        var search = request.Query["search"].ToString();
        if (search.Length > 200)
        {
            errors["search"] = ["Search text cannot exceed 200 characters."];
        }

        if (errors.Count > 0)
        {
            return (new MasterDataQuery(), errors);
        }

        return (new MasterDataQuery
        {
            Page = page,
            PageSize = pageSize,
            Active = active,
            Search = search,
            Type = request.Query["type"].ToString(),
            ParentId = parentId,
            ProductId = productId,
            Size = request.Query["size"].ToString(),
            Color = request.Query["color"].ToString(),
        }, null);

        int ParseInt(string key, int fallback, int minimum, int maximum)
        {
            if (!request.Query.TryGetValue(key, out var value))
            {
                return fallback;
            }

            if (int.TryParse(value, out var parsed) && parsed >= minimum && parsed <= maximum)
            {
                return parsed;
            }

            errors[key] = [$"{key} must be between {minimum} and {maximum}."];
            return fallback;
        }

        Guid? ParseGuid(string key)
        {
            if (!request.Query.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (Guid.TryParse(value, out var parsed))
            {
                return parsed;
            }

            errors[key] = ["A valid identifier is required."];
            return null;
        }
    }

    private static Guid GetCompanyId(ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(SecurityClaims.CompanyId)!);

    private static MasterDataActor GetActor(HttpContext context)
    {
        var principal = context.User;
        return new MasterDataActor(
            Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
            principal.FindFirstValue(ClaimTypes.Name)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? "unknown",
            context.TraceIdentifier);
    }

    private static string ToPascalCase(string value) =>
        string.Concat(value.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private sealed record SetActiveRequest(bool IsActive);
}
