using System.Security.Claims;
using Erp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Security;

internal sealed record PermissionRequirement(string PermissionCode) : IAuthorizationRequirement;

internal sealed class PermissionAuthorizationHandler(ErpDbContext db)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var companyIdValue = context.User.FindFirstValue(SecurityClaims.CompanyId);
        if (!Guid.TryParse(userIdValue, out var userId)
            || !Guid.TryParse(companyIdValue, out var companyId))
        {
            return;
        }

        var moduleAccessCode = requirement.PermissionCode == "hr.confidential.read"
            ? "hr.access"
            : $"{requirement.PermissionCode[..requirement.PermissionCode.LastIndexOf('.') ]}.access";
        string[] permissionCodes = requirement.PermissionCode.EndsWith(".access", StringComparison.Ordinal)
            ? [requirement.PermissionCode]
            : [moduleAccessCode, requirement.PermissionCode];
        var grantedCount = await db.UserCompanyRoles
            .AsNoTracking()
            .Where(membership =>
                membership.UserId == userId
                && membership.CompanyId == companyId
                && membership.User.IsActive
                && membership.Role.IsActive
                && db.Companies.Any(company => company.Id == companyId && company.IsActive))
            .SelectMany(membership => membership.Role.RolePermissions
                .Select(rolePermission => rolePermission.Permission.Code))
            .Where(code => permissionCodes.Contains(code))
            .Distinct()
            .CountAsync();

        if (grantedCount == permissionCodes.Length)
        {
            context.Succeed(requirement);
        }
    }
}

internal static class SecurityClaims
{
    public const string CompanyId = "erp:company_id";
    public const string SecurityStamp = "erp:security_stamp";
}

internal static class PermissionPolicies
{
    public static AuthorizationPolicy For(string permissionCode) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissionCode))
            .Build();
}
