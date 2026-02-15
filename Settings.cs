namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Overlay detail levels.
    /// </summary>
    public enum OverlayMode {
        Off,
        Basic,
        Extra,
        Debug
    }

    /// <summary>
    /// Module settings exposed in Everest's mod options menu.
    /// </summary>
    public class Settings : EverestModuleSettings {
        /// <summary>
        /// Whether attempt tracking is currently enabled.
        /// </summary>
        public bool TrackingEnabled { get; set; } = true;

        /// <summary>
        /// Overlay detail level: Off, Basic, Extra, or Debug.
        /// </summary>
        public OverlayMode OverlayMode { get; set; } = OverlayMode.Basic;

        /// <summary>
        /// Minimum attempts before fitting a logistic model (vs constant probability).
        /// </summary>
        [SettingRange(5, 50)]
        public int MinAttemptsForFit { get; set; } = 15;

        // -- Actions (buttons in the settings menu) --

        /// <summary>
        /// Clear data for the current chapter.
        /// Set by the module to point at the current chapter's SID.
        /// </summary>
        [SettingIgnore]
        public string CurrentSID { get; set; }
    }
}