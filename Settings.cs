using Microsoft.Xna.Framework.Input;

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

        /// <summary>
        /// When a room has enough data but a negative learning rate (flat model fallback),
        /// it is marked low confidence only if its predicted success rate is below this %.
        /// </summary>
        [SettingRange(5, 100)]
        public int NegativeBetaConfidenceThreshold { get; set; } = 80;

        /// <summary>
        /// Key binding to teleport to the recommended practice room.
        /// When the recommendation is "Go for Gold", teleports to the first room instead.
        /// </summary>
        [DefaultButtonBinding(0, Keys.F8)]
        public ButtonBinding TeleportToRecommended { get; set; }

        // -- Actions (buttons in the settings menu) --

        /// <summary>
        /// Clear data for the current chapter.
        /// Set by the module to point at the current chapter's SID.
        /// </summary>
        [SettingIgnore]
        public string CurrentSID { get; set; }
    }
}