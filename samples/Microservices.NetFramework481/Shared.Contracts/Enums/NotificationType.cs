namespace Shared.Contracts.Enums;

/// <summary>
/// Types of notifications that can be sent to customers.
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// Email notification.
    /// </summary>
    Email = 0,

    /// <summary>
    /// SMS notification.
    /// </summary>
    SMS = 1,

    /// <summary>
    /// Push notification.
    /// </summary>
    Push = 2
}
