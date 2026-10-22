using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Instances;

namespace AxoClient.Tests;

public class ModLockTests
{
    private static ContentEntry Mod(string file, string? project = null, ContentSource source = ContentSource.Modrinth) =>
        new() { File = file, Name = file, ProjectId = project, Source = source };

    [Fact]
    public void OffLocksNothing()
    {
        var modLock = new ModLock();
        Assert.False(modLock.IsLocked(Mod("sodium.jar", "AANobbMI")));
        Assert.True(modLock.CanAdd());
    }

    [Fact]
    public void PackLocksOnlyModsPresentWhenEnabled()
    {
        var modLock = new ModLock();
        modLock.SetLevel(ModLockLevel.Pack, [Mod("sodium.jar", "AANobbMI"), Mod("own.jar", source: ContentSource.Local)]);
        Assert.True(modLock.IsLocked(Mod("sodium-0.6.jar", "AANobbMI")));
        Assert.True(modLock.IsLocked(Mod("own.jar", source: ContentSource.Local)));
        Assert.False(modLock.IsLocked(Mod("added-later.jar", "P7dR8mSH")));
        Assert.True(modLock.CanAdd());
    }

    [Fact]
    public void FullLocksEverythingButTheLauncherMod()
    {
        var modLock = new ModLock();
        modLock.SetLevel(ModLockLevel.Full, []);
        Assert.True(modLock.IsLocked(Mod("anything.jar")));
        Assert.False(modLock.IsLocked(Mod("axoclient.jar", source: ContentSource.AxoClient)));
        Assert.False(modLock.CanAdd());
        Assert.Throws<AxoException>(modLock.ThrowIfCannotAdd);
    }

    [Fact]
    public void SwitchingBetweenLevelsKeepsPackList()
    {
        var modLock = new ModLock();
        modLock.SetLevel(ModLockLevel.Pack, [Mod("a.jar", "A")]);
        modLock.SetLevel(ModLockLevel.Full, [Mod("a.jar", "A"), Mod("b.jar", "B")]);
        modLock.SetLevel(ModLockLevel.Pack, [Mod("a.jar", "A"), Mod("b.jar", "B")]);
        Assert.Equal(["project:A"], modLock.PackMods);
    }

    [Fact]
    public void OffClearsPackList()
    {
        var modLock = new ModLock();
        modLock.SetLevel(ModLockLevel.Pack, [Mod("a.jar", "A")]);
        modLock.SetLevel(ModLockLevel.Off, []);
        Assert.Empty(modLock.PackMods);
    }
}
