using System.Diagnostics.CodeAnalysis;

namespace Ocb.Runtime.Worker.Tests.Architecture;

/// <summary>
/// Hard boundary: the Runtime Worker must never depend on OpenClaw
/// assemblies, source bridges, or NuGet packages.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class NoOpenClawDependencyTests
{
    /// <summary>
    /// Verify the worker project file contains zero OpenClaw references.
    /// </summary>
    [Fact]
    public void RuntimeWorkerProject_ShouldNotReferenceOpenClawAssemblies()
    {
        var csprojPath = Path.Combine(
            RepositoryRoot(), "dotnet", "src", "Ocb.Runtime.Worker", "Ocb.Runtime.Worker.csproj");

        var projectText = File.ReadAllText(csprojPath);

        Assert.False(
            projectText.Contains("OpenClaw", StringComparison.Ordinal),
            "Runtime Worker must not depend on OpenClaw.*");
    }

    /// <summary>
    /// No source file in the worker project should import an OpenClaw namespace.
    /// </summary>
    [Fact]
    public void RuntimeWorkerSources_ShouldNotImportOpenClawNamespaces()
    {
        var srcRoot = Path.Combine(RepositoryRoot(), "dotnet", "src", "Ocb.Runtime.Worker");
        if (!Directory.Exists(srcRoot))
            return; // No sources yet — not a failure.

        var sourceFiles = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories);

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            Assert.False(
                content.Contains("OpenClaw", StringComparison.Ordinal),
                $"File {Path.GetFileName(file)} must not reference OpenClaw.");
        }
    }

    private static string RepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
            dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException(
            "Cannot locate repository root from " + AppContext.BaseDirectory);
    }
}
