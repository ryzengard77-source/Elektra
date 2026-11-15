using System.IO;

namespace Elektra.Audio;

public static class VoskModelPathResolver
{
    private const int MaximumNestedModelDepth = 4;
    private const int MaximumParentSearchLevels = 5;

    public static string Resolve(string? configuredPath, string baseDirectory)
    {
        string fullBaseDirectory = Path.GetFullPath(baseDirectory);
        string configuredModelPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine("Models", "vosk-model-small-en-us-0.15")
                : configuredPath,
            fullBaseDirectory);

        if (IsModelDirectory(configuredModelPath))
            return configuredModelPath;

        var modelRoots = new List<string>();
        string? currentDirectory = fullBaseDirectory;
        for (int level = 0; level < MaximumParentSearchLevels && currentDirectory != null; level++)
        {
            AddUnique(modelRoots, Path.Combine(currentDirectory, "Models"));

            if (level == 1 && Directory.Exists(currentDirectory))
            {
                foreach (string siblingOutputDirectory in Directory.EnumerateDirectories(currentDirectory)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    AddUnique(modelRoots, Path.Combine(siblingOutputDirectory, "Models"));
                }
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        foreach (string modelRoot in modelRoots)
        {
            string? modelDirectory = FindModelDirectory(modelRoot);
            if (modelDirectory != null)
                return modelDirectory;
        }

        return configuredModelPath;
    }

    private static string? FindModelDirectory(string root)
    {
        if (IsModelDirectory(root))
            return root;
        if (!Directory.Exists(root))
            return null;

        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));
        while (pending.TryDequeue(out var current))
        {
            if (current.Depth >= MaximumNestedModelDepth)
                continue;

            foreach (string directory in Directory.EnumerateDirectories(current.Path)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    continue;
                if (IsModelDirectory(directory))
                    return directory;
                pending.Enqueue((directory, current.Depth + 1));
            }
        }

        return null;
    }

    private static bool IsModelDirectory(string path) =>
        Directory.Exists(Path.Combine(path, "am")) && Directory.Exists(Path.Combine(path, "conf"));

    private static void AddUnique(List<string> paths, string candidate)
    {
        if (!paths.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            paths.Add(candidate);
    }
}
