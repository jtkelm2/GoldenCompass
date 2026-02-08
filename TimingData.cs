using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Loads per-room completion times from JSON files on disk.
    /// 
    /// Timing files are stored per chapter SID in:
    ///   Mods/GoldenCompassData/timings/{sanitized_sid}.json
    ///
    /// Format: { "room_name": seconds, ... }
    /// </summary>
    public class TimingData {
        private static readonly string TimingsDir = Path.Combine("Mods", "GoldenCompassData", "timings");

        // SID -> (room name -> seconds)
        private Dictionary<string, Dictionary<string, double>> _cache
            = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Get timings for a chapter. Returns null if no timing file exists.
        /// </summary>
        public Dictionary<string, double> GetChapterTimings(string sid) {
            if (_cache.ContainsKey(sid))
                return _cache[sid];

            var timings = LoadFromDisk(sid);
            if (timings != null)
                _cache[sid] = timings;
            return timings;
        }

        /// <summary>
        /// Get timing for a specific room. Returns fallback if unavailable.
        /// </summary>
        public double GetRoomTime(string sid, string room, double fallback = 10.0) {
            var timings = GetChapterTimings(sid);
            if (timings != null && timings.ContainsKey(room))
                return timings[room];
            return fallback;
        }

        /// <summary>
        /// Check whether a timing file exists for the given SID.
        /// </summary>
        public bool HasTimings(string sid) {
            return GetChapterTimings(sid) != null;
        }

        /// <summary>
        /// Invalidate cached timings for a chapter (e.g. after file edit).
        /// </summary>
        public void InvalidateCache(string sid) {
            if (_cache.ContainsKey(sid))
                _cache.Remove(sid);
        }

        /// <summary>
        /// Get the expected file path for a chapter's timing file.
        /// Useful for telling the user where to place the file.
        /// </summary>
        public static string GetTimingFilePath(string sid) {
            string sanitized = SanitizeFileName(sid);
            return Path.Combine(TimingsDir, sanitized + ".json");
        }

        private Dictionary<string, double> LoadFromDisk(string sid) {
            string path = GetTimingFilePath(sid);
            try {
                if (File.Exists(path)) {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<Dictionary<string, double>>(json);
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass",
                    $"Failed to load timings for {sid}: {e.Message}");
            }
            return null;
        }

        /// <summary>
        /// Sanitize a SID for use as a filename.
        /// Replaces path separators and other problematic characters.
        /// </summary>
        private static string SanitizeFileName(string sid) {
            char[] invalid = Path.GetInvalidFileNameChars();
            string result = sid;
            foreach (char c in invalid)
                result = result.Replace(c, '_');
            // Also replace forward slash which SIDs commonly contain
            result = result.Replace('/', '_');
            return result;
        }
    }
}
