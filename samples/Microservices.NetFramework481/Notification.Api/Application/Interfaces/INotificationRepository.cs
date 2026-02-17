using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sample.Notification.NetFramework481.Application.Interfaces;

public interface INotificationRepository
{
    Task<Domain.Notifications.Notification?> GetByIdAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task SaveAsync(Domain.Notifications.Notification notification, CancellationToken cancellationToken = default);
    Task UpdateAsync(Domain.Notifications.Notification notification, CancellationToken cancellationToken = default);
    Task<bool> SendNotificationAsync(Guid notificationId, CancellationToken cancellationToken = default);
}
