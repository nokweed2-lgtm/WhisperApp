using System;
using System.IO;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class SharedPathsTests : IDisposable
    {
        private readonly string _root;

        public SharedPathsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"whisperwin-sharedpaths-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public void ResolveSharedDirectory_FindsEmbeddedCopyNextToExe()
        {
            var exeDir = Path.Combine(_root, "publish");
            var sharedDir = Path.Combine(exeDir, "shared");
            Directory.CreateDirectory(sharedDir);
            File.WriteAllText(Path.Combine(sharedDir, "correction-prompt.md"), "template");

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Equal(sharedDir, resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_WalksUpToFindRepoSharedFolder()
        {
            // Simulate windows/WhisperWin/bin/Debug/net8.0-windows/ inside a repo checkout.
            var exeDir = Path.Combine(_root, "windows", "WhisperWin", "bin", "Debug", "net8.0-windows");
            Directory.CreateDirectory(exeDir);
            var sharedDir = Path.Combine(_root, "shared");
            Directory.CreateDirectory(sharedDir);
            File.WriteAllText(Path.Combine(sharedDir, "correction-prompt.md"), "template");

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Equal(sharedDir, resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_ReturnsNullWhenNotFoundAnywhere()
        {
            var exeDir = Path.Combine(_root, "nowhere");
            Directory.CreateDirectory(exeDir);

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Null(resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_PrefersRepoSharedFolderOverEmbeddedCopy()
        {
            // A published build sitting inside the repo checkout (e.g. dev loop, or a publish
            // output left under the source tree) has BOTH an embedded shared/ next to the exe AND
            // a repo-style shared/ further up. The repo copy must win — it's the one synced with
            // the Mac app via OneDrive; the embedded copy is a stale build-time snapshot.
            var exeDir = Path.Combine(_root, "windows", "WhisperWin", "bin", "Release", "net8.0-windows", "publish");
            Directory.CreateDirectory(exeDir);

            var embeddedShared = Path.Combine(exeDir, "shared");
            Directory.CreateDirectory(embeddedShared);
            File.WriteAllText(Path.Combine(embeddedShared, "correction-prompt.md"), "embedded (stale)");

            var repoShared = Path.Combine(_root, "shared");
            Directory.CreateDirectory(repoShared);
            File.WriteAllText(Path.Combine(repoShared, "correction-prompt.md"), "repo (live)");

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Equal(repoShared, resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_IgnoresDirectoryMissingPromptFile()
        {
            var exeDir = Path.Combine(_root, "publish2");
            var sharedDir = Path.Combine(exeDir, "shared");
            Directory.CreateDirectory(sharedDir);
            // no correction-prompt.md written — should not count as valid

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Null(resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_EmptySharedFolderPartwayUp_KeepsWalkingToRealOne()
        {
            // An intermediate ancestor happens to have its own (unrelated, empty) "shared" folder
            // with no correction-prompt.md — e.g. some other project's build output. IsValidSharedDir
            // must reject it and keep walking up rather than stopping/returning null early.
            var exeDir = Path.Combine(_root, "a", "b", "c", "bin", "Debug", "net8.0-windows");
            Directory.CreateDirectory(exeDir);

            var decoyShared = Path.Combine(_root, "a", "b", "shared");
            Directory.CreateDirectory(decoyShared); // no correction-prompt.md inside

            var realShared = Path.Combine(_root, "shared");
            Directory.CreateDirectory(realShared);
            File.WriteAllText(Path.Combine(realShared, "correction-prompt.md"), "template");

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Equal(realShared, resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_ExeDirectoryItselfNamedShared_DoesNotMatchItself()
        {
            // The exe directory itself happens to be named "shared" (unlikely but not impossible
            // for a standalone install path). Step 1 starts walking from exeDirectory.Parent, so it
            // must not treat exeDirectory's own name/contents as a match; only an ancestor's child
            // folder literally named "shared" (or the embedded fallback) counts.
            var exeDir = Path.Combine(_root, "tools", "shared");
            Directory.CreateDirectory(exeDir);
            // exeDir has no correction-prompt.md of its own directly usable as "shared/shared"; and
            // there is no valid shared/ elsewhere in the tree.

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Null(resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_WalksUpManyLevelsDeep()
        {
            // Confirms there's no artificial depth cap on the walk-up loop.
            var exeDir = Path.Combine(_root, "1", "2", "3", "4", "5", "6", "7", "8", "bin", "Release", "net8.0-windows", "publish");
            Directory.CreateDirectory(exeDir);

            var sharedDir = Path.Combine(_root, "shared");
            Directory.CreateDirectory(sharedDir);
            File.WriteAllText(Path.Combine(sharedDir, "correction-prompt.md"), "template");

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Equal(sharedDir, resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_EmbeddedSharedFolderMissingPromptFile_FallsBackToNull()
        {
            // The embedded fallback (step 2) must apply the same IsValidSharedDir check as the
            // walk-up — an empty "shared" folder next to the exe with no ancestor match at all
            // must resolve to null, not to the empty embedded folder.
            var exeDir = Path.Combine(_root, "standalone");
            var embeddedShared = Path.Combine(exeDir, "shared");
            Directory.CreateDirectory(embeddedShared);
            // no correction-prompt.md inside embeddedShared

            var resolved = SharedPaths.ResolveSharedDirectory(exeDir);

            Assert.Null(resolved);
        }

        [Fact]
        public void ResolveSharedDirectory_EmptyExeDirectory_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => SharedPaths.ResolveSharedDirectory(""));
        }
    }
}
