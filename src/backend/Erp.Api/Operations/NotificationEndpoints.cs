using System.Security.Claims;
using Erp.Api.Security;
using Erp.Domain.Operations;
using Erp.Domain.Platform;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Erp.Api.Operations;

internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var notifications = app.MapGroup("/api/v1/notifications");
        notifications.MapGet("", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            var userId = UserId(context);
            var query = db.InAppNotifications.AsNoTracking()
                .Where(value => value.CompanyId == companyId
                    && (value.RecipientUserId == null || value.RecipientUserId == userId))
                .Select(value => new
                {
                    value.Id, value.Category, value.Title, value.Message,
                    value.SourceType, value.SourceId, value.CreatedAt,
                    IsRead = db.NotificationReadReceipts.Any(receipt =>
                        receipt.CompanyId == companyId && receipt.NotificationId == value.Id
                        && receipt.UserId == userId),
                });
            var items = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
                ? (await query.Take(100).ToArrayAsync(cancellationToken))
                    .OrderByDescending(value => value.CreatedAt).ToArray()
                : await query.OrderByDescending(value => value.CreatedAt).Take(100)
                    .ToArrayAsync(cancellationToken);
            return Results.Ok(items);
        }).RequireAuthorization(PermissionPolicies.For("notifications.read"));

        notifications.MapPost("/{id:guid}/read", async (Guid id, HttpContext context,
            ErpDbContext db, CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            var userId = UserId(context);
            var notification = await db.InAppNotifications.SingleOrDefaultAsync(value =>
                value.CompanyId == companyId && value.Id == id
                && (value.RecipientUserId == null || value.RecipientUserId == userId),
                cancellationToken);
            if (notification is null) return Results.NotFound();
            if (!await db.NotificationReadReceipts.AnyAsync(value =>
                value.CompanyId == companyId && value.NotificationId == id && value.UserId == userId,
                cancellationToken))
            {
                db.NotificationReadReceipts.Add(new NotificationReadReceipt
                {
                    Id = Guid.NewGuid(), CompanyId = companyId,
                    NotificationId = id, UserId = userId,
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            return Results.NoContent();
        }).RequireAuthorization(PermissionPolicies.For("notifications.read"));

        notifications.MapGet("/delivery-logs", async (HttpContext context, ErpDbContext db,
            CancellationToken cancellationToken) =>
        {
            var companyId = CompanyId(context);
            var query = db.NotificationDeliveryLogs.AsNoTracking()
                .Where(value => value.CompanyId == companyId)
                .Select(value => new
                {
                    value.Id, value.NotificationId, value.Recipient, value.Status,
                    value.AttemptCount, value.Error, value.DeliveredAt, value.UpdatedAt,
                });
            var items = db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
                ? (await query.Take(500).ToArrayAsync(cancellationToken))
                    .OrderByDescending(value => value.UpdatedAt).ToArray()
                : await query.OrderByDescending(value => value.UpdatedAt).Take(500)
                    .ToArrayAsync(cancellationToken);
            return Results.Ok(items);
        }).RequireAuthorization(PermissionPolicies.For("notifications.manage"));
    }

    private static Guid CompanyId(HttpContext context) =>
        Guid.Parse(context.User.FindFirstValue(SecurityClaims.CompanyId)!);

    private static Guid UserId(HttpContext context) =>
        Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
