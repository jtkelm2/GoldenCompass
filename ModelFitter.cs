using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;
using MathNet.Numerics.Optimization.ObjectiveFunctions;

namespace Celeste.Mod.GoldenCompass {
    /// <summary>
    /// Parameters for a fitted logistic model on a single room.
    /// P(success | attempt n) = sigmoid(beta0 + beta1 * n)
    /// </summary>
    public class RoomModel {
        public double Beta0 { get; set; }
        public double Beta1 { get; set; }
        public double Time { get; set; }
        public int AttemptCount { get; set; }
        public bool LowConfidence { get; set; }

        /// <summary>
        /// Probability of success on the given attempt (0-indexed).
        /// </summary>
        public double SuccessProb(int attempt) {
            return Sigmoid(Beta0 + Beta1 * attempt);
        }

        /// <summary>
        /// Time for an attempt: full time if success, half if death.
        /// </summary>
        public double AttemptTime(bool success) {
            return success ? Time : Time / 2.0;
        }

        private static double Sigmoid(double x) {
            if (x >= 0) {
                double ez = Math.Exp(-x);
                return 1.0 / (1.0 + ez);
            } else {
                double ez = Math.Exp(x);
                return ez / (1.0 + ez);
            }
        }
    }

    /// <summary>
    /// Fits logistic regression models to room attempt data using MLE
    /// via MathNet.Numerics BfgsMinimizer.
    /// </summary>
    public static class ModelFitter {
        /// <summary>
        /// Fit a logistic model to a sequence of success/fail outcomes.
        /// </summary>
        /// <param name="attempts">List of success/failure outcomes.</param>
        /// <param name="time">Room completion time in seconds.</param>
        public static RoomModel Fit(List<bool> attempts, double time) {
            int n = attempts.Count;

            if (n == 0) {
                return new RoomModel {
                    Beta0 = 0.0,
                    Beta1 = 0.0,
                    Time = time,
                    AttemptCount = 0,
                    LowConfidence = true
                };
            }

            double successRate = attempts.Count(a => a) / (double)n;
            successRate = Clamp(successRate, 0.01, 0.99);

            // Below threshold: use constant probability model
            if (n < GoldenCompassModule.Instance.ModSettings.MinAttemptsForFit) {
                return new RoomModel {
                    Beta0 = Math.Log(successRate / (1.0 - successRate)),
                    Beta1 = 0.0,
                    Time = time,
                    AttemptCount = n,
                    LowConfidence = true
                };
            }

            return FitLogistic(attempts, time);
        }

        private static RoomModel FitLogistic(List<bool> attempts, double time) {
            int n = attempts.Count;
            double[] t = new double[n];
            double[] y = new double[n];
            for (int i = 0; i < n; i++) {
                t[i] = i;
                y[i] = attempts[i] ? 1.0 : 0.0;
            }

            // Negative log-likelihood
            double objective(Vector<double> p)
            {
              double b0 = p[0], b1 = p[1];
              double nll = 0.0;
              for (int i = 0; i < n; i++)
              {
                double eta = b0 + b1 * t[i];
                double prob = StableSigmoid(eta);
                prob = Clamp(prob, 1e-10, 1.0 - 1e-10);
                nll -= y[i] * Math.Log(prob) + (1.0 - y[i]) * Math.Log(1.0 - prob);
              }
              return nll;
            }

            // Gradient of negative log-likelihood
            Vector<double> gradient(Vector<double> p)
            {
              double b0 = p[0], b1 = p[1];
              double g0 = 0.0, g1 = 0.0;
              for (int i = 0; i < n; i++)
              {
                double eta = b0 + b1 * t[i];
                double prob = StableSigmoid(eta);
                double residual = prob - y[i];
                g0 += residual;
                g1 += residual * t[i];
              }
              return Vector<double>.Build.DenseOfArray(new[] { g0, g1 });
            }

            var objectiveFunc = ObjectiveFunction.Gradient((Func<Vector<double>, double>)objective, (Func<Vector<double>, Vector<double>>)gradient);
            var initialGuess = Vector<double>.Build.DenseOfArray(new[] { 0.0, 0.0 });

            double beta0, beta1;
            bool lowConfidence = false;

            try {
                var solver = new BfgsMinimizer(1e-8, 1e-8, 1e-8, 1000);
                var result = solver.FindMinimum(objectiveFunc, initialGuess);
                beta0 = result.MinimizingPoint[0];
                beta1 = result.MinimizingPoint[1];
            } catch (Exception e) {
                Logger.Log(LogLevel.Warn, "GoldenCompass",
                    $"Logistic fit failed, using constant model: {e.Message}");
                double sr = attempts.Count(a => a) / (double)n;
                sr = Clamp(sr, 0.01, 0.99);
                return new RoomModel {
                    Beta0 = Math.Log(sr / (1.0 - sr)),
                    Beta1 = 0.0,
                    Time = time,
                    AttemptCount = n,
                    LowConfidence = true
                };
            }

            // Negative learning rate: fall back to constant model
            if (beta1 < 0) {
                Logger.Log(LogLevel.Info, "GoldenCompass",
                    "Negative learning rate detected; using constant model.");
                double sr = attempts.Count(a => a) / (double)n;
                sr = Clamp(sr, 0.01, 0.99);
                beta0 = Math.Log(sr / (1.0 - sr));
                beta1 = 0.0;
                lowConfidence = true;
            }

            // Confidence check via Fisher information
            if (!lowConfidence) {
                lowConfidence = ComputeLowConfidence(beta0, beta1, t, n);
            }

            return new RoomModel {
                Beta0 = beta0,
                Beta1 = beta1,
                Time = time,
                AttemptCount = n,
                LowConfidence = lowConfidence
            };
        }

        /// <summary>
        /// Check if SE(beta1) > |beta1| using the Fisher information matrix.
        /// </summary>
        private static bool ComputeLowConfidence(double beta0, double beta1, double[] t, int n) {
            try {
                double h00 = 0, h01 = 0, h11 = 0;
                for (int i = 0; i < n; i++) {
                    double eta = beta0 + beta1 * t[i];
                    double p = StableSigmoid(eta);
                    double w = p * (1.0 - p);
                    h00 += w;
                    h01 += w * t[i];
                    h11 += w * t[i] * t[i];
                }

                double det = h00 * h11 - h01 * h01;
                if (Math.Abs(det) < 1e-15) return true;

                double varBeta1 = h00 / det;
                if (varBeta1 < 0) return true;

                double seBeta1 = Math.Sqrt(varBeta1);
                return seBeta1 > Math.Abs(beta1);
            } catch {
                return true;
            }
        }

        private static double StableSigmoid(double x) {
            if (x >= 0) {
                double ez = Math.Exp(-x);
                return 1.0 / (1.0 + ez);
            } else {
                double ez = Math.Exp(x);
                return ez / (1.0 + ez);
            }
        }

        private static double Clamp(double value, double min, double max) {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}