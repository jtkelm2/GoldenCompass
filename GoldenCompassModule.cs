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
            On.Celeste.Level.Update += OnLevelUpdate;
        }

        public override void Unload() {
            Everest.Events.Player.OnDie -= OnPlayerDie;
            Everest.Events.Level.OnTransitionTo -= OnRoomTransition;
            Everest.Events.Level.OnComplete -= OnLevelComplete;
            Everest.Events.Level.OnEnter -= OnLevelEnter;
            Everest.Events.Level.OnLoadLevel -= OnLoadLevel;
            Everest.Events.Level.OnExit -= OnLevelExit;
            On.Celeste.Level.Update -= OnLevelUpdate;
        }

        private void OnLevelUpdate(On.Celeste.Level.orig_Update orig, Level level) {
            orig(level);

            if (ModSettings.TeleportToRecommended != null && ModSettings.TeleportToRecommended.Pressed) {
                TeleportToRecommendedRoom(level);
            }
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

        /// <summary>
        /// Teleport to the recommended practice room, or the first room if "Go for Gold".
        /// </summary>
        private void TeleportToRecommendedRoom(Level level) {
            var advisor = Service?.Advisor;
            if (advisor == null || !advisor.HasModels) return;

            var rec = advisor.GetRecommendation();
            if (rec == null) return;

            string targetRoom;
            if (rec.GoForGold) {
                // Teleport to the first room in the chapter
                var chapterTimings = Service.Timings.GetChapterTimings(CurrentSID);
                if (chapterTimings == null || chapterTimings.RoomOrder.Count == 0) return;
                targetRoom = chapterTimings.RoomOrder[0];
            } else {
                targetRoom = rec.PracticeRoom;
            }

            if (string.IsNullOrEmpty(targetRoom)) return;

            // Don't teleport if already in the target room
            if (level.Session.Level == targetRoom) return;

            TeleportToRoom(level, targetRoom);
        }

        /// <summary>
        /// Teleport the player to the specified room within the current chapter.
        /// Sets the session level and respawn point, then triggers a level reload.
        /// </summary>
        private void TeleportToRoom(Level level, string roomName) {
            try {
                // Find the LevelData for the target room
                LevelData targetLevelData = level.Session.MapData.Get(roomName);
                if (targetLevelData == null) {
                    Logger.Log(LogLevel.Warn, "GoldenCompass",
                        $"Cannot teleport: room '{roomName}' not found in map data.");
                    return;
                }

                // Set session to target room
                level.Session.Level = roomName;

                // Find a valid spawn point in the target room (top-left corner as reference)
                Vector2 spawnPoint = level.GetSpawnPoint(new Vector2(
                    targetLevelData.Bounds.Left,
                    targetLevelData.Bounds.Top));
                level.Session.RespawnPoint = spawnPoint;

                // Update tracking state
                _previousRoomName = null;
                CurrentRoomName = roomName;

                // Teleport by reloading the level at the new session state
                // This mirrors how the debug map teleport works
                Engine.Scene = new LevelLoader(level.Session, spawnPoint);

                Logger.Log(LogLevel.Info, "GoldenCompass",
                    $"Teleported to room: {roomName}");
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass",
                    $"Failed to teleport to room '{roomName}': {e.Message}");
            }
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