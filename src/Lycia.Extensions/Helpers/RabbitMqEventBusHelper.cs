using Lycia.Extensions.Configurations;

namespace Lycia.Extensions.Helpers;

public static class RabbitMqEventBusHelper
{
    public static Dictionary<string, object?> BuildMessageHeaders(object message, Guid? sagaId, Type messageType, string typeLabel)
    {
        var headers = new Dictionary<string, object?>();
        dynamic msg = message;

        // SagaId preference: message.SagaId if available, else sagaId parameter
        Guid? effectiveSagaId = RabbitMqEventBusHelper.GetGuidProperty("SagaId", msg, sagaId);
        if (effectiveSagaId.HasValue)
            headers[Constants.SagaIdHeader] = effectiveSagaId.Value.ToString();

        // CorrelationId: prefer property, else effectiveSagaId or new Guid
        var correlationId = RabbitMqEventBusHelper.GetGuidProperty("CorrelationId", msg, effectiveSagaId ?? Guid.NewGuid());
        headers[Constants.CorrelationIdHeader] = correlationId.ToString();

        // MessageId
        var messageId = RabbitMqEventBusHelper.GetGuidProperty("MessageId", msg);
        if (messageId.HasValue) headers[Constants.MessageIdHeader] = messageId.Value.ToString();

        // ParentMessageId (first instance, only if present)
        var parentMessageId = RabbitMqEventBusHelper.GetGuidProperty("ParentMessageId", msg);
        if (parentMessageId.HasValue) headers[Constants.ParentMessageIdHeader] = parentMessageId.Value.ToString();

        // Timestamp
        var timestamp = RabbitMqEventBusHelper.GetDateTimeProperty("Timestamp", msg);
        if (timestamp.HasValue) headers[Constants.TimestampHeader] = timestamp.Value.ToString("o");

        // ApplicationId
        var applicationId = RabbitMqEventBusHelper.GetStringProperty("ApplicationId", msg);
        if (!string.IsNullOrWhiteSpace(applicationId)) headers[Constants.ApplicationIdHeader] = applicationId;

        // ParentMessageId (again, fallback/generation)
        var parentMsgIdForFallback = RabbitMqEventBusHelper.GetGuidProperty("ParentMessageId", msg);
        headers[Constants.ParentMessageIdHeader] = (parentMsgIdForFallback ?? Guid.NewGuid()).ToString();

        headers[typeLabel] = messageType.FullName;
        headers[Constants.PublishedAtHeader] = DateTime.UtcNow.ToString("o");

        return headers;
    }

    private static Guid? GetGuidProperty(string propName, dynamic msg, Guid? fallback = null)
    {
        try
        {
            var val = msg?.GetType().GetProperty(propName)?.GetValue(msg, null);
            if (val is Guid g && g != Guid.Empty)
                return g;
            if (val is string s && Guid.TryParse(s, out var gs) && gs != Guid.Empty)
                return gs;
        }
        catch
        {
            // ignored
        }

        return fallback;
    }

    private static string? GetStringProperty(string propName, dynamic msg, string? fallback = null)
    {
        try
        {
            var val = msg?.GetType().GetProperty(propName)?.GetValue(msg, null);
            if (val is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }
        catch
        {
            // ignored
        }

        return fallback;
    }

    private static DateTime? GetDateTimeProperty(string propName, dynamic msg, DateTime? fallback = null)
    {
        try
        {
            var val = msg?.GetType().GetProperty(propName)?.GetValue(msg, null);
            if (val is DateTime dt && dt != default)
                return dt;
            if (val is string s && DateTime.TryParse(s, out var dtStr) && dtStr != default)
                return dtStr;
        }
        catch
        {
            // ignored
        }

        return fallback;
    }
}