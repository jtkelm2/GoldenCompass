using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Central service that owns data stores (AttemptTracker, TimingData),
    /// the SemiomniscientAdvisor, and the fitted room models.
    /// 
    /// Keeps the module thin: the module handles Everest events and delegates
    /// all data/model logic here.
    /// </summary>
    public class Service {
        public AttemptTracker Tracker { get; private set; }
        public TimingData Timings { get; private set; }
        public SemiomniscientAdvisor Advisor { get; private set; }

        public bool HasTimingsForCurrentChapter { get; private set; }

        private string _currentSID;
        private List<string> _roomOrder;
        private Dictionary<string, RoomModel> _models;

        public Service() {
            Tracker = new AttemptTracker();
            Timings = new TimingData();
            Advisor = new SemiomniscientAdvisor();
            _roomOrder = new List<string>();
            _models = new Dictionary<string, RoomModel>();
        }

        /// <summary>
        /// Called when the player enters a new chapter (or the SID changes).
        /// Loads timing data, initializes attempt file, and fits all models.
        /// </summary>
        public void OnChapterChanged(string sid) {
            _currentSID = sid;
            Logger.Log(LogLevel.Warn, "GoldenCompass",
                    $"Service OnChapterChanged at {sid}");
            HasTimingsForCurrentChapter = Timings.HasTimings(sid);

            Tracker.EnsureChapterFile(sid);

            if (HasTimingsForCurrentChapter)
                RebuildAllModels();
            else
                ClearModels();

            Logger.Log(LogLevel.Info, "GoldenCompass",
                $"Chapter changed to {sid}. Timings available: {HasTimingsForCurrentChapter}");
        }

        /// <summary>
        /// Record an attempt and refit only the affected room's model,
        /// then update the advisor.
        /// </summary>
        public void RecordAttempt(string sid, string room, bool success) {
            Tracker.Record(sid, room, success);

            if (!HasTimingsForCurrentChapter) return;

            var chapterTimings = Timings.GetChapterTimings(sid);
            if (chapterTimings == null || !chapterTimings.Timings.ContainsKey(room)) return;

            double time = chapterTimings.Timings[room];
            List<bool> attempts = Tracker.GetRoomData(sid, room) ?? new List<bool>();
            _models[room] = ModelFitter.Fit(attempts, time);

            Advisor.UpdateModels(_roomOrder, _models);
        }

        /// <summary>
        /// Rebuild all models from scratch. Used on chapter entry and after clearing data.
        /// </summary>
        public void RebuildAllModels() {
            if (_currentSID == null) return;

            var chapterTimings = Timings.GetChapterTimings(_currentSID);
            if (chapterTimings == null) {
                ClearModels();
                return;
            }

            _roomOrder = new List<string>(chapterTimings.RoomOrder);
            _models = new Dictionary<string, RoomModel>();
            var chapterData = Tracker.GetChapterData(_currentSID);

            foreach (string room in _roomOrder) {
                double time = chapterTimings.Timings[room];
                List<bool> attempts = chapterData != null
                    ? (Tracker.GetRoomData(_currentSID, room) ?? new List<bool>())
                    : new List<bool>();
                _models[room] = ModelFitter.Fit(attempts, time);
            }

            Advisor.UpdateModels(_roomOrder, _models);
        }

        /// <summary>
        /// Clear data for the current chapter and reset models.
        /// </summary>
        public void ClearCurrentChapter() {
            if (_currentSID == null) return;
            Tracker.ClearChapter(_currentSID);
            RebuildAllModels();
        }

        /// <summary>
        /// Clear all data and reset advisor.
        /// </summary>
        public void ClearAllData() {
            Tracker.ClearAll();
            ClearModels();
        }

        private void ClearModels() {
            _roomOrder = new List<string>();
            _models = new Dictionary<string, RoomModel>();
            Advisor.UpdateModels(_roomOrder, _models);
        }
    }
}