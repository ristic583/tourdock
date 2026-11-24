using AwesomeAssertions;

namespace TourDock.ArchitectureTests;

/// <summary>
/// Size limits from docs/conventions.md §7 for C#: files up to 300 lines, lines up to 140 characters. Files that were
/// already over on 2026-11-16 are listed in size-baseline.txt and may not get worse (a ratchet); new files must comply.
/// Generated migrations and the demo data catalog are exempt.
/// </summary>
public sealed class SizeLimitTests
{
    private const int MaxLines = 300;
    private const int MaxLineLength = 140;

    [Fact]
    public void Source_files_stay_within_the_size_limits()
    {
        var root = RepositoryRoot();
        var baseline = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "size-baseline.txt"))
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split(' '))
            .ToDictionary(p => p[0], p => (Lines: int.Parse(p[1]), LongLines: int.Parse(p[2])));

        var problems = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/Migrations/", StringComparison.Ordinal) || relative.EndsWith("/DemoCatalog.cs", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            var longLines = lines.Count(l => l.Length > MaxLineLength);
            var (maxLines, maxLong) = baseline.TryGetValue(relative, out var known)
                ? (Math.Max(known.Lines, MaxLines), known.LongLines)
                : (MaxLines, 0);

            if (lines.Length > maxLines)
            {
                problems.Add($"{relative}: {lines.Length} lines (max {maxLines})");
            }

            if (longLines > maxLong)
            {
                problems.Add($"{relative}: {longLines} lines longer than {MaxLineLength} characters (max {maxLong})");
            }
        }

        problems.Should().BeEmpty("split long files and wrap long lines (see docs/conventions.md §7)");
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TourDock.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("TourDock.slnx not found above " + AppContext.BaseDirectory);
    }
}
