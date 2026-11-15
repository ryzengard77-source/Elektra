using Elektra.Security;

namespace Elektra.Tests;

[TestClass]
public sealed class DirectoryEncryptionPathTests
{
    private readonly DirectoryEncryptionService _service = new();

    [TestMethod]
    public void GetProtectedDirectory_RejectsPathsOutsideTheAllowlist()
    {
        Assert.IsNull(_service.GetProtectedDirectory("Desktop"));
        Assert.IsNull(_service.GetProtectedDirectory("C:\\"));
        Assert.IsNull(_service.GetProtectedDirectory("Pictures\\..\\Desktop"));
    }

    [TestMethod]
    public void GetProtectedDirectory_ResolvesAllowedNamesCaseInsensitively()
    {
        Assert.AreEqual(
            Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            _service.GetProtectedDirectory("pIcTuReS"));
        Assert.AreEqual(
            Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
            _service.GetProtectedDirectory("DOWNLOADS"));
    }
}
