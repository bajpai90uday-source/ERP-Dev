using System.Net;
using System.Net.Mail;
using Erp.Domain.Operations;
using Erp.Domain.Platform;
using Erp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Erp.Api.Operations;

internal sealed class NotificationEmailOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public int RetryLimit { get; set; } = 5;
}

internal sealed class NotificationEmailWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationEmailOptions> options,
    ILogger<NotificationEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!Configured())
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Notification email processing failed.");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        var pending = await db.InAppNotifications.AsNoTracking()
            .OrderBy(value => value.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var notification in pending)
        {
            var recipients = await (
                from membership in db.UserCompanyRoles.AsNoTracking()
                join account in db.UserAccounts.AsNoTracking()
                    on membership.UserId equals account.Id
                where membership.CompanyId == notification.CompanyId
                    && account.IsActive && account.Email != null
                select new { account.Id, account.Email })
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var recipient in recipients)
            {
                var existing = await db.NotificationDeliveryLogs.SingleOrDefaultAsync(value =>
                    value.CompanyId == notification.CompanyId
                    && value.NotificationId == notification.Id
                    && value.Recipient == recipient.Email, cancellationToken);
                if (existing?.Status == "delivered"
                    || existing?.AttemptCount >= options.Value.RetryLimit)
                    continue;

                var delivery = existing ?? new NotificationDeliveryLog
                {
                    Id = Guid.NewGuid(),
                    CompanyId = notification.CompanyId,
                    NotificationId = notification.Id,
                    Recipient = recipient.Email!,
                };
                delivery.Status = "sending";
                delivery.AttemptCount++;
                delivery.UpdatedAt = DateTimeOffset.UtcNow;
                if (existing is null) db.NotificationDeliveryLogs.Add(delivery);
                await db.SaveChangesAsync(cancellationToken);

                try
                {
                    using var message = new MailMessage(
                        options.Value.From!, recipient.Email!, notification.Title, notification.Message);
                    using var client = new SmtpClient(options.Value.Host!, options.Value.Port)
                    {
                        EnableSsl = options.Value.EnableSsl,
                        Timeout = 15000,
                    };
                    if (!string.IsNullOrWhiteSpace(options.Value.Username))
                    {
                        client.Credentials = new NetworkCredential(
                            options.Value.Username, options.Value.Password);
                    }
                    await client.SendMailAsync(message, cancellationToken);
                    delivery.Status = "delivered";
                    delivery.Error = null;
                    delivery.DeliveredAt = DateTimeOffset.UtcNow;
                }
                catch (Exception exception) when (exception is SmtpException or InvalidOperationException)
                {
                    delivery.Status = "failed";
                    delivery.Error = exception.Message.Length > 1000
                        ? exception.Message[..1000] : exception.Message;
                    logger.LogWarning(exception,
                        "Email notification {NotificationId} could not be delivered.", notification.Id);
                }
                delivery.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private bool Configured()
    {
        var value = options.Value;
        return !string.IsNullOrWhiteSpace(value.Host)
            && !string.IsNullOrWhiteSpace(value.From)
            && value.Port is > 0 and <= 65535
            && value.RetryLimit is > 0 and <= 20;
    }
}
