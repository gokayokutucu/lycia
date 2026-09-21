// Copyright 2023 Lycia Contributors
// Licensed under the Apache License, Version 2.0
// https://www.apache.org/licenses/LICENSE-2.0
using System.Text.RegularExpressions;
using Lycia.Tests.Infrastructure;
using Newtonsoft.Json.Linq;

namespace Lycia.Tests;

/// <summary>
/// Guards the supported-infrastructure contract in <c>infrastructure-versions.json</c>. A test image, a CI
/// service container, a compose file or the documented table can only change together with the contract, so
/// compatibility is never altered by quietly bumping a container tag.
/// </summary>
public class InfrastructureContractTests
{
    private static readonly string[] Integrations = ["rabbitmq", "redis", "postgresql", "sqlserver", "kafka", "nats"];

    /// <summary>The image repository each integration is run from. Variants of the same repository are accepted.</summary>
    private static readonly Dictionary<string, string[]> Repositories = new()
    {
        ["rabbitmq"] = ["rabbitmq"],
        ["redis"] = ["redis"],
        ["postgresql"] = ["postgres"],
        ["sqlserver"] = ["mcr.microsoft.com/mssql/server"],
        ["kafka"] = ["confluentinc/cp-kafka", "apache/kafka"],
        ["nats"] = ["nats"]
    };

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Lycia.sln")))
                return directory.FullName;
        throw new InvalidOperationException("Repository root (Lycia.sln) was not found.");
    }

    private static JToken Entry(string integration, string track) => InfrastructureVersions.Document["integrations"]![integration]![track]!;

    private static (string Repository, string Tag) Split(string image)
    {
        var colon = image.LastIndexOf(':');
        Assert.True(colon > 0 && colon < image.Length - 1, $"'{image}' has no explicit tag.");
        return (image.Substring(0, colon), image.Substring(colon + 1));
    }

    private static Version Parse(string version) =>
        Version.Parse(version.Contains('.') ? version : version + ".0");

    [Fact]
    public void Every_integration_defines_a_minimum_and_a_current_with_an_explicit_tag_naming_its_series()
    {
        foreach (var integration in Integrations)
        foreach (var track in new[] { "minimum", "current" })
        {
            var entry = Entry(integration, track);
            var version = (string)entry["version"]!;
            var (repository, tag) = Split((string)entry["image"]!);

            Assert.False(string.IsNullOrWhiteSpace(version), $"{integration}/{track} has no version.");
            Assert.Contains(repository, Repositories[integration]);
            Assert.NotEqual("latest", tag);
            Assert.DoesNotContain("latest", tag, StringComparison.OrdinalIgnoreCase);
            // The tag must name the series the contract claims, so the two cannot drift apart.
            Assert.True(tag.Contains(version) || IsConfluentMapping(integration, tag, version),
                $"{integration}/{track}: tag '{tag}' does not name version series '{version}'.");
        }
    }

    // Confluent Platform versions its images differently from Apache Kafka (cp-kafka 7.7.1 is Kafka 3.7.1); the
    // contract states the Kafka series and pins the exact Confluent image, so that mapping is checked here.
    private static bool IsConfluentMapping(string integration, string tag, string version) =>
        integration == "kafka" && tag.StartsWith("7.", StringComparison.Ordinal) && Regex.IsMatch(version, @"^3\.\d+$")
        || integration == "kafka" && tag.StartsWith("8.", StringComparison.Ordinal) && Regex.IsMatch(version, @"^4\.\d+$");

    [Fact]
    public void The_minimum_is_never_newer_than_the_current_version()
    {
        foreach (var integration in Integrations)
            Assert.True(Parse((string)Entry(integration, "minimum")["version"]!) <= Parse((string)Entry(integration, "current")["version"]!),
                $"{integration}: minimum is newer than current.");
    }

    [Fact]
    public void Test_projects_take_every_image_from_the_contract()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\.WithImage\(\s*""" ))
            .Select(path => Path.GetRelativePath(RepositoryRoot(), path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files hard-code a container image instead of using InfrastructureVersions.Image(...): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Ci_and_compose_stacks_run_the_current_series_of_every_integration()
    {
        var root = RepositoryRoot();
        var files = new List<string> { Path.Combine(root, ".github", "workflows", "dotnet.yml") };
        files.AddRange(Directory.EnumerateFiles(root, "docker-compose*.yml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")));

        var problems = new List<string>();
        foreach (var file in files.Where(File.Exists))
        foreach (Match match in Regex.Matches(File.ReadAllText(file), @"image:\s*([^\s,}]+)"))
        {
            var image = match.Groups[1].Value;
            if (!image.Contains(':')) continue;
            var (repository, tag) = Split(image);
            foreach (var integration in Integrations.Where(i => Repositories[i].Contains(repository)))
            {
                var current = (string)Entry(integration, "current")["image"]!;
                var (_, currentTag) = Split(current);
                var series = (string)Entry(integration, "current")["version"]!;
                // A variant (for example -alpine) of the same series is fine; a different series is not.
                if (tag != currentTag && !tag.StartsWith(series, StringComparison.Ordinal) && !IsConfluentMapping(integration, tag, series))
                    problems.Add($"{Path.GetRelativePath(root, file)}: {image} (contract current: {current})");
            }
        }

        Assert.True(problems.Count == 0, "Images that disagree with infrastructure-versions.json: " + string.Join("; ", problems));
    }

    [Fact]
    public void The_readme_documents_the_minimum_and_tested_versions_of_every_integration()
    {
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));
        var start = readme.IndexOf("## Supported Infrastructure Versions", StringComparison.Ordinal);
        Assert.True(start >= 0, "README has no 'Supported Infrastructure Versions' section.");
        var end = readme.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        readme = readme.Substring(start, (end < 0 ? readme.Length : end) - start);
        var names = new Dictionary<string, string>
        {
            ["rabbitmq"] = "RabbitMQ", ["redis"] = "Redis", ["postgresql"] = "PostgreSQL",
            ["sqlserver"] = "SQL Server", ["kafka"] = "Kafka", ["nats"] = "NATS"
        };

        foreach (var integration in Integrations)
        {
            var row = readme.Split('\n').FirstOrDefault(line => line.TrimStart().StartsWith($"| {names[integration]} "));
            Assert.True(row != null, $"README has no compatibility row for {names[integration]}.");
            Assert.Contains((string)Entry(integration, "minimum")["version"]!, row);
            Assert.Contains((string)Entry(integration, "current")["version"]!, row);
        }
    }
}
