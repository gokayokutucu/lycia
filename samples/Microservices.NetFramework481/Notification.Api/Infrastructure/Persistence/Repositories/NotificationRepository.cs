using Microsoft.Extensions.Logging;
using Sample.Notification.NetFramework481.Application.Interfaces;
using Sample.Notification.NetFramework481.Domain.Notifications;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Notification.NetFramework481.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository(
    NotificationDbContext context,
    ILogger<NotificationRepository> logger) : INotificationRepository
{
    public async Task<Domain.Notifications.Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default)
        => await context.Notifications.FindAsync([notificationId], cancellationToken);

    public async Task SaveAsync(Domain.Notifications.Notification notification, CancellationToken cancellationToken = default)
    {
        await context.Notifications.AddAsync(notification, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Domain.Notifications.Notification notification, CancellationToken cancellationToken = default)
    {
        context.Notifications.Update(notification);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> SendNotificationAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await GetByIdAsync(notificationId, cancellationToken) 
            ?? throw new InvalidOperationException($"Notification not found: {notificationId}");
        notification.Status = NotificationStatus.Sent;
        notification.SentAt = DateTime.UtcNow;

        try
        {
            // Simulate sending notification
            logger.LogInformation("Sending {Type} notification to {Recipient}: {Subject}", 
                notification.Type, notification.Recipient, notification.Subject);

            await Task.Delay(50, cancellationToken);

            await UpdateAsync(notification, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            notification.Status = NotificationStatus.Failed;
            // Log the failure reason instead of storing it
            logger.LogError(ex, "Failed to send notification to {Recipient}", notification.Recipient);
            await UpdateAsync(notification, cancellationToken);
            return false;
        }
    }
}
