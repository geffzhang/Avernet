namespace Ocb.Architecture.Tests;

internal static class RepositoryPaths
{
    public static DirectoryInfo Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory ?? throw new DirectoryNotFoundException("Repository root containing AGENTS.md was not found.");
    }
}
