using System;
using System.IO;

namespace WhisperWin.Core
{
    /// <summary>
    /// Locates the `shared/` directory (correction-prompt.md + dictionary.json) that is shared
    /// between the Mac and Windows apps. Walks up from the executable location first, looking for
    /// a repo-style `shared/` folder — this is checked before the embedded copy so a dev build (or
    /// even a published one still living inside the OneDrive-synced repo checkout) finds the same
    /// file the Mac app reads/writes, instead of silently reading its own bundled snapshot. Falls
    /// back to a copy embedded next to the exe (the build copies shared/ into the publish output)
    /// only when no repo-style folder is found — e.g. a standalone install outside the repo.
    /// </summary>
    public static class SharedPaths
    {
        private const string SharedDirName = "shared";
        private const string PromptFileName = "correction-prompt.md";
        private const string DictionaryFileName = "dictionary.json";

        /// <summary>
        /// Resolves the `shared/` directory. Returns null if it cannot be found anywhere.
        /// </summary>
        public static string? ResolveSharedDirectory(string exeDirectory)
        {
            if (string.IsNullOrEmpty(exeDirectory))
            {
                throw new ArgumentException("Executable directory must be provided.", nameof(exeDirectory));
            }

            // 1. Walk up from the exe's PARENT looking for a repo-style shared/ folder (dev loop,
            //    and any published build that still lives inside the source checkout). Starts one
            //    level above exeDirectory so it can't match the embedded copy checked in step 2 —
            //    checked first so the app sees the same file the Mac app syncs via OneDrive,
            //    instead of silently reading its own bundled snapshot.
            var dir = new DirectoryInfo(exeDirectory).Parent;
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, SharedDirName);
                if (IsValidSharedDir(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }

            // 2. Embedded copy right next to the exe (build output) — fallback for a standalone
            //    install outside the repo, where no ancestor directory has a shared/ folder.
            var embedded = Path.Combine(exeDirectory, SharedDirName);
            if (IsValidSharedDir(embedded))
            {
                return embedded;
            }

            return null;
        }

        private static bool IsValidSharedDir(string path)
        {
            return Directory.Exists(path) && File.Exists(Path.Combine(path, PromptFileName));
        }

        public static string PromptFilePath(string sharedDirectory) => Path.Combine(sharedDirectory, PromptFileName);

        public static string DictionaryFilePath(string sharedDirectory) => Path.Combine(sharedDirectory, DictionaryFileName);
    }
}
