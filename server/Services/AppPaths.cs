namespace PWExtendedApp.Server.Services;

/// <summary>
/// Resolves paths that live outside the app folder (estructura.json, salidas)
/// regardless of whether the app runs from the repo (dotnet run), from a
/// published folder, or as a Windows Service.
/// </summary>
public static class AppPaths
{
    // estructura.json now lives inside the server folder (server\app\estructura.json).
    private static readonly string[] EstructuraRelatives =
    [
        @"app\estructura.json",
        @"server\app\estructura.json"
    ];

    public const string OutputRelative = @"salidas";

    public static string? FindEstructuraJson(string? contentRoot = null, string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        foreach (var relative in EstructuraRelatives)
        {
            var found = EnumerateCandidates(contentRoot, null, relative).FirstOrDefault(File.Exists);
            if (found != null) return found;
        }

        return null;
    }

    public static string? FindExistingOutputFile(string outputDir, string fileName, string? contentRoot = null)
    {
        var direct = Path.Combine(outputDir, fileName);
        if (File.Exists(direct)) return direct;

        return EnumerateCandidates(contentRoot, null, Path.Combine(OutputRelative, fileName))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Walks up from the app folder looking for an existing file or directory.
    /// Useful to seed defaults without hardcoding absolute machine paths.
    /// </summary>
    public static string? FindUpward(string relative)
    {
        return EnumerateCandidates(null, null, relative)
            .FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
    }

    private static IEnumerable<string> EnumerateCandidates(string? contentRoot, string? configuredPath, string relative)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        var bases = new List<string?>();
        if (!string.IsNullOrWhiteSpace(contentRoot)) bases.Add(contentRoot);
        bases.Add(AppContext.BaseDirectory);
        bases.Add(Directory.GetCurrentDirectory());

        foreach (var root in bases
                     .Where(b => !string.IsNullOrWhiteSpace(b))
                     .Select(b => Path.GetFullPath(b!))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dir = new DirectoryInfo(root);
            for (int depth = 0; depth < 6 && dir != null; depth++, dir = dir.Parent)
            {
                yield return Path.Combine(dir.FullName, relative);
            }
        }
    }
}
