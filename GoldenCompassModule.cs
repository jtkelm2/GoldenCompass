using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Monocle;

namespace Celeste.Mod.GoldenCompass {
    public class GoldenCompassModule : EverestModule {
        public static GoldenCompassModule Instance;

        public override Type SettingsType => null;

        private static readonly string LogDir = Path.Combine("Mods", "GoldenCompassData");
        private static readonly string LogFilePath = Path.Combine(LogDir, "attempt-history.json");

        private static Dictionary<string, Dictionary<string, List<bool>>> _data;

        public GoldenCompassModule() {
            Instance = this;
        }

        public override void Load() {
            if (!Directory.Exists(LogDir))
                Directory.CreateDirectory(LogDir);

            LoadData();

            // Hook into player death and room transitions
            Everest.Events.Player.OnDie += OnPlayerDie;
            Everest.Events.Level.OnComplete += OnLevelComplete;
        }

        public override void Unload() {
            Everest.Events.Player.OnDie -= OnPlayerDie;
            Everest.Events.Level.OnComplete -= OnLevelComplete;
        }

        private void OnPlayerDie(Player player) {
            if (Engine.Scene is Level level) {
                string chapter = GetChapterName(level.Session);
                string room = level.Session.Level;
                LogAttempt(chapter, room, success: false);
            }
        }

        private void OnLevelComplete(Level level) {
            string chapter = GetChapterName(level.Session);
            string room = level.Session.Level;
            LogAttempt(chapter, room, success: true);
        }

        private static string GetChapterName(Session session) {
        string sid = session.Area.SID ?? session.Area.ToString();
        var mode = session.Area.Mode;
        string suffix = mode == AreaMode.BSide ? "_B"
                      : mode == AreaMode.CSide ? "_C"
                      : "";
        return sid + suffix;
        }

        private static void LogAttempt(string chapter, string room, bool success) {
            if (_data == null) LoadData();

            if (!_data.ContainsKey(chapter))
                _data[chapter] = new Dictionary<string, List<bool>>();

            if (!_data[chapter].ContainsKey(room))
                _data[chapter][room] = new List<bool>();

            _data[chapter][room].Add(success);
            SaveData();
        }

        private static void LoadData() {
          try {
              _data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<bool>>>>(
                  File.ReadAllText(LogFilePath));
          } catch {
              _data = new Dictionary<string, Dictionary<string, List<bool>>>();
          }
        }

        private static void SaveData() {
            try {
                File.WriteAllText(LogFilePath, JsonConvert.SerializeObject(_data, Formatting.Indented));
            } catch {
                // silently fail
            }
        }
    }
}