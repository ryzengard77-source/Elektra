using Elektra.Platform;

namespace Elektra.Tests;

[TestClass]
public sealed class GoogleEarthCommandTests
{
    [TestMethod]
    [DataRow("open google earth")]
    [DataRow("open the google earth")]
    [DataRow("launch google earth")]
    [DataRow("launch the google earth")]
    [DataRow("start google earth")]
    [DataRow("google earth")]
    public void IsGoogleEarthOpenCommand_RecognizesOpenEarthPhrases(string command)
    {
        Assert.IsTrue(WindowsToolLauncher.IsGoogleEarthOpenCommand(command));
    }

    [TestMethod]
    [DataRow("open paris")]
    [DataRow("find paris")]
    [DataRow("locate 10 downing street")]
    public void IsGoogleEarthOpenCommand_DoesNotTreatPlaceSearchAsEarthLaunch(string command)
    {
        Assert.IsFalse(WindowsToolLauncher.IsGoogleEarthOpenCommand(command));
    }
}
