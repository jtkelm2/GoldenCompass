using System;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Renders the overlay in the top-right corner of the screen.
    /// Detail level is controlled by OverlayMode setting:
    ///   Off      - nothing rendered
    ///   Basic    - tracking status + recommendation (practice room or go for gold)
    ///              + single optimized time estimate
    ///   Detailed - adds naive vs optimized comparison, time spent, current room
    ///              success probability, and cost/benefit breakdown
    ///   Debug    - adds beta coefficients, per-room confidence flags
    /// </summary>
    public class Renderer : Entity {
        private const float Padding = 10f;
        private const float LineHeight = 24f;
        private const float Scale = 0.4f;

        /// <summary>
        /// Tracks vertical layout position for right-aligned HUD lines.
        /// </summary>
        private struct LayoutCursor {
            public readonly float X;
            private readonly float startY;
            private float line;

            public float Y => startY + line * LineHeight;

            public LayoutCursor(float x, float y) {
                X = x;
                startY = y;
                line = 0;
            }

            public void Advance(int n = 1) => line += n;
            public void Skip() => line += 1.2f;
        }

        /// <summary>
        /// Snapshot of all state needed for a single render frame,
        /// gathered once to avoid repeated null-check chains.
        /// </summary>
        private struct RenderContext {
            public OverlayMode Mode;
            public bool Tracking;
            public bool HasTimings;
            public string CurrentSID;
            public string CurrentRoom;
            public SemiomniscientAdvisor Advisor;
            public Recommendation Recommendation;
            public Service Service;

            public static RenderContext? TryCreate() {
                var module = GoldenCompassModule.Instance;
                if (module == null) return null;

                var settings = module.ModSettings;
                if (settings.OverlayMode == OverlayMode.Off) return null;

                var service = module.Service;
                var advisor = service.Advisor;
                var rec = advisor?.HasModels == true ? advisor.GetRecommendation() : null;

                return new RenderContext {
                    Mode = settings.OverlayMode,
                    Tracking = settings.TrackingEnabled,
                    HasTimings = service.HasTimingsForCurrentChapter,
                    CurrentSID = module.CurrentSID,
                    CurrentRoom = module.CurrentRoomName,
                    Advisor = advisor,
                    Recommendation = rec,
                    Service = service,
                };
            }
        }

        public Renderer() {
            Tag = Tags.HUD | Tags.Global | Tags.PauseUpdate | Tags.TransitionUpdate;
            Depth = -10000;
        }

        public override void Render() {
            var ctx = RenderContext.TryCreate();
            if (ctx == null) return;

            var c = ctx.Value;
            var cursor = new LayoutCursor(Engine.Width - Padding, Padding);

            RenderTrackingStatus(ref cursor, c);

            if (!c.HasTimings) {
                RenderNoTimingData(ref cursor, c);
                return;
            }

            if (c.Recommendation == null) {
                DrawRight("Collecting data...", ref cursor, Color.DarkGray);
                return;
            }

            RenderRecommendation(ref cursor, c);

            if (c.Mode >= OverlayMode.Basic) {
                RenderOptimizedEstimate(ref cursor, c);
            }

            if (c.Mode >= OverlayMode.Detailed) {
                RenderTimeEstimates(ref cursor, c);
                RenderCurrentRoomDetails(ref cursor, c);
            }

            if (c.Mode >= OverlayMode.Debug) {
                RenderCurrentRoomDebug(ref cursor, c);
            }
        }

        // ---- Section renderers ------------------------------------------------

        private void RenderTrackingStatus(ref LayoutCursor cursor, RenderContext ctx) {
            string text = ctx.Tracking ? "TRACKING: ON" : "TRACKING: OFF";
            Color color = ctx.Tracking ? Color.LimeGreen : Color.Gray;
            DrawRight(text, ref cursor, color);
        }

        private void RenderNoTimingData(ref LayoutCursor cursor, RenderContext ctx) {
            DrawRight("No timing data for this chapter", ref cursor, Color.DarkGray);
            DrawRight("Place file at:", ref cursor, Color.DarkGray);
            string path = TimingData.GetTimingFilePath(ctx.CurrentSID ?? "?");
            DrawRight(path, ref cursor, Color.DarkGray);
        }

        private void RenderRecommendation(ref LayoutCursor cursor, RenderContext ctx) {
            var rec = ctx.Recommendation;
            cursor.Advance(); // blank line before recommendation

            if (rec.GoForGold) {
                DrawRight(">> GO FOR GOLD <<", ref cursor, Color.Gold);
            } else {
                DrawRight($"Practice: Room {rec.PracticeRoom}", ref cursor, Color.Cyan);
                RenderPracticeReason(ref cursor, rec);
            }
        }

        private void RenderPracticeReason(ref LayoutCursor cursor, Recommendation rec) {
            switch (rec.PracticeRoomConfidence) {
                case ConfidenceLevel.InsufficientData:
                    DrawRight("(needs more data)", ref cursor, Color.Yellow * 0.7f);
                    break;

                case ConfidenceLevel.NegativeLearningRate:
                    DrawRight("(not improving - practice needed)", ref cursor, Color.Orange * 0.7f);
                    break;

                case ConfidenceLevel.Confident:
                    string advantage = FormatTime(rec.NetBenefitSeconds);
                    DrawRight($"Saves ~{advantage}/attempt", ref cursor, Color.Cyan * 0.8f);
                    break;
            }
        }

        /// <summary>
        /// Basic: single optimized estimate line beneath the recommendation.
        /// </summary>
        private void RenderOptimizedEstimate(ref LayoutCursor cursor, RenderContext ctx) {
            var rec = ctx.Recommendation;
            cursor.Advance();
            string smartTime = FormatTime(rec.SmartEstimateSeconds);
            DrawRight($"Est. remaining: {smartTime}", ref cursor, Color.LimeGreen * 0.9f);
        }

        /// <summary>
        /// Detailed: naive vs optimized comparison and time spent.
        /// </summary>
        private void RenderTimeEstimates(ref LayoutCursor cursor, RenderContext ctx) {
            var rec = ctx.Recommendation;

            string naiveTime = FormatTime(rec.NaiveEstimateSeconds);
            DrawRight($"  Naive grind: {naiveTime}", ref cursor, Color.OrangeRed * 0.9f);

            double? expended = ctx.Service.GetTimeExpended();
            if (expended != null) {
                string expendedTime = FormatTime(expended.Value);
                DrawRight($"  Time spent:  {expendedTime}", ref cursor, Color.White * 0.4f);
            }
        }

        /// <summary>
        /// Detailed: success probability, attempt count, and cost/benefit for
        /// the room the player is currently standing in.
        /// </summary>
        private void RenderCurrentRoomDetails(ref LayoutCursor cursor, RenderContext ctx) {
            if (ctx.CurrentRoom == null || ctx.Advisor == null || !ctx.Advisor.HasModels)
                return;

            var roomModel = ctx.Advisor.GetRoomModel(ctx.CurrentRoom);
            if (roomModel == null) return;

            cursor.Skip();
            DrawRight($"Current room: {ctx.CurrentRoom}", ref cursor, Color.White * 0.6f);

            double pNow = roomModel.SuccessProb(roomModel.AttemptCount);
            DrawRight($"  P(success)={pNow:F2}  attempts={roomModel.AttemptCount}", ref cursor, Color.White * 0.5f);

            var detail = ctx.Advisor.GetRoomPracticeBenefit(ctx.CurrentRoom);
            if (detail != null) {
                string benefitStr = FormatTime(detail.Value.Benefit);
                string costStr = FormatTime(detail.Value.Cost);
                string netStr = FormatTime(detail.Value.Net);
                Color netColor = detail.Value.Net > 0 ? Color.LimeGreen * 0.7f : Color.OrangeRed * 0.7f;
                DrawRight($"  Benefit: {benefitStr}  Cost: {costStr}", ref cursor, Color.White * 0.5f);
                DrawRight($"  Net: {netStr}", ref cursor, netColor);
            }
        }

        /// <summary>
        /// Debug: model internals — beta coefficients and confidence classification.
        /// </summary>
        private void RenderCurrentRoomDebug(ref LayoutCursor cursor, RenderContext ctx) {
            if (ctx.CurrentRoom == null || ctx.Advisor == null || !ctx.Advisor.HasModels)
                return;

            var roomModel = ctx.Advisor.GetRoomModel(ctx.CurrentRoom);
            if (roomModel == null) return;

            cursor.Skip();
            DrawRight($"[Debug] {ctx.CurrentRoom}", ref cursor, Color.White * 0.4f);
            DrawRight($"  b0={roomModel.Beta0:F3}  b1={roomModel.Beta1:F4}", ref cursor, Color.White * 0.4f);

            if (roomModel.LowConfidence) {
                switch (roomModel.Confidence) {
                    case ConfidenceLevel.InsufficientData:
                        DrawRight($"  (insufficient data)", ref cursor, Color.Yellow * 0.5f);
                        break;
                    case ConfidenceLevel.NegativeLearningRate:
                        DrawRight($"  (flat/declining performance)", ref cursor, Color.Orange * 0.5f);
                        break;
                }
            }
        }

        // ---- Utilities --------------------------------------------------------

        /// <summary>
        /// Draw right-aligned text at the cursor's current position,
        /// then advance the cursor by one line.
        /// </summary>
        private static void DrawRight(string text, ref LayoutCursor cursor, Color color) {
            float width = ActiveFont.Measure(text).X * Scale;
            ActiveFont.Draw(
                text,
                new Vector2(cursor.X - width, cursor.Y),
                Vector2.Zero,
                Vector2.One * Scale,
                color
            );
            cursor.Advance();
        }

        private static string FormatTime(double seconds) {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds) || seconds > 1e8)
                return "???";

            string sign = seconds < 0 ? "-" : "";
            var ts = TimeSpan.FromSeconds(Math.Abs(seconds));

            if (ts.TotalHours >= 1)
                return $"{sign}{(int)ts.TotalHours}h {ts.Minutes}m";
            if (ts.TotalMinutes >= 1)
                return $"{sign}{ts.Minutes}m {ts.Seconds}s";
            return $"{sign}{ts.Seconds}s";
        }
    }
}