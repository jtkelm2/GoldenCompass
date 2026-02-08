using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Manages per-chapter, per-room attempt history.
    /// Persists to disk as JSON on every recorded attempt.
    /// </summary>
    public class AttemptTracker {
        private static readonly string DataDir = Path.Combine("Mods", "GoldenCompassData");
        private static readonly string DataFilePath = Path.Combine(DataDir, "attempt-history.json");

        // SID -> (room name -> list of success/fail outcomes)
        private Dictionary<string, Dictionary<string, List<bool>>> _data;

        public AttemptTracker() {
            Load();
        }

        /// <summary>
        /// Record an attempt outcome for a room.
        /// </summary>
        public void Record(string sid, string room, bool success) {
            EnsureChapter(sid);

            if (!_data[sid].ContainsKey(room))
                _data[sid][room] = new List<bool>();

            _data[sid][room].Add(success);
            Save();
        }

        /// <summary>
        /// Get all attempt data for a chapter, or null if no data exists.
        /// </summary>
        public Dictionary<string, List<bool>> GetChapterData(string sid) {
            if (_data.ContainsKey(sid))
                return _data[sid];
            return null;
        }

        /// <summary>
        /// Get attempt list for a specific room, or null if no data.
        /// </summary>
        public List<bool> GetRoomData(string sid, string room) {
            if (_data.ContainsKey(sid) && _data[sid].ContainsKey(room))
                return _data[sid][room];
            return null;
        }

        /// <summary>
        /// Clear all data for a chapter.
        /// </summary>
        public void ClearChapter(string sid) {
            if (_data.ContainsKey(sid)) {
                _data.Remove(sid);
                Save();
            }
        }

        /// <summary>
        /// Clear all data.
        /// </summary>
        public void ClearAll() {
            _data = new Dictionary<string, Dictionary<string, List<bool>>>();
            Save();
        }

        /// <summary>
        /// Get set of all chapter SIDs that have data.
        /// </summary>
        public IEnumerable<string> GetTrackedChapters() {
            return _data.Keys;
        }

        private void EnsureChapter(string sid) {
            if (!_data.ContainsKey(sid))
                _data[sid] = new Dictionary<string, List<bool>>();
        }

        private void Load() {
            try {
                if (File.Exists(DataFilePath)) {
                    string json = File.ReadAllText(DataFilePath);
                    _data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<bool>>>>(json);
                    if (_data != null) return;
                }
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass", $"Failed to load attempt data: {e.Message}");
            }
            _data = new Dictionary<string, Dictionary<string, List<bool>>>();
        }

        private void Save() {
            try {
                if (!Directory.Exists(DataDir))
                    Directory.CreateDirectory(DataDir);
                File.WriteAllText(DataFilePath, JsonConvert.SerializeObject(_data, Formatting.Indented));
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass", $"Failed to save attempt data: {e.Message}");
            }
        }
    }
}
