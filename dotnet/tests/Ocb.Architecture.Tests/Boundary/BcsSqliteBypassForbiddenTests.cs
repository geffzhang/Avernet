using System.Diagnostics.CodeAnalysis;

namespace Ocb.Architecture.Tests.Boundary;

/// <summary>
/// Architecture boundary test: Ocb.Bcs.Client and Ocb.Fusion must never
/// reference SQLite-related types, ensuring Rust BCS data-layer isolation.
/// </summary>
[SuppressMessage("Naming", "CA1707", Justification = "xUnit test naming convention uses underscores.")]
public sealed class BcsSqliteBypassForbiddenTests
{
    private static readonly string[] BannedIdentifiers =
    [
        "SQLiteConnection",
        "SqliteConnection",
        "Microsoft.Data.Sqlite",
        "System.Data.SQLite",
        "SQLitePCL",
    ];

    [Fact]
    public void FusionProject_MustNotReferenceSqlite()
    {
        var srcDir = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src");

        var fusionSources = GetSourceFiles(srcDir, "Ocb.Fusion");
        foreach (var file in fusionSources)
        {
            var lines = File.ReadAllLines(file);
            foreach (var line in lines)
            {
                foreach (var banned in BannedIdentifiers)
                {
                    Assert.DoesNotContain(banned, line, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void BcsClientProject_MustNotReferenceSqlite()
    {
        var srcDir = Path.Combine(RepositoryPaths.Root().FullName, "dotnet", "src");

        var bcsSources = GetSourceFiles(srcDir, "Ocb.Bcs.Client");
        // If the project doesn't exist yet, the test passes vacuously
        if (bcsSources.Length == 0)
            return;

        foreach (var file in bcsSources)
        {
            var lines = File.ReadAllLines(file);
            foreach (var line in lines)
            {
                foreach (var banned in BannedIdentifiers)
                {
                    Assert.DoesNotContain(banned, line, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void DirectoryPackages_MustNotIncludeSqlite()
    {
        var packagesFile = Path.Combine(
            RepositoryPaths.Root().FullName, "dotnet", "Directory.Packages.props");
        Assert.True(File.Exists(packagesFile));

        var content = File.ReadAllText(packagesFile);
        Assert.DoesNotContain("Sqlite", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqliteConnection", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetSourceFiles(string srcRoot, string projectName)
    {
        var projectDir = Path.Combine(srcRoot, projectName);
        if (!Directory.Exists(projectDir))
            return [];

        return Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);
    }
}
