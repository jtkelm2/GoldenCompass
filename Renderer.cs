using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Renders the GoldenCompass overlay in the top-right corner of the screen.
    /// Detail level is controlled by OverlayMode setting:
    ///   Off   - nothing rendered
    ///   Basic - tracking status + recommendation (practice room or go for gold)
    ///   Extra - adds time estimates, cost/benefit of current room, beta values for current room
    ///   Debug - same as Extra plus Fisher information matrix for current room
    /// </summary>
    public class Renderer : Entity {
        private const float Padding = 10f;
        private const float LineHeight = 28f;
        private const float Scale = 0.5f;

        public Renderer() {
            Tag = Tags.HUD | Tags.Global | Tags.PauseUpdate | Tags.TransitionUpdate;
            Depth = -10000;
        }

        public override void Render() {
            var module = GoldenCompassModule.Instance;
            if (module == null) return;

            var settings = module.ModSettings;
            if (settings.OverlayMode == OverlayMode.Off)
                return;

            var service = module.Service;
            var advisor = service.Advisor;
            var rec = advisor?.HasModels == true ? advisor.GetRecommendation() : null;
            bool tracking = settings.TrackingEnabled;
            bool hasTimings = service.HasTimingsForCurrentChapter;

            float x = Engine.Width - Padding;
            float y = Padding;
            int line = 0;

            // --- Always shown (Basic and above) ---

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
                string path = TimingData.GetTimingFilePath(module.CurrentSID ?? "?");
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
                RenderPracticeReason(rec, x, y, ref line);
            }

            // --- Extra / Debug ---
            if (settings.OverlayMode >= OverlayMode.Extra) {
                // Time estimates
                line += 2;
                DrawRight("Est. time remaining:", x, y + line * LineHeight, Color.White * 0.7f);
                line++;
                string naiveTime = FormatTime(rec.NaiveEstimateSeconds);
                DrawRight($"  Naive grind: {naiveTime}", x, y + line * LineHeight, Color.OrangeRed * 0.9f);
                line++;
                string smartTime = FormatTime(rec.SmartEstimateSeconds);
                DrawRight($"  Optimized:   {smartTime}", x, y + line * LineHeight, Color.LimeGreen * 0.9f);

                // Current room cost/benefit and model params
                RenderCurrentRoomDetails(x, y, ref line);
            }
        }

        /// <summary>
        /// Render the reason line beneath a practice recommendation.
        /// </summary>
        private void RenderPracticeReason(Recommendation rec, float x, float y, ref int line) {
            switch (rec.PracticeRoomConfidence) {
                case ConfidenceLevel.InsufficientData:
                    DrawRight("(needs more data)", x, y + line * LineHeight, Color.Yellow * 0.7f);
                    break;

                case ConfidenceLevel.NegativeLearningRate:
                    DrawRight("(not improving - practice needed)", x, y + line * LineHeight, Color.Orange * 0.7f);
                    break;

                case ConfidenceLevel.Confident:
                    string advantage = FormatTime(rec.NetBenefitSeconds);
                    DrawRight($"Saves ~{advantage}/attempt", x, y + line * LineHeight, Color.Cyan * 0.8f);
                    break;
            }
        }

        /// <summary>
        /// Show cost/benefit and beta values for the current room the player is in.
        /// </summary>
        private void RenderCurrentRoomDetails(float x, float y, ref int line) {
            var module = GoldenCompassModule.Instance;
            if (module == null) return;

            string currentRoom = module.CurrentRoomName;
            if (currentRoom == null) return;

            var advisor = module.Service.Advisor;
            if (advisor == null || !advisor.HasModels) return;

            var roomModel = advisor.GetRoomModel(currentRoom);
            if (roomModel == null) return;

            line += 2;
            DrawRight($"Current room: {currentRoom}", x, y + line * LineHeight, Color.White * 0.6f);
            line++;

            // Beta values
            DrawRight($"  b0={roomModel.Beta0:F3}  b1={roomModel.Beta1:F4}", x, y + line * LineHeight, Color.White * 0.5f);
            line++;

            // Current success probability
            double pNow = roomModel.SuccessProb(roomModel.AttemptCount);
            DrawRight($"  P(success)={pNow:F2}  attempts={roomModel.AttemptCount}", x, y + line * LineHeight, Color.White * 0.5f);
            line++;

            // Cost/benefit of practicing this room
            var detail = advisor.GetRoomPracticeBenefit(currentRoom);
            if (detail != null) {
                string benefitStr = FormatTime(detail.Value.Benefit);
                string costStr = FormatTime(detail.Value.Cost);
                string netStr = FormatTime(detail.Value.Net);
                Color netColor = detail.Value.Net > 0 ? Color.LimeGreen * 0.7f : Color.OrangeRed * 0.7f;
                DrawRight($"  Benefit: {benefitStr}  Cost: {costStr}", x, y + line * LineHeight, Color.White * 0.5f);
                line++;
                DrawRight($"  Net: {netStr}", x, y + line * LineHeight, netColor);
            }

            // Per-room confidence warning for current room
            if (roomModel.LowConfidence) {
                line++;
                switch (roomModel.Confidence) {
                    case ConfidenceLevel.InsufficientData:
                        DrawRight($"  (insufficient data for {currentRoom})", x, y + line * LineHeight, Color.Yellow * 0.6f);
                        break;
                    case ConfidenceLevel.NegativeLearningRate:
                        DrawRight($"  (flat/declining performance in {currentRoom})", x, y + line * LineHeight, Color.Orange * 0.6f);
                        break;
                }
            }
        }

        private static void DrawRight(string text, float rightX, float y, Color color) {
            float width = ActiveFont.Measure(text).X * Scale;
            ActiveFont.Draw(text, new Vector2(rightX - width, y), Vector2.Zero, Vector2.One * Scale, color);
        }

        private static string FormatTime(double seconds) {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds > 1e8)
                return "???";

            int totalSeconds = (int)Math.Abs(seconds);
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            string sign = seconds < 0 ? "-" : "";

            if (hours > 0)
                return $"{sign}{hours}h {minutes}m";
            if (minutes > 0)
                return $"{sign}{minutes}m {secs}s";
            return $"{sign}{secs}s";
        }
    }
}