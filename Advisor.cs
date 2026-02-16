using System;
using System.Collections.Generic;
using System.Linq;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Recommendation from the advisor.
    /// </summary>
    public class Recommendation {
        /// <summary>If true, the player should attempt the golden berry.</summary>
        public bool GoForGold { get; set; }

        /// <summary>Room to practice next (null if GoForGold).</summary>
        public string PracticeRoom { get; set; }

        /// <summary>
        /// Expected time saved per practice attempt on the recommended room (seconds).
        /// Net benefit = benefit - cost. Only meaningful when not GoForGold.
        /// </summary>
        public double NetBenefitSeconds { get; set; }

        /// <summary>
        /// Estimated time to completion via naive grinding (seconds).
        /// Uses mean-field simulation with passive improvement only (no deliberate practice).
        /// </summary>
        public double NaiveEstimateSeconds { get; set; }

        /// <summary>
        /// Estimated time to completion via semiomniscient strategy (seconds).
        /// Uses mean-field simulation with optimal practice between attempt rounds.
        /// </summary>
        public double SmartEstimateSeconds { get; set; }
    }

    /// <summary>
    /// Benefit/cost breakdown for practicing a specific room.
    /// </summary>
    public struct RoomPracticeBenefit {
        public double Benefit;
        public double Cost;
        public double Net;
    }

    /// <summary>
    /// Implements the semiomniscient strategy: recommends which room to practice
    /// or whether to go for the golden berry, based on fitted room models.
    /// </summary>
    public class SemiomniscientAdvisor {
        private List<string> _roomOrder;
        private Dictionary<string, RoomModel> _models;

        // Cached from last GetRecommendation call
        private Dictionary<string, double> _currentProbs;

        // Cache the recommendation and practice benefit to avoid recalculating every frame
        private Recommendation _cachedRecommendation;
        private RoomPracticeBenefit? _cachedPracticeBenefit;
        private String _cachedRoom;

        public SemiomniscientAdvisor() {
            _roomOrder = new List<string>();
            _models = new Dictionary<string, RoomModel>();
        }

        /// <summary>
        /// Update the advisor with new models. Called after refitting.
        /// Room order is taken from the order of keys provided.
        /// </summary>
        public void UpdateModels(List<string> roomOrder, Dictionary<string, RoomModel> models) {
            _roomOrder = new List<string>(roomOrder);
            _models = new Dictionary<string, RoomModel>(models);

            _currentProbs = new Dictionary<string, double>();
            foreach (string room in _roomOrder) {
              _currentProbs[room] = _models[room].SuccessProb(_models[room].AttemptCount);
            }

            _cachedRecommendation = null;
            _cachedPracticeBenefit = null;
        }

        /// <summary>
        /// Whether the advisor has enough data to produce a recommendation.
        /// </summary>
        public bool HasModels => _roomOrder.Count > 0 && _models.Count > 0;

        /// <summary>
        /// Get the RoomModel for a specific room, or null if not found.
        /// </summary>
        public RoomModel GetRoomModel(string room) {
            RoomModel model;
            return _models.TryGetValue(room, out model) ? model : null;
        }

        /// <summary>
        /// Get the cost/benefit breakdown for practicing a specific room.
        /// Returns null if the room is not in the model set or no recommendation data is available.
        /// </summary>
        public RoomPracticeBenefit? GetRoomPracticeBenefit(string room) {
            if (room == _cachedRoom && _cachedPracticeBenefit != null)
              return _cachedPracticeBenefit;

            if (_currentProbs == null || !_models.ContainsKey(room) || !_currentProbs.ContainsKey(room)) {
                if (_currentProbs == null) {
                  Logger.Log(LogLevel.Info, "GoldenCompass", "_currentProbs == null");
                  return null;
                }
                if (!_models.ContainsKey(room))
                  Logger.Log(LogLevel.Info, "GoldenCompass", $"!_models.ContainsKey({room})");
                if (!_currentProbs.ContainsKey(room))
                  Logger.Log(LogLevel.Info, "GoldenCompass", $"!_currentProbs.ContainsKey({room})");
                return null;
            }

            var model = _models[room];
            double pNew = model.SuccessProb(model.AttemptCount + 1);

            var currentE0 = ComputeE0(_currentProbs);

            var newProbs = new Dictionary<string, double>(_currentProbs);
            newProbs[room] = pNew;
            double newE0 = ComputeE0(newProbs);

            double benefit = currentE0 - newE0;
            double cost = model.Time * (1.0 + _currentProbs[room]) / 2.0;

            _cachedRoom = room;
            _cachedPracticeBenefit = new RoomPracticeBenefit {
                Benefit = benefit,
                Cost = cost,
                Net = benefit - cost
            };

            return _cachedPracticeBenefit;
        }

        /// <summary>
        /// Compute the current recommendation.
        /// </summary>
        public Recommendation GetRecommendation() {
            if (!HasModels) return null;

            if (_cachedRecommendation != null)
                return _cachedRecommendation;

            var currentE0 = ComputeE0(_currentProbs);

            // Find the room with the best net benefit from one more practice attempt
            string bestRoom = null;
            double bestNet = 0.0; // threshold: must be positive to recommend practice

            foreach (string room in _roomOrder) {
                var model = _models[room];
                double pNew = model.SuccessProb(model.AttemptCount + 1);

                var newProbs = new Dictionary<string, double>(_currentProbs);
                newProbs[room] = pNew;
                double newE0 = ComputeE0(newProbs);

                double benefit = currentE0 - newE0;
                double cost = model.Time * (1.0 + _currentProbs[room]) / 2.0;
                double net = benefit - cost;

                if (net > bestNet) {
                    bestNet = net;
                    bestRoom = room;
                }
            }

            // Compute forward-looking estimates via mean-field simulation
            double naiveEstimate = ComputeMeanFieldEstimate(withPractice: false);
            double smartEstimate = ComputeMeanFieldEstimate(withPractice: true);

            _cachedRecommendation = new Recommendation {
                GoForGold = bestRoom == null,
                PracticeRoom = bestRoom,
                NetBenefitSeconds = bestNet,
                NaiveEstimateSeconds = naiveEstimate,
                SmartEstimateSeconds = smartEstimate
            };

            return _cachedRecommendation;
        }

        // ──────────────────────────────────────────────────────────────
        //  Mean-field forward simulation
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate total expected time to completion using a mean-field (fluid)
        /// approximation. Replaces stochastic flip counts with their expected
        /// trajectories, yielding a deterministic iteration.
        ///
        /// When withPractice = false, simulates naive grinding: the player
        /// repeatedly attempts the golden run, with probabilities improving
        /// passively from the attempt flips alone.
        ///
        /// When withPractice = true, simulates the semiomniscient strategy:
        /// between each attempt round, the player practices the room with the
        /// highest net marginal benefit until no room's benefit exceeds its cost.
        /// </summary>
        private double ComputeMeanFieldEstimate(bool withPractice) {
            int n = _roomOrder.Count;
            double[] times = new double[n];
            double[] flipCounts = new double[n];

            for (int i = 0; i < n; i++) {
                times[i] = _models[_roomOrder[i]].Time;
                flipCounts[i] = _models[_roomOrder[i]].AttemptCount;
            }

            double totalCost = 0.0;
            double survival = 1.0;
            const int maxRounds = 100000;
            const double epsilon = 1e-12;
            const int maxPracticePerRound = 1000;

            double[] probs = new double[n];

            for (int round = 0; round < maxRounds && survival > epsilon; round++) {
                // Snapshot current probabilities
                for (int i = 0; i < n; i++) {
                    probs[i] = GetProb(i, flipCounts[i]);
                }

                // ── Practice phase (only at progress k=0) ──
                if (withPractice) {
                    for (int iter = 0; iter < maxPracticePerRound; iter++) {
                        double e0 = ComputeE0(probs, times, n);

                        int bestIdx = -1;
                        double bestNet = 0.0;

                        for (int i = 0; i < n; i++) {
                            double pOld = probs[i];
                            double pNew = GetProb(i, flipCounts[i] + 1);

                            // Temporarily swap to compute new E0
                            probs[i] = pNew;
                            double newE0 = ComputeE0(probs, times, n);
                            probs[i] = pOld; // restore

                            double benefit = e0 - newE0;
                            double cost = times[i] * (1.0 + pOld) / 2.0;
                            double net = benefit - cost;

                            if (net > bestNet) {
                                bestNet = net;
                                bestIdx = i;
                            }
                        }

                        if (bestIdx < 0) break;

                        // Commit the practice flip
                        totalCost += survival * times[bestIdx] * (1.0 + probs[bestIdx]) / 2.0;
                        flipCounts[bestIdx] += 1.0;
                        probs[bestIdx] = GetProb(bestIdx, flipCounts[bestIdx]);
                    }
                }

                // ── Attempt round ──

                // Success probability: product of all p_i
                double S = 1.0;
                for (int i = 0; i < n; i++) S *= probs[i];

                // Expected cost of one attempt round
                double roundCost = 0.0;
                double prodPrev = 1.0;
                for (int i = 0; i < n; i++) {
                    roundCost += times[i] * (1.0 + probs[i]) / 2.0 * prodPrev;
                    prodPrev *= probs[i];
                }

                totalCost += survival * roundCost;

                // Mean-field update: coin j is reached with prob prod_{m<j} p_m
                prodPrev = 1.0;
                for (int i = 0; i < n; i++) {
                    flipCounts[i] += prodPrev;
                    prodPrev *= probs[i];
                }

                survival *= (1.0 - S);
            }

            return totalCost;
        }

        private double GetProb(int roomIdx, double n) {
            var model = _models[_roomOrder[roomIdx]];
            return model.SuccessProb(n);
        }

        // ──────────────────────────────────────────────────────────────
        //  E0 computation
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Expected time to complete a full golden run given current probabilities.
        ///
        /// E0 = sum_j [ a_j / prod_{m=j..n} p_m ]
        ///    = (1/P) * sum_j [ a_j * prod_{m &lt; j} p_m ]
        ///
        /// where a_j = t_j * (1 + p_j) / 2 is the expected time per visit to room j.
        /// </summary>
        private double ComputeE0(Dictionary<string, double> probs) {
            double P = 1.0;
            foreach (string room in _roomOrder)
                P *= probs[room];

            if (P < 1e-15) return double.MaxValue;

            double total = 0.0;
            double prodPrev = 1.0;
            foreach (string room in _roomOrder) {
                double p = probs[room];
                double t = _models[room].Time;
                double a = t * (1.0 + p) / 2.0;
                total += a * prodPrev;
                prodPrev *= p;
            }

            return total / P;
        }

        /// <summary>
        /// Array-based overload of ComputeE0 for use in the mean-field inner loop,
        /// avoiding dictionary allocation.
        /// </summary>
        private double ComputeE0(double[] probs, double[] times, int n) {
            double P = 1.0;
            for (int i = 0; i < n; i++) P *= probs[i];

            if (P < 1e-15) return double.MaxValue;

            double total = 0.0;
            double prodPrev = 1.0;
            for (int i = 0; i < n; i++) {
                total += times[i] * (1.0 + probs[i]) / 2.0 * prodPrev;
                prodPrev *= probs[i];
            }

            return total / P;
        }
    }
}