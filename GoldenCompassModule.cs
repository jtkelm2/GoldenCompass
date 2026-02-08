using System;
using System.Collections.Generic;
using System.Linq;
using Monocle;

namespace Celeste.Mod.GoldenCompass {
    public class GoldenCompassModule : EverestModule {
        public static GoldenCompassModule Instance;

        public override Type SettingsType => typeof(GoldenCompassSettings);
        public GoldenCompassSettings ModSettings => (GoldenCompassSettings)_Settings;

        public AttemptTracker Tracker { get; private set; }
        public TimingData Timings { get; private set; }
        public SemiomniscientAdvisor Advisor { get; private set; }

        public bool HasTimingsForCurrentChapter { get; private set; }

        private Dictionary<string, int> _attemptsSinceRefit = new Dictionary<string, int>();
        private string _currentSID;
        private GoldenCompassRenderer _renderer;

        public GoldenCompassModule() {
            Instance = this;
        }

        public override void Load() {
            Tracker = new AttemptTracker();
            Timings = new TimingData();
            Advisor = new SemiomniscientAdvisor();

            Everest.Events.Player.OnDie += OnPlayerDie;
            Everest.Events.Level.OnComplete += OnRoomComplete;
            Everest.Events.Level.OnEnter += OnLevelEnter;
            Everest.Events.Level.OnExit += OnLevelExit;
        }

        public override void Unload() {
            Everest.Events.Player.OnDie -= OnPlayerDie;
            Everest.Events.Level.OnComplete -= OnRoomComplete;
            Everest.Events.Level.OnEnter -= OnLevelEnter;
            Everest.Events.Level.OnExit -= OnLevelExit;
        }

        private void OnPlayerDie(Player player) {
            if (!ModSettings.TrackingEnabled) return;
            if (!(Engine.Scene is Level level)) return;

            string sid = GetSID(level.Session);
            string room = level.Session.Level;
            RecordAttempt(sid, room, success: false);
        }

        private void OnRoomComplete(Level level) {
            if (!ModSettings.TrackingEnabled) return;

            string sid = GetSID(level.Session);
            string room = level.Session.Level;
            RecordAttempt(sid, room, success: true);
        }

        private void OnLevelEnter(Session session, bool fromSaveData) {
            string sid = GetSID(session);
            if (_currentSID != sid)
                OnChapterChanged(sid);
        }

        private void OnLevelExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow) {
            _renderer = null;
        }

        private void OnChapterChanged(string sid) {
            _currentSID = sid;
            ModSettings.CurrentSID = sid;
            _attemptsSinceRefit.Clear();
            HasTimingsForCurrentChapter = Timings.HasTimings(sid);

            if (HasTimingsForCurrentChapter)
                RefitAllModels();

            Logger.Log(LogLevel.Info, "GoldenCompass",
                $"Chapter changed to {sid}. Timings available: {HasTimingsForCurrentChapter}");
        }

        private void RecordAttempt(string sid, string room, bool success) {
            Tracker.Record(sid, room, success);

            if (!_attemptsSinceRefit.ContainsKey(room))
                _attemptsSinceRefit[room] = 0;
            _attemptsSinceRefit[room]++;

            if (_attemptsSinceRefit[room] >= ModSettings.RefitInterval) {
                _attemptsSinceRefit[room] = 0;
                RefitAllModels();
            }

            // Ensure renderer is present whenever we record data
            if (Engine.Scene is Level level)
                EnsureRenderer(level);
        }

        private void RefitAllModels() {
            if (_currentSID == null) return;

            var chapterData = Tracker.GetChapterData(_currentSID);
            var chapterTimings = Timings.GetChapterTimings(_currentSID);
            if (chapterTimings == null) return;

            var roomOrder = chapterTimings.Keys.ToList();
            roomOrder.Sort(StringComparer.Ordinal);

            var models = new Dictionary<string, RoomModel>();
            foreach (string room in roomOrder) {
                double time = chapterTimings[room];
                List<bool> attempts = chapterData != null
                    ? (Tracker.GetRoomData(_currentSID, room) ?? new List<bool>())
                    : new List<bool>();
                models[room] = ModelFitter.Fit(attempts, time, ModSettings.MinAttemptsForFit);
            }

            Advisor.UpdateModels(roomOrder, models);
        }

        private void EnsureRenderer(Level level) {
            if (_renderer != null && _renderer.Scene == level) return;
            _renderer = new GoldenCompassRenderer();
            level.Add(_renderer);
        }

        private static string GetSID(Session session) {
            string sid = session.Area.SID ?? session.Area.ToString();
            var mode = session.Area.Mode;
            string suffix = mode == AreaMode.BSide ? "_B"
                          : mode == AreaMode.CSide ? "_C"
                          : "";
            return sid + suffix;
        }

        public override void CreateModMenuSection(TextMenu menu, bool inGame, FMOD.Studio.EventInstance snapshot) {
            CreateModMenuSectionHeader(menu, inGame, snapshot);

            if (inGame && _currentSID != null) {
                var clearChapterItem = new TextMenu.Button("Clear Data: Current Chapter");
                clearChapterItem.Pressed(() => {
                    Tracker.ClearChapter(_currentSID);
                    _attemptsSinceRefit.Clear();
                    RefitAllModels();
                    Logger.Log(LogLevel.Info, "GoldenCompass", $"Cleared data for {_currentSID}");
                });
                menu.Add(clearChapterItem);
            }

            var clearAllItem = new TextMenu.Button("Clear All Data");
            clearAllItem.Pressed(() => {
                Tracker.ClearAll();
                _attemptsSinceRefit.Clear();
                Advisor.UpdateModels(new List<string>(), new Dictionary<string, RoomModel>());
                Logger.Log(LogLevel.Info, "GoldenCompass", "Cleared all data");
            });
            menu.Add(clearAllItem);

            if (_currentSID != null && !HasTimingsForCurrentChapter) {
                string path = TimingData.GetTimingFilePath(_currentSID);
                var infoItem = new TextMenu.SubHeader($"Timing file needed: {path}");
                menu.Add(infoItem);
            }
        }
    }
}