using System.Xml.Linq;

namespace Ocb.Architecture.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] FrameworkPackages =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Orleans.Server",
        "Minio",
        "Qdrant",
        "Sonnet"
    ];

    [Theory]
    [InlineData("Ocb.Contracts")]
    public void ContractProjectsHaveNoProjectReferences(string projectName)
    {
        var project = LoadProject(projectName);
        Assert.Empty(FindElements(project, "ProjectReference"));
    }

    [Fact]
    public void CoreReferencesOnlyContractsAndPluginApi()
    {
        var project = LoadProject("Ocb.Core");
        var references = FindElements(project, "ProjectReference")
            .Select(GetIncludeAttribute)
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!))
            .Order()
            .ToArray();

        Assert.Equal(["Ocb.Contracts", "Ocb.PluginApi"], references);
    }

    [Theory]
    [InlineData("Ocb.Contracts")]
    [InlineData("Ocb.Core")]
    [InlineData("Ocb.PluginApi")]
    public void CoreAndContractsDoNotReferenceFrameworkImplementations(string projectName)
    {
        var project = LoadProject(projectName);
        var packages = FindElements(project, "PackageReference")
            .Select(GetIncludeAttribute)
            .Where(include => include is not null)
            .Select(include => include!)
            .ToArray();

        Assert.DoesNotContain(packages, package => FrameworkPackages.Any(prefix =>
            package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void RawEnvironmentAccessIsConfinedToConfigurationProject()
    {
        var sourceRoot = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src");
        var offenders = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsExcludedEnvironmentScanPath(path))
            .Where(path => File.ReadAllText(path).Contains("Environment.GetEnvironmentVariable", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static XDocument LoadProject(string projectName)
    {
        var path = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src", projectName, $"{projectName}.csproj");
        return XDocument.Load(path);
    }

    private static IEnumerable<XElement> FindElements(XDocument project, string elementLocalName)
    {
        return project.Descendants().Where(element =>
            string.Equals(element.Name.LocalName, elementLocalName, StringComparison.Ordinal));
    }

    private static string? GetIncludeAttribute(XElement element)
    {
        return element.Attribute("Include")?.Value;
    }

    private static bool IsExcludedEnvironmentScanPath(string path)
    {
        return PathContainsSegment(path, "Ocb.Configuration")
            || PathContainsSegment(path, "bin")
            || PathContainsSegment(path, "obj");
    }

    private static bool PathContainsSegment(string path, string segment)
    {
        var segments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(candidate => string.Equals(candidate, segment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChannelsHasNoProjectReferences()
    {
        var project = LoadProject("Ocb.Channels");
        Assert.Empty(FindElements(project, "ProjectReference"));
    }

    [Fact]
    public void GatewayReferencesOnlyAllowedProjects()
    {
        var project = LoadProject("Ocb.Gateway");
        var references = FindElements(project, "ProjectReference")
            .Select(GetIncludeAttribute)
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!))
            .Order()
            .ToArray();

        // Gateway must reference Contracts, PluginApi, and Configuration.
        // It must NOT reference Ocb.Core (core logic does not belong in the web host).
        Assert.Equal(["Ocb.Configuration", "Ocb.Contracts", "Ocb.PluginApi"], references);
        Assert.DoesNotContain("Ocb.Core", references);
    }
}
