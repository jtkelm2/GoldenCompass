using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Per-chapter timing data loaded from JSON files.
    /// 
    /// Stores both a room-to-time dictionary for lookups and a room order list
    /// that preserves the key order from the JSON file, since Dictionary does
    /// not guarantee insertion order on .NET 4.5.2.
    /// 
    /// Timing files are stored per chapter SID in:
    ///   {PathUtils.TimingsDir}/{sanitized_sid}.json
    ///
    /// Format: { "room_name": seconds, ... }
    /// </summary>
    public class TimingData {
        /// <summary>
        /// Holds both the timing dictionary and the ordered room list for a chapter.
        /// </summary>
        public class ChapterTimings {
            public List<string> RoomOrder { get; set; }
            public Dictionary<string, double> Timings { get; set; }
        }

        // SID -> chapter timings (order + lookup)
        private Dictionary<string, ChapterTimings> _cache
            = new Dictionary<string, ChapterTimings>();

        /// <summary>
        /// Get timings for a chapter. Returns null if no timing file exists.
        /// </summary>
        public ChapterTimings GetChapterTimings(string sid) {
            if (_cache.ContainsKey(sid))
                return _cache[sid];

            var timings = LoadFromDisk(sid);
            if (timings != null)
                _cache[sid] = timings;
            return timings;
        }

        /// <summary>
        /// Get timing for a specific room.
        /// </summary>
        public double? GetRoomTime(string sid, string room) {
            var chapter = GetChapterTimings(sid);
            if (chapter != null && chapter.Timings.ContainsKey(room))
                return chapter.Timings[room];
            return null;
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
            string sanitized = PathUtils.SanitizeFileName(sid);
            return Path.Combine(PathUtils.TimingsDir, sanitized + ".json");
        }

        private ChapterTimings LoadFromDisk(string sid) {
            string path = GetTimingFilePath(sid);
            Logger.Log(LogLevel.Warn, "GoldenCompass",
                    $"Attempting Timing LoadFromDisk at {sid}");            
            try {
                if (File.Exists(path)) {
                    string json = File.ReadAllText(path);
                    var jObj = JObject.Parse(json);

                    var roomOrder = new List<string>();
                    var timings = new Dictionary<string, double>();

                    foreach (var prop in jObj.Properties()) {
                        string room = prop.Name;
                        double time = prop.Value.Value<double>();
                        roomOrder.Add(room);
                        timings[room] = time;
                    }

                    return new ChapterTimings {
                        RoomOrder = roomOrder,
                        Timings = timings
                    };
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass",
                    $"Failed to load timings for {sid}: {e.Message}");
            }
            return null;
        }
    }
}