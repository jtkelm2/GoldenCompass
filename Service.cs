using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Central service that owns data stores (AttemptTracker, TimingData),
    /// the SemiomniscientAdvisor, and the fitted room models.
    /// 
    /// Keeps the module thin: the module handles Everest events and delegates
    /// all data/model logic here.
    ///
    /// Model fitting and advisor updates run on a background thread at
    /// BelowNormal priority to avoid frame hitches. A generation counter
    /// ensures only the latest refit is applied; intermediate results from
    /// stale dispatches are discarded.
    /// </summary>
    public class Service {
        public AttemptTracker Tracker { get; private set; }
        public TimingData Timings { get; private set; }

        /// <summary>
        /// The current advisor. Volatile so the main thread always sees
        /// the latest reference after a background swap.
        /// </summary>
        private volatile SemiomniscientAdvisor _advisor;
        public SemiomniscientAdvisor Advisor => _advisor;

        /// <summary>
        /// True while a background refit is in progress. Checked by the
        /// renderer to display a visual indicator.
        /// </summary>
        private volatile bool _isRefitting;
        public bool IsRefitting => _isRefitting;

        public bool HasTimingsForCurrentChapter { get; private set; }

        private string _currentSID;

        /// <summary>
        /// Monotonically increasing generation counter. Incremented each time
        /// a refit is dispatched; the background thread checks on completion
        /// whether its generation is still current before swapping in results.
        /// </summary>
        private int _generation;

        public Service() {
            Tracker = new AttemptTracker();
            Timings = new TimingData();
            _advisor = new SemiomniscientAdvisor();
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
                DispatchRebuildAllModels();
            else
                ClearModels();

            Logger.Log(LogLevel.Info, "GoldenCompass",
                $"Chapter changed to {sid}. Timings available: {HasTimingsForCurrentChapter}");
        }

        /// <summary>
        /// Record an attempt and dispatch a background refit for the chapter.
        /// The tracker write is synchronous (cheap); model fitting runs on a
        /// background thread.
        /// </summary>
        public void RecordAttempt(string sid, string room, bool success) {
            Tracker.Record(sid, room, success);

            if (!HasTimingsForCurrentChapter) return;

            var chapterTimings = Timings.GetChapterTimings(sid);
            if (chapterTimings == null) return;

            DispatchRebuildAllModels();
        }

        /// <summary>
        /// Dispatch a full model rebuild on a background thread.
        /// Bumps the generation counter so any in-flight older rebuild
        /// will discard its results.
        /// </summary>
        private void DispatchRebuildAllModels() {
            if (_currentSID == null) return;

            var chapterTimings = Timings.GetChapterTimings(_currentSID);
            if (chapterTimings == null) {
                ClearModels();
                return;
            }

            // Snapshot all inputs for the background thread
            var roomOrder = new List<string>(chapterTimings.RoomOrder);
            var chapterData = Tracker.GetChapterData(_currentSID);

            // Build a snapshot of attempt data (deep copy the lists)
            var attemptSnapshot = new Dictionary<string, List<bool>>();
            foreach (string room in roomOrder) {
                List<bool> attempts = chapterData != null
                    ? (Tracker.GetRoomData(_currentSID, room) ?? new List<bool>())
                    : new List<bool>();
                attemptSnapshot[room] = new List<bool>(attempts);
            }

            var timingsSnapshot = new Dictionary<string, double>(chapterTimings.Timings);

            int gen = Interlocked.Increment(ref _generation);
            _isRefitting = true;

            var thread = new Thread(() => {
                try {
                    BackgroundRebuild(gen, roomOrder, timingsSnapshot, attemptSnapshot);
                } catch (Exception e) {
                    Logger.Log(LogLevel.Warn, "GoldenCompass",
                        $"Background refit failed: {e.Message}");
                }
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.Name = "GoldenCompass-Refit";
            thread.Start();
        }

        /// <summary>
        /// Runs on the background thread. Fits all room models, builds a new
        /// advisor, pre-warms its recommendation, then atomically swaps it in
        /// if the generation is still current.
        /// </summary>
        private void BackgroundRebuild(
            int generation,
            List<string> roomOrder,
            Dictionary<string, double> timings,
            Dictionary<string, List<bool>> attemptSnapshot)
        {
            // Fit models
            var models = new Dictionary<string, RoomModel>();
            foreach (string room in roomOrder) {
                // Check if we've been superseded before each fit
                if (generation != Volatile.Read(ref _generation)) return;

                double time = timings[room];
                List<bool> attempts = attemptSnapshot.ContainsKey(room)
                    ? attemptSnapshot[room]
                    : new List<bool>();
                models[room] = ModelFitter.Fit(attempts, time);
            }

            // Build a fresh advisor and pre-warm the recommendation
            var newAdvisor = new SemiomniscientAdvisor();
            newAdvisor.UpdateModels(roomOrder, models);
            newAdvisor.GetRecommendation(); // pre-warm cache

            // Only swap in if we're still the latest generation
            if (generation == Volatile.Read(ref _generation)) {
                _advisor = newAdvisor;
                _isRefitting = false;
            }
        }

        /// <summary>
        /// Clear data for the current chapter and reset models.
        /// </summary>
        public void ClearCurrentChapter() {
            if (_currentSID == null) return;
            Tracker.ClearChapter(_currentSID);
            DispatchRebuildAllModels();
        }

        /// <summary>
        /// Clear all data and reset advisor.
        /// </summary>
        public void ClearAllData() {
            Tracker.ClearAll();
            ClearModels();
        }

        /// <summary>
        /// Compute total time expended on the current chapter:
        /// each success counts as full room time, each failure as half.
        /// Returns null if no timing data is available.
        /// </summary>
        public double? GetTimeExpended() {
            if (_currentSID == null || !HasTimingsForCurrentChapter) return null;

            var chapterTimings = Timings.GetChapterTimings(_currentSID);
            if (chapterTimings == null) return null;

            var chapterData = Tracker.GetChapterData(_currentSID);
            if (chapterData == null) return 0.0;

            double total = 0.0;
            foreach (var kvp in chapterData) {
                string room = kvp.Key;
                if (!chapterTimings.Timings.ContainsKey(room)) continue;
                double time = chapterTimings.Timings[room];
                foreach (bool success in kvp.Value) {
                    total += success ? time : time / 2.0;
                }
            }
            return total;
        }

        private void ClearModels() {
            // Bump generation to cancel any in-flight refit
            Interlocked.Increment(ref _generation);
            _isRefitting = false;

            var emptyAdvisor = new SemiomniscientAdvisor();
            emptyAdvisor.UpdateModels(new List<string>(), new Dictionary<string, RoomModel>());
            _advisor = emptyAdvisor;
        }
    }
}