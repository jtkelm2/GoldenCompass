using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.GoldenCompass {
    public class GoldenCompassModule : EverestModule {
        public static GoldenCompassModule Instance { get; private set; }

        public override Type SettingsType => typeof(Settings);
        public Settings ModSettings => (Settings)_Settings;

        public Service Service { get; private set; }

        public string CurrentSID { get; private set; }
        public string CurrentRoomName { get; private set; }
        private string _previousRoomName;
        private Renderer _renderer;

        public GoldenCompassModule() {
            Instance = this;
        }

        public override void Load() {
            Service = new Service();

            Everest.Events.Player.OnDie += OnPlayerDie;
            Everest.Events.Level.OnTransitionTo += OnRoomTransition;
            Everest.Events.Level.OnComplete += OnLevelComplete;
            Everest.Events.Level.OnEnter += OnLevelEnter;
            Everest.Events.Level.OnLoadLevel += OnLoadLevel;
            Everest.Events.Level.OnExit += OnLevelExit;
        }

        public override void Unload() {
            Everest.Events.Player.OnDie -= OnPlayerDie;
            Everest.Events.Level.OnTransitionTo -= OnRoomTransition;
            Everest.Events.Level.OnComplete -= OnLevelComplete;
            Everest.Events.Level.OnEnter -= OnLevelEnter;
            Everest.Events.Level.OnLoadLevel -= OnLoadLevel;
            Everest.Events.Level.OnExit -= OnLevelExit;
        }

        private void OnPlayerDie(Player player) {
            if (!ModSettings.TrackingEnabled) return;
            if (!(Engine.Scene is Level level)) return;

            string sid = GetSID(level.Session);
            string room = level.Session.Level;
            Service.RecordAttempt(sid, room, success: false);
        }

        private void OnRoomTransition(Level level, LevelData next, Vector2 direction) {
            if (!ModSettings.TrackingEnabled) return;
            if (next == null) return;

            string sid = GetSID(level.Session);
            string newRoomName = next.Name;

            // Only record success if we are transitioning from one room into another non-current, non-previous room
            if (CurrentRoomName != null && newRoomName != CurrentRoomName && newRoomName != _previousRoomName) {
                Service.RecordAttempt(sid, CurrentRoomName, success: true);
            }

            _previousRoomName = CurrentRoomName;
            CurrentRoomName = newRoomName;
        }

        private void OnLevelComplete(Level level) {
            if (!ModSettings.TrackingEnabled) return;

            string sid = GetSID(level.Session);
            Service.RecordAttempt(sid, CurrentRoomName, success: true);
        }

        private void OnLevelEnter(Session session, bool fromSaveData) {
            CurrentSID = GetSID(session);

            Service.OnChapterChanged(CurrentSID);

            CurrentRoomName = session.Level;
            _previousRoomName = null;
        }

        private void OnLoadLevel(Level level, Player.IntroTypes playerIntro, bool isFromLoader) {
            EnsureRenderer(level);

            string actualRoom = level.Session.Level;
            if (actualRoom != CurrentRoomName) {
                _previousRoomName = null;
                CurrentRoomName = actualRoom;
            }
        }

        private void OnLevelExit(Level level, LevelExit exit, LevelExit.Mode mode, Session session, HiresSnow snow) {
            _renderer = null;
        }

        private void EnsureRenderer(Level level) {
            if (_renderer != null && _renderer.Scene == level) return;
            _renderer = new Renderer();
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
            base.CreateModMenuSection(menu, inGame, snapshot);

            if (inGame && CurrentSID != null) {
                var clearChapterItem = new TextMenu.Button("Clear Data: Current Chapter");
                clearChapterItem.Pressed(() => {
                    Service.ClearCurrentChapter();
                    Logger.Log(LogLevel.Info, "GoldenCompass", $"Cleared data for {CurrentSID}");
                });
                menu.Add(clearChapterItem);
            }

            var clearAllItem = new TextMenu.Button("Clear All Data");
            clearAllItem.Pressed(() => {
                Service.ClearAllData();
                Logger.Log(LogLevel.Info, "GoldenCompass", "Cleared all data");
            });
            menu.Add(clearAllItem);

            if (CurrentSID != null && !Service.HasTimingsForCurrentChapter) {
                string path = TimingData.GetTimingFilePath(CurrentSID);
                var infoItem = new TextMenu.SubHeader($"Timing file needed: {path}");
                menu.Add(infoItem);
            }
        }
    }
}