namespace Lycia.Observability;

using System.Diagnostics;

/// <summary>
/// Provides a singleton holder for the ActivitySource instance used in the Lycia observability context.
/// </summary>
/// <remarks>
/// This class facilitates the creation and management of diagnostic activities
/// scoped to the Lycia ActivitySource name.
/// </remarks>
public sealed class LyciaActivitySourceHolder : IDisposable
{
    /// <summary>
    /// Provides a singleton holder for the <see cref="ActivitySource"/> instance used in the Lycia observability context.
    /// </summary>
    /// <remarks>
    /// This class facilitates the creation and management of diagnostic activities
    /// scoped to the Lycia ActivitySource name.
    /// </remarks>
    public LyciaActivitySourceHolder(ActivitySource source)
    {
        Source = source;
    }
    /// <summary>
    /// Represents the name of the ActivitySource instance used for diagnostic activities
    /// within the Lycia observability context.
    /// </summary>
    /// <remarks>
    /// This constant defines the identifying name for the ActivitySource, enabling
    /// consistent tracing and monitoring of activities scoped to "Lycia".
    /// </remarks>
    public const string Name = "Lycia";

    /// <summary>
    /// Provides the ActivitySource instance utilized for creating and managing diagnostic activities
    /// within the Lycia observability framework.
    /// </summary>
    /// <remarks>
    /// This property encapsulates the ActivitySource associated with the "Lycia" name,
    /// ensuring consistency in tracing and observability across activities.
    /// </remarks>
    public ActivitySource Source { get; } = new(Name);

    /// <summary>
    /// Releases the resources used by the <see cref="LyciaActivitySourceHolder"/> class, including
    /// the encapsulated <see cref="ActivitySource"/> instance.
    /// </summary>
    /// <remarks>
    /// This method is responsible for disposing of the <see cref="ActivitySource"/> instance
    /// to ensure proper release of all allocated resources and avoid potential memory leaks.
    /// </remarks>
    public void Dispose() => Source.Dispose();
}