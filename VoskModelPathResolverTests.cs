using Elektra.Audio;

namespace Elektra.Tests;

[TestClass]
public sealed class VoskModelPathResolverTests
{
    [TestMethod]
    public void Resolve_FindsModelInNeighboringFrameworkOutputAfterTargetUpgrade()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"ElektraModelTest-{Guid.NewGuid():N}");
        string currentOutput = Path.Combine(temporaryRoot, "bin", "Debug", "net10.0-windows");
        string previousOutputModel = Path.Combine(
            temporaryRoot,
            "bin",
            "Debug",
            "net9.0-windows",
            "Models",
            "vosk-model-small-en-us-0.15",
            "vosk-model-small-en-us-0.15");
        Directory.CreateDirectory(currentOutput);
        Directory.CreateDirectory(Path.Combine(previousOutputModel, "am"));
        Directory.CreateDirectory(Path.Combine(previousOutputModel, "conf"));

        try
        {
            string resolvedPath = VoskModelPathResolver.Resolve(
                "Models/vosk-model-small-en-us-0.15",
                currentOutput);

            Assert.AreEqual(Path.GetFullPath(previousOutputModel), resolvedPath);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [TestMethod]
    public void Resolve_PrefersAnExplicitConfiguredModelPath()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"ElektraModelTest-{Guid.NewGuid():N}");
        string configuredModel = Path.Combine(temporaryRoot, "custom-model");
        Directory.CreateDirectory(Path.Combine(configuredModel, "am"));
        Directory.CreateDirectory(Path.Combine(configuredModel, "conf"));

        try
        {
            string resolvedPath = VoskModelPathResolver.Resolve(configuredModel, temporaryRoot);

            Assert.AreEqual(Path.GetFullPath(configuredModel), resolvedPath);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
