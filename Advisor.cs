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
        /// </summary>
        public double NaiveEstimateSeconds { get; set; }

        /// <summary>
        /// Estimated time to completion via semiomniscient strategy (seconds).
        /// This is the current E0 — expected time of a single golden attempt cycle.
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
        private double _currentE0;

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
            _currentProbs = null;
            _currentE0 = 0;
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
            if (_currentProbs == null || !_models.ContainsKey(room) || !_currentProbs.ContainsKey(room))
                return null;

            var model = _models[room];
            double pNew = model.SuccessProb(model.AttemptCount + 1);

            var newProbs = new Dictionary<string, double>(_currentProbs);
            newProbs[room] = pNew;
            double newE0 = ComputeE0(newProbs);

            double benefit = _currentE0 - newE0;
            double cost = model.Time * (1.0 + _currentProbs[room]) / 2.0;

            return new RoomPracticeBenefit {
                Benefit = benefit,
                Cost = cost,
                Net = benefit - cost
            };
        }

        /// <summary>
        /// Compute the current recommendation.
        /// </summary>
        public Recommendation GetRecommendation() {
            if (!HasModels) return null;

            _currentProbs = new Dictionary<string, double>();
            foreach (string room in _roomOrder) {
                _currentProbs[room] = _models[room].SuccessProb(_models[room].AttemptCount);
            }

            _currentE0 = ComputeE0(_currentProbs);

            // Find the room with the best net benefit from one more practice attempt
            string bestRoom = null;
            double bestNet = 0.0; // threshold: must be positive to recommend practice

            foreach (string room in _roomOrder) {
                var model = _models[room];
                double pNew = model.SuccessProb(model.AttemptCount + 1);

                var newProbs = new Dictionary<string, double>(_currentProbs);
                newProbs[room] = pNew;
                double newE0 = ComputeE0(newProbs);

                double benefit = _currentE0 - newE0;
                double cost = model.Time * (1.0 + _currentProbs[room]) / 2.0;
                double net = benefit - cost;

                if (net > bestNet) {
                    bestNet = net;
                    bestRoom = room;
                }
            }

            double naiveEstimate = ComputeNaiveEstimate(_currentProbs);

            return new Recommendation {
                GoForGold = bestRoom == null,
                PracticeRoom = bestRoom,
                NetBenefitSeconds = bestNet,
                NaiveEstimateSeconds = naiveEstimate,
                SmartEstimateSeconds = _currentE0
            };
        }

        /// <summary>
        /// Expected time to complete a full golden run given current probabilities.
        /// 
        /// E0 = (1/P) * sum_j [ a_j * prod_{k &lt; j} p_k ]
        /// where P = product of all p_j, and a_j = t_j * (1 + p_j) / 2
        /// 
        /// This represents the expected time of one "cycle" of naive grinding
        /// at the current skill level, divided by the probability of success,
        /// giving the expected total time until completion.
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
        /// Estimate time to completion via naive grinding.
        /// This is simply E0 at the current skill level — the expected time
        /// if the player just does full runs from now without further practice.
        /// </summary>
        private double ComputeNaiveEstimate(Dictionary<string, double> probs) {
            return ComputeE0(probs);
        }
    }
}