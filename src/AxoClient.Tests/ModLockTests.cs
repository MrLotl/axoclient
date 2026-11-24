using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Instances;

namespace AxoClient.Tests
{
    public class ModLockTests
    {
        private ContentEntry mod(string file, string? project = null, ContentSource source = ContentSource.Modrinth) =>
            new() { File = file, Name = file, ProjectId = project, Source = source };

        [Fact]
        public void OffLocksNothing()
        {
            var modLock = new ModLock();
            Assert.False(modLock.IsLocked(mod("sodium.jar", "AANobbMI")));
            Assert.True(modLock.CanAdd());
        }

        [Fact]
        public void PackLocksOnlyModsPresentWhenEnabled()
        {
            var modLock = new ModLock();
            modLock.SetLevel(ModLockLevel.Pack, [mod("sodium.jar", "AANobbMI"), mod("own.jar", source: ContentSource.Local)]);
            Assert.True(modLock.IsLocked(mod("sodium-0.6.jar", "AANobbMI")));
            Assert.True(modLock.IsLocked(mod("own.jar", source: ContentSource.Local)));
            Assert.False(modLock.IsLocked(mod("added-later.jar", "P7dR8mSH")));
            Assert.True(modLock.CanAdd());
        }

        [Fact]
        public void FullLocksEverythingButTheLauncherMod()
        {
            var modLock = new ModLock();
            modLock.SetLevel(ModLockLevel.Full, []);
            Assert.True(modLock.IsLocked(mod("anything.jar")));
            Assert.False(modLock.IsLocked(mod("axoclient.jar", source: ContentSource.AxoClient)));
            Assert.False(modLock.CanAdd());
            Assert.Throws<AxoException>(modLock.ThrowIfCannotAdd);
        }

        [Fact]
        public void SwitchingBetweenLevelsKeepsPackList()
        {
            var modLock = new ModLock();
            modLock.SetLevel(ModLockLevel.Pack, [mod("a.jar", "A")]);
            modLock.SetLevel(ModLockLevel.Full, [mod("a.jar", "A"), mod("b.jar", "B")]);
            modLock.SetLevel(ModLockLevel.Pack, [mod("a.jar", "A"), mod("b.jar", "B")]);
            Assert.Equal(["project:A"], modLock.PackMods);
        }

        [Fact]
        public void OffClearsPackList()
        {
            var modLock = new ModLock();
            modLock.SetLevel(ModLockLevel.Pack, [mod("a.jar", "A")]);
            modLock.SetLevel(ModLockLevel.Off, []);
            Assert.Empty(modLock.PackMods);
        }
    }
}
