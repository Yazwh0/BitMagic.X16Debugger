using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests;

[TestClass]
public class X16PathTests
{
    [TestMethod]
    [DataRow("LEVEL1.PRG", "LEVEL1.PRG")]
    [DataRow("LEVEL1.PRG,P,R", "LEVEL1.PRG")]
    [DataRow("@:LEVEL1.PRG", "LEVEL1.PRG")]
    [DataRow("0:LEVEL1.PRG", "LEVEL1.PRG")]
    [DataRow("@0:LEVEL1.PRG,S,W", "LEVEL1.PRG")]
    [DataRow("//GAME/:LEVEL1.PRG", "/GAME/LEVEL1.PRG")]
    [DataRow("/GAME/:LEVEL1.PRG", "GAME/LEVEL1.PRG")]
    [DataRow("1//GAME/DATA/:LEVEL1.PRG", "/GAME/DATA/LEVEL1.PRG")]
    [DataRow("//:LEVEL1.PRG", "/LEVEL1.PRG")]
    [DataRow("/:LEVEL1.PRG", "LEVEL1.PRG")]     // a single '/' isn't a path
    [DataRow("GAME:LEVEL1.PRG", "LEVEL1.PRG")]  // nor is anything not wrapped in '/'
    [DataRow("GAME/LEVEL1.PRG", "GAME/LEVEL1.PRG")]
    [DataRow("/GAME/LEVEL1.PRG", "/GAME/LEVEL1.PRG")]
    public void ToUnixPath(string dosName, string expected)
    {
        Assert.AreEqual(expected, X16Path.ToUnixPath(dosName));
    }

    [TestMethod]
    [DataRow("/", "LEVEL1.PRG", "/LEVEL1.PRG")]
    [DataRow("/GAME", "LEVEL1.PRG", "/GAME/LEVEL1.PRG")]
    [DataRow("/GAME", "DATA/LEVEL1.PRG", "/GAME/DATA/LEVEL1.PRG")]
    [DataRow("/GAME", "/LEVEL1.PRG", "/LEVEL1.PRG")]
    [DataRow("/GAME/DATA", "../LEVEL1.PRG", "/GAME/LEVEL1.PRG")]
    [DataRow("/GAME", "./LEVEL1.PRG", "/GAME/LEVEL1.PRG")]
    [DataRow("/", "../../LEVEL1.PRG", "/LEVEL1.PRG")]
    [DataRow("/GAME", "..", "/")]
    [DataRow("/GAME/", "DATA//LEVEL1.PRG", "/GAME/DATA/LEVEL1.PRG")]
    public void Resolve(string currentDirectory, string unixPath, string expected)
    {
        Assert.AreEqual(expected, X16Path.Resolve(currentDirectory, unixPath));
    }

    [TestMethod]
    [DataRow("/GAME", "LEVEL1.PRG", "/GAME/LEVEL1.PRG")]
    [DataRow("/GAME", "//:LEVEL1.PRG", "/LEVEL1.PRG")]
    [DataRow("/GAME", "/DATA/:LEVEL1.PRG,P", "/GAME/DATA/LEVEL1.PRG")]
    [DataRow("/GAME", "//DATA/:LEVEL1.PRG", "/DATA/LEVEL1.PRG")]
    public void ResolveDosName(string currentDirectory, string dosName, string expected)
    {
        Assert.AreEqual(expected, X16Path.ResolveDosName(currentDirectory, dosName));
    }

    [TestMethod]
    [DataRow("/GAME/LEVEL1.PRG", "LEVEL1.PRG", "GAME/LEVEL1.PRG")]
    [DataRow("/LEVEL1.PRG", "LEVEL1.PRG", "LEVEL1.PRG")]
    [DataRow("/", "", "")]
    public void FilenameAndSdCardPath(string path, string filename, string sdCardPath)
    {
        Assert.AreEqual(filename, X16Path.GetFilename(path));
        Assert.AreEqual(sdCardPath, X16Path.ToSdCardPath(path));
    }

    [TestMethod]
    [DataRow("GAME\\LEVEL1.PRG")]
    [DataRow("\\GAME\\LEVEL1.PRG")]
    [DataRow("GAME/LEVEL1.PRG")]
    public void FromSdCardPath(string sdCardPath)
    {
        Assert.AreEqual("/GAME/LEVEL1.PRG", X16Path.FromSdCardPath(sdCardPath));
    }
}
