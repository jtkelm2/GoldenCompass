using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Manages per-chapter, per-room attempt history.
    /// Each chapter's data is stored in a separate JSON file at:
    ///   {PathUtils.AttemptsDir}/{sanitized_sid}.json
    /// Format: { "room_name": [true, false, ...], ... }
    /// </summary>
    public class AttemptTracker {
        // SID -> (room name -> list of success/fail outcomes)
        private Dictionary<string, Dictionary<string, List<bool>>> _cache
            = new Dictionary<string, Dictionary<string, List<bool>>>();

        /// <summary>
        /// Record an attempt outcome for a room.
        /// </summary>
        public void Record(string sid, string room, bool success) {
            var chapterData = EnsureChapter(sid);

            if (!chapterData.ContainsKey(room))
                chapterData[room] = new List<bool>();

            chapterData[room].Add(success);
            Save(sid);
        }

        /// <summary>
        /// Get all attempt data for a chapter, or null if no data exists.
        /// </summary>
        public Dictionary<string, List<bool>> GetChapterData(string sid) {
            if (_cache.ContainsKey(sid))
                return _cache[sid];

            var data = LoadFromDisk(sid);
            if (data != null)
                _cache[sid] = data;
            return data;
        }

        /// <summary>
        /// Get attempt list for a specific room, or null if no data.
        /// </summary>
        public List<bool> GetRoomData(string sid, string room) {
            var chapterData = GetChapterData(sid);
            if (chapterData != null && chapterData.ContainsKey(room))
                return chapterData[room];
            return null;
        }

        /// <summary>
        /// Clear all data for a chapter.
        /// </summary>
        public void ClearChapter(string sid) {
            if (_cache.ContainsKey(sid))
                _cache.Remove(sid);

            try {
                string path = GetAttemptFilePath(sid);
                if (File.Exists(path))
                    File.Delete(path);
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass", $"Failed to delete attempt file for {sid}: {e.Message}");
            }
        }

        /// <summary>
        /// Clear all data.
        /// </summary>
        public void ClearAll() {
            _cache.Clear();

            try {
                string dir = PathUtils.AttemptsDir;
                if (Directory.Exists(dir)) {
                    foreach (string file in Directory.GetFiles(dir, "*.json"))
                        File.Delete(file);
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass", $"Failed to clear all attempt data: {e.Message}");
            }
        }

        /// <summary>
        /// Get set of all chapter SIDs that have data.
        /// </summary>
        public IEnumerable<string> GetTrackedChapters() {
            return _cache.Keys;
        }

        /// <summary>
        /// Ensure a chapter file exists on disk (creates an empty one if not).
        /// Called when first entering a chapter.
        /// </summary>
        public void EnsureChapterFile(string sid) {
            EnsureChapter(sid);
            Save(sid);
        }

        /// <summary>
        /// Get the expected file path for a chapter's attempt file.
        /// </summary>
        public static string GetAttemptFilePath(string sid) {
            string sanitized = PathUtils.SanitizeFileName(sid);
            return Path.Combine(PathUtils.AttemptsDir, sanitized + ".json");
        }

        private Dictionary<string, List<bool>> EnsureChapter(string sid) {
            var data = GetChapterData(sid);
            if (data == null) {
                data = new Dictionary<string, List<bool>>();
                _cache[sid] = data;
            }
            return data;
        }

        private Dictionary<string, List<bool>> LoadFromDisk(string sid) {
            string path = GetAttemptFilePath(sid);
            try {
                if (File.Exists(path)) {
                    string json = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<Dictionary<string, List<bool>>>(json);
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass", $"Failed to load attempt data for {sid}: {e.Message}");
            }
            return null;
        }

        private void Save(string sid) {
            if (!_cache.ContainsKey(sid)) {
              Logger.Log(LogLevel.Warn, "GoldenCompass", $"_cache does not contain key {sid}");
              return;
            }

            try {
                string dir = PathUtils.AttemptsDir;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string path = GetAttemptFilePath(sid);
                File.WriteAllText(path, JsonConvert.SerializeObject(_cache[sid], Formatting.Indented));
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass", $"Failed to save attempt data for {sid}: {e.Message}");
            }
        }
    }
}