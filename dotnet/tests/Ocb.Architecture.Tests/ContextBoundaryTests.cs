using System.Text.Json;
using System.Xml.Linq;

namespace Ocb.Architecture.Tests;

public sealed class ContextBoundaryTests
{
    [Fact]
    public void EveryProductionProjectDeclaresAValidContextBoundary()
    {
        var sourceRoot = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src");
        var projects = Directory.GetDirectories(sourceRoot, "Ocb.*", SearchOption.TopDirectoryOnly);
        var productionProjectNames = projects
          .Select(Path.GetFileName)
          .Where(name => !string.IsNullOrWhiteSpace(name))
          .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var path = Path.Combine(project, "context-boundary.json");
            Assert.True(File.Exists(path), $"Missing {path}");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("purpose").GetString()));
            Assert.Equal(JsonValueKind.Array, root.GetProperty("provides").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("consumes").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("internal_dependencies").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("change_impact").GetString()));

            var declaredDependencies = root.GetProperty("internal_dependencies")
              .EnumerateArray()
              .Select(element => element.GetString())
              .Where(value => value is not null)
              .ToHashSet(StringComparer.Ordinal);

            Assert.DoesNotContain(
              declaredDependencies,
              dependency => !productionProjectNames.Contains(dependency));

            var projectFile = Directory.GetFiles(project, "*.csproj", SearchOption.TopDirectoryOnly).Single();
            var actualDependencies = XDocument.Load(projectFile)
              .Descendants()
              .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
              .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
              .Select(Path.GetFileNameWithoutExtension)
              .ToArray();

            Assert.DoesNotContain(actualDependencies, dependency => !declaredDependencies.Contains(dependency));
        }
    }
}
