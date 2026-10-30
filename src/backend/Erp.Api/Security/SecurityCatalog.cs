using Erp.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Security;

internal sealed record PermissionDefinition(string Code, string Name);
internal sealed record RoleDefinition(string Code, string Name, IReadOnlySet<string> PermissionCodes);

internal static class SecurityCatalog
{
    private static readonly IReadOnlyDictionary<string, string[]> ModuleActions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["sales"] = ["read", "create", "update", "delete", "submit", "approve", "confirm", "amend", "fulfill", "export"],
            ["customers"] = ["read", "create", "update", "deactivate"],
            ["suppliers"] = ["read", "create", "update", "deactivate"],
            ["purchase"] = ["read", "create", "update", "submit", "approve", "issue", "amend", "receive", "export"],
            ["items"] = ["read", "create", "update", "deactivate"],
            ["itemcategories"] = ["read", "create", "update", "deactivate"],
            ["uom"] = ["read", "create", "update", "deactivate"],
            ["warehouses"] = ["read", "create", "update", "deactivate"],
            ["machines"] = ["read", "create", "update", "deactivate"],
            ["operations"] = ["read", "create", "update", "deactivate"],
            ["inventory"] = ["read", "receive", "issue", "return", "transfer", "adjust", "count", "export"],
            ["products"] = ["read", "create", "update", "approve"],
            ["boms"] = ["read", "create", "update", "approve", "deactivate"],
            ["workorders"] = ["read", "create", "update", "submit", "approve", "release", "hold", "complete", "cancel"],
            ["planning"] = ["read", "create", "update", "schedule"],
            ["production"] = ["read", "confirm", "complete", "approve", "correct"],
            ["jobcards"] = ["read", "assign", "confirm"],
            ["materials"] = ["read", "issue", "return"],
            ["quality"] = ["read", "inspect", "disposition", "approve"],
            ["jobwork"] = ["read", "create", "dispatch", "receive", "reconcile"],
            ["maintenance"] = ["read", "create", "update", "complete"],
            ["hr"] = ["read", "create", "update", "deactivate"],
            ["attendance"] = ["read", "create", "update", "approve"],
            ["dispatch"] = ["read", "create", "update", "pack", "ship"],
            ["finance"] = ["read", "create", "update", "post", "export"],
            ["notifications"] = ["read", "manage"],
            ["mes"] = ["read", "manage"],
            ["reports"] = ["read", "export"],
        };

    public static IReadOnlyList<PermissionDefinition> Permissions { get; } =
    [
        .. ModuleActions.SelectMany(module => new[]
        {
            new PermissionDefinition($"{module.Key}.access", $"{Title(module.Key)} module access"),
        }.Concat(module.Value.Select(action =>
            new PermissionDefinition($"{module.Key}.{action}", $"{Title(module.Key)} {Title(action)}")))),
        new("security.users.read", "View users"),
        new("security.users.access", "User administration module access"),
        new("security.users.create", "Create users"),
        new("security.users.update", "Update users"),
        new("security.users.deactivate", "Activate or deactivate users"),
        new("security.users.reset_password", "Reset user passwords"),
        new("security.roles.read", "View roles"),
        new("security.roles.access", "Role administration module access"),
        new("security.roles.create", "Create roles"),
        new("security.roles.update", "Update role permissions"),
        new("security.permissions.read", "View permission catalog"),
        new("security.permissions.access", "Permission catalog module access"),
        new("security.audit.read", "View security audit events"),
        new("security.audit.access", "Security audit module access"),
    ];

    public static IReadOnlyList<RoleDefinition> Roles { get; } =
    [
        new("SALES", "Sales", Codes("sales", "customers:read,create,update,deactivate",
            "products:read", "items:read", "reports:read,export")),
        new("PURCHASE", "Purchase", Codes("purchase", "suppliers:read,create,update,deactivate",
            "items:read", "itemcategories:read", "uom:read", "inventory:read", "reports:read,export")),
        new("STORES", "Stores", Codes("inventory", "items", "itemcategories", "uom", "warehouses",
            "reports:read,export")),
        new("PRODUCTION", "Production", Codes("workorders", "planning", "production",
            "jobcards", "materials", "machines", "operations", "boms:read,create,update,approve",
            "products:read,create,update,approve", "items:read", "inventory:read", "reports:read,export")),
        new("QUALITY", "Quality", Codes("quality", "production:read,correct", "jobcards:read", "boms:read",
            "inventory:read", "reports:read,export")),
        new("MAINTENANCE", "Maintenance", Codes("maintenance", "machines", "items:read", "inventory:read",
            "reports:read")),
        new("HR", "HR", Codes("hr", "attendance", "reports:read,export")),
        new("DISPATCH", "Dispatch", Codes("dispatch", "inventory:read", "sales:read",
            "reports:read,export")),
        new("FINANCE", "Finance", Codes("finance", "sales:read", "purchase:read",
            "inventory:read", "reports:read,export")),
        new("ADMINISTRATION", "Administration",
            Permissions.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal)),
    ];

    public static async Task SeedCompanyAsync(
        Erp.Infrastructure.Persistence.ErpDbContext db,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var existingPermissions = await db.Permissions
            .ToDictionaryAsync(permission => permission.Code, cancellationToken);

        foreach (var definition in Permissions)
        {
            if (!existingPermissions.ContainsKey(definition.Code))
            {
                var permission = new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = definition.Code,
                    Name = definition.Name,
                };
                db.Permissions.Add(permission);
                existingPermissions.Add(permission.Code, permission);
            }
        }

        foreach (var definition in Roles)
        {
            var role = await db.Roles.SingleOrDefaultAsync(
                existing => existing.CompanyId == companyId && existing.Code == definition.Code,
                cancellationToken);

            if (role is null)
            {
                role = new Role
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    Code = definition.Code,
                    Name = definition.Name,
                    IsSystemRole = true,
                };
                db.Roles.Add(role);
            }

            var rolePermissions = await db.RolePermissions
                .Where(existing => existing.CompanyId == companyId && existing.RoleId == role.Id)
                .Select(existing => existing.PermissionId)
                .ToListAsync(cancellationToken);
            var assignedPermissionIds = rolePermissions.ToHashSet();
            foreach (var permissionCode in definition.PermissionCodes)
            {
                var permissionId = existingPermissions[permissionCode].Id;
                if (!assignedPermissionIds.Contains(permissionId))
                {
                    db.RolePermissions.Add(new RolePermission
                    {
                        CompanyId = companyId,
                        RoleId = role.Id,
                        PermissionId = permissionId,
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlySet<string> Codes(params string[] assignments)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in assignments)
        {
            var parts = assignment.Split(':', 2);
            if (ModuleActions.ContainsKey(parts[0]))
            {
                codes.Add($"{parts[0]}.access");
                if (parts.Length == 1)
                {
                    foreach (var action in ModuleActions[parts[0]])
                    {
                        codes.Add($"{parts[0]}.{action}");
                    }
                }
                else
                {
                    foreach (var action in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        codes.Add($"{parts[0]}.{action}");
                    }
                }
            }
            else if (assignment.StartsWith("security.", StringComparison.Ordinal))
            {
                codes.Add(assignment);
            }
            else
            {
                codes.Add($"security.{assignment}");
            }
        }

        return codes;
    }

    private static string Title(string value) =>
        string.Join(' ', value.Split('.', '_').Select(part =>
            part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
}
