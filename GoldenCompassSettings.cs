namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Overlay detail levels.
    /// </summary>
    public enum OverlayMode {
        Off,
        Basic,
        Extra,
        All
    }

    /// <summary>
    /// Module settings exposed in Everest's mod options menu.
    /// </summary>
    public class GoldenCompassSettings : EverestModuleSettings {
        /// <summary>
        /// Whether attempt tracking is currently enabled.
        /// </summary>
        public bool TrackingEnabled { get; set; } = true;

        /// <summary>
        /// Overlay detail level: Off, Basic, Extra, or All.
        /// </summary>
        public OverlayMode OverlayMode { get; set; } = OverlayMode.Basic;

        /// <summary>
        /// Minimum attempts before fitting a logistic model (vs constant probability).
        /// </summary>
        [SettingRange(5, 50)]
        public int MinAttemptsForFit { get; set; } = 15;

        /// <summary>
        /// How often to refit models, in attempts per room.
        /// Lower = more responsive but slightly more CPU.
        /// </summary>
        [SettingRange(1, 20)]
        public int RefitInterval { get; set; } = 5;

        // -- Actions (buttons in the settings menu) --

        /// <summary>
        /// Clear data for the current chapter.
        /// Set by the module to point at the current chapter's SID.
        /// </summary>
        [SettingIgnore]
        public string CurrentSID { get; set; }
    }
}