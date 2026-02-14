using System.IO;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Shared path utilities for data file resolution.
    /// All mod data is stored under Everest's mod save directory:
    ///   {Everest.PathSettings}/Saves/modsavedata/GoldenCompass/
    /// </summary>
    public static class PathUtils {
        private static string _baseDir;

        /// <summary>
        /// Base directory for all GoldenCompass data.
        /// Resolved lazily so Everest has time to initialize.
        /// </summary>
        public static string BaseDir {
            get {
                if (_baseDir == null) {
                    _baseDir = Path.Combine(
                        Everest.PathSettings,
                        "GoldenCompass"
                    );
                }
                return _baseDir;
            }
        }

        public static string AttemptsDir => Path.Combine(BaseDir, "attempts");
        public static string TimingsDir => Path.Combine(BaseDir, "timings");

        /// <summary>
        /// Sanitize a SID for use as a filename.
        /// Replaces invalid filename characters and forward slashes with underscores.
        /// </summary>
        public static string SanitizeFileName(string sid) {
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = sid;
            foreach (char c in invalid)
                result = result.Replace(c, '_');
            result = result.Replace('/', '_');
            return result;
        }
    }
}