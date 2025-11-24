using Microsoft.Extensions.Logging;

namespace Lycia.Extensions.Configurations;

/// <summary>
/// Represents the configuration options for logging behavior within the application.
/// </summary>
public sealed class LoggingOptions
{
    // Defaults
    /// <summary>
    /// Gets or sets the minimum log level required for messages to be logged.
    /// </summary>
    /// <remarks>
    /// Messages with a log level below the specified minimum level will be ignored.
    /// The default value is <see cref="LogLevel.Information"/>.
    /// </remarks>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    // Saga log behavior
    /// <summary>
    /// Gets or sets a value indicating whether message headers should be included in the log output.
    /// </summary>
    /// <remarks>
    /// When set to <c>true</c>, message headers will be logged. This can be useful for debugging or tracing purposes,
    /// but may expose sensitive information if headers contain confidential data. The default value is <c>false</c>.
    /// </remarks>
    public bool IncludeMessageHeaders { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether the message payload should be included in the logs.
    /// </summary>
    /// <remarks>
    /// When set to <c>true</c>, the logged messages will include their associated payloads.
    /// This can be useful for debugging or tracing purposes, but may increase log size or expose sensitive information.
    /// Ensure careful consideration when enabling this property in a production environment.
    /// The default value is <c>false</c>.
    /// </remarks>
    public bool IncludeMessagePayload { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum length of the message payload included in logs.
    /// </summary>
    /// <remarks>
    /// If the message payload exceeds the specified maximum length, it may be truncated in the logs.
    /// This property allows controlling the size of logged payloads to manage log verbosity and storage.
    /// The default value is 2048.
    /// </remarks>
    public int PayloadMaxLength { get; set; } = 2048;

    // Snipping / Redaction
    /// <summary>
    /// Gets or sets the collection of header keys that should be redacted in log messages.
    /// </summary>
    /// <remarks>
    /// The specified keys will have their values redacted to ensure sensitive or confidential
    /// information is not exposed in logs. By default, no header keys are redacted.
    /// </remarks>
    public string[] RedactedHeaderKeys { get; set; } = [];

    // Templates (optional)
    /// <summary>
    /// Gets or sets the template used at the start of a logging message flow or process.
    /// </summary>
    /// <remarks>
    /// This property allows customization of the log message format for the initiation of a process or workflow.
    /// It can be null if no specific template is required. The template may include placeholders for dynamic content.
    /// </remarks>
    public string? StartTemplate { get; set; }
    /// <summary>
    /// Gets or sets the template used for logging successful operations.
    /// </summary>
    /// <remarks>
    /// This template determines how success messages are formatted in the logs.
    /// If not provided, no specific formatting will be applied for success messages.
    /// </remarks>
    public string? SuccessTemplate { get; set; }
    /// <summary>
    /// 
    /// </summary>
    public string? ErrorTemplate { get; set; }
}