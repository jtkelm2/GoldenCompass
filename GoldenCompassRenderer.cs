using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Renders the GoldenCompass overlay in the top-right corner of the screen.
    /// Shows: tracking status, recommendation, time estimates.
    /// </summary>
    public class GoldenCompassRenderer : Entity {
        private const float Padding = 10f;
        private const float LineHeight = 28f;
        private const float Scale = 0.5f;

        public GoldenCompassRenderer() {
            Tag = Tags.HUD | Tags.Global | Tags.PauseUpdate | Tags.TransitionUpdate;
            Depth = -10000;
        }

        public override void Render() {
            if (!GoldenCompassModule.Instance.ModSettings.OverlayVisible)
                return;

            var settings = GoldenCompassModule.Instance.ModSettings;
            var advisor = GoldenCompassModule.Instance.Advisor;
            var rec = advisor?.HasModels == true ? advisor.GetRecommendation() : null;
            bool tracking = settings.TrackingEnabled;
            bool hasTimings = GoldenCompassModule.Instance.HasTimingsForCurrentChapter;

            float x = Engine.Width - Padding;
            float y = Padding;
            int line = 0;

            // Tracking status
            string trackingText = tracking ? "TRACKING: ON" : "TRACKING: OFF";
            Color trackingColor = tracking ? Color.LimeGreen : Color.Gray;
            DrawRight(trackingText, x, y + line * LineHeight, trackingColor);
            line++;

            if (!hasTimings) {
                DrawRight("No timing data for this chapter", x, y + line * LineHeight, Color.DarkGray);
                line++;
                DrawRight("Place file at:", x, y + line * LineHeight, Color.DarkGray);
                line++;
                string path = TimingData.GetTimingFilePath(settings.CurrentSID ?? "?");
                DrawRight(path, x, y + line * LineHeight, Color.DarkGray);
                return;
            }

            if (rec == null) {
                DrawRight("Collecting data...", x, y + line * LineHeight, Color.DarkGray);
                return;
            }

            // Recommendation
            line++;
            if (rec.GoForGold) {
                DrawRight(">> GO FOR GOLD <<", x, y + line * LineHeight, Color.Gold);
            } else {
                DrawRight($"Practice: Room {rec.PracticeRoom}", x, y + line * LineHeight, Color.Cyan);
                line++;
                string advantage = FormatTime(rec.NetBenefitSeconds);
                DrawRight($"Saves ~{advantage}/attempt", x, y + line * LineHeight, Color.Cyan * 0.8f);
            }

            // Time estimates
            line += 2;
            DrawRight("Est. time remaining:", x, y + line * LineHeight, Color.White * 0.7f);
            line++;
            string naiveTime = FormatTime(rec.NaiveEstimateSeconds);
            DrawRight($"  Naive grind: {naiveTime}", x, y + line * LineHeight, Color.OrangeRed * 0.9f);
            line++;
            string smartTime = FormatTime(rec.SmartEstimateSeconds);
            DrawRight($"  Optimized:   {smartTime}", x, y + line * LineHeight, Color.LimeGreen * 0.9f);

            // Confidence warning
            if (rec.AnyLowConfidence) {
                line += 2;
                DrawRight("(low data - estimates rough)", x, y + line * LineHeight, Color.Yellow * 0.6f);
            }
        }

        private static void DrawRight(string text, float rightX, float y, Color color) {
            float width = ActiveFont.Measure(text).X * Scale;
            ActiveFont.Draw(text, new Vector2(rightX - width, y), Vector2.Zero, Vector2.One * Scale, color);
        }

        private static string FormatTime(double seconds) {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds > 1e8)
                return "???";

            int totalSeconds = (int)seconds;
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;

            if (hours > 0)
                return $"{hours}h {minutes}m";
            if (minutes > 0)
                return $"{minutes}m {secs}s";
            return $"{secs}s";
        }
    }
}