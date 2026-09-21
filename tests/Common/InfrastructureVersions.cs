// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
// Shared (linked) by every test project that starts real infrastructure.
using System.IO;
using Newtonsoft.Json.Linq;

namespace Lycia.Tests.Infrastructure;

/// <summary>
/// The test images for every external system Lycia integrates with, read from the repository's single
/// <c>infrastructure-versions.json</c>. Tests never hard-code an image tag: the compatibility contract lives in
/// that file, and a tag can only change by editing it (and the documentation the contract test checks it against).
/// </summary>
/// <remarks>
/// Two tracks are defined per integration. <c>current</c> is the recent version exercised by normal CI;
/// <c>minimum</c> is the lowest version Lycia supports. Set <see cref="TrackVariable"/> to <c>minimum</c> to run
/// the same suites against the minimum versions. A single image can be overridden with
/// <c>LYCIA_TEST_{INTEGRATION}_IMAGE</c> (for example <c>LYCIA_TEST_RABBITMQ_IMAGE</c>), which is how a candidate
/// version is tried before it is written into the contract.
/// </remarks>
public static class InfrastructureVersions
{
    public const string TrackVariable = "LYCIA_TEST_INFRA_TRACK";

    private static readonly Lazy<JObject> Contract = new(Load);

    /// <summary>Gets the selected track: <c>current</c> (default) or <c>minimum</c>.</summary>
    public static string Track
    {
        get
        {
            var track = Environment.GetEnvironmentVariable(TrackVariable);
            return string.IsNullOrWhiteSpace(track) ? "current" : track!.Trim().ToLowerInvariant();
        }
    }

    /// <summary>Gets the image for <paramref name="integration"/> (rabbitmq, redis, postgresql, sqlserver, kafka, nats).</summary>
    public static string Image(string integration)
    {
        var overrideImage = Environment.GetEnvironmentVariable($"LYCIA_TEST_{integration.ToUpperInvariant()}_IMAGE");
        if (!string.IsNullOrWhiteSpace(overrideImage)) return overrideImage!.Trim();
        return Entry(integration, Track)["image"]!.Value<string>()!;
    }

    /// <summary>Gets the version series the selected track claims for <paramref name="integration"/>.</summary>
    public static string Version(string integration) => Entry(integration, Track)["version"]!.Value<string>()!;

    /// <summary>Gets the whole parsed contract, for tests that validate it.</summary>
    public static JObject Document => Contract.Value;

    private static JToken Entry(string integration, string track)
    {
        var integrations = (JObject)Contract.Value["integrations"]!;
        var node = integrations[integration.ToLowerInvariant()]
                   ?? throw new InvalidOperationException($"'{integration}' is not in infrastructure-versions.json.");
        return node[track] ?? throw new InvalidOperationException(
            $"'{integration}' has no '{track}' entry in infrastructure-versions.json (track '{track}' is not valid).");
    }

    private static JObject Load()
    {
        // The file is copied next to the test binaries; fall back to walking up to the repository root.
        var candidates = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var start in candidates)
        {
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "infrastructure-versions.json");
                if (File.Exists(path)) return JObject.Parse(File.ReadAllText(path));
            }
        }

        throw new FileNotFoundException("infrastructure-versions.json was not found.");
    }
}
