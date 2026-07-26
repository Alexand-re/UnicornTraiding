using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace cAlgo.Robots
{
    // ==========================================================================================
    // 1. DATA MODELS & ML FEATURE EXTRACTOR
    // ==========================================================================================
    public class MlofiMlFeatureData
    {
        public float MlofiScore { get; set; }
        public float VwapDistancePercent { get; set; }
        public float VwapSlope { get; set; }
        public float EmaRatio { get; set; }
        public float NormalizedAtr { get; set; }
        public float VolumeRatio { get; set; }
        public float Rsi14 { get; set; }
        public bool Label { get; set; }
    }

    public class MlofiMlPredictionData
    {
        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }

        [ColumnName("Probability")]
        public float Probability { get; set; }

        [ColumnName("Score")]
        public float Score { get; set; }
    }

    public static class MlofiMlFeatureExtractor
    {
        public static MlofiMlFeatureData ExtractFeatures(
            double mlofiScore,
            double currentPrice,
            double vwap,
            double vwapSlope,
            double ema20,
            double ema50,
            double atr1m,
            double currentVolume,
            double avgVolume20,
            double rsi14,
            bool label = false)
        {
            float vwapDist = vwap > 0 ? (float)((currentPrice - vwap) / vwap * 100.0) : 0f;
            float emaRat = ema50 > 0 ? (float)(ema20 / ema50) : 1.0f;
            float normAtr = currentPrice > 0 ? (float)(atr1m / currentPrice * 100.0) : 0f;
            float volRatio = avgVolume20 > 0 ? (float)(currentVolume / avgVolume20) : 1.0f;

            return new MlofiMlFeatureData
            {
                MlofiScore = (float)mlofiScore,
                VwapDistancePercent = vwapDist,
                VwapSlope = (float)vwapSlope,
                EmaRatio = emaRat,
                NormalizedAtr = normAtr,
                VolumeRatio = volRatio,
                Rsi14 = (float)rsi14,
                Label = label
            };
        }
    }

    public class MlofiMlPredictorEngine
    {
        private readonly MLContext _mlContext;
        private ITransformer? _model;
        private PredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>? _predictionEngine;

        public bool IsTrained { get; private set; }

        public MlofiMlPredictorEngine(int seed = 42)
        {
            _mlContext = new MLContext(seed: seed);
        }

        public (double Accuracy, double Auc, double Precision, int SampleCount) TrainModel(List<MlofiMlFeatureData> trainingData, Action<string> log)
        {
            if (trainingData == null || trainingData.Count < 50)
            {
                log("⚠️ Échantillons d'entraînement insuffisants (< 50). ML désactivé.");
                return (0, 0, 0, 0);
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);
            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(MlofiMlFeatureData.MlofiScore),
                    nameof(MlofiMlFeatureData.VwapDistancePercent),
                    nameof(MlofiMlFeatureData.VwapSlope),
                    nameof(MlofiMlFeatureData.EmaRatio),
                    nameof(MlofiMlFeatureData.NormalizedAtr),
                    nameof(MlofiMlFeatureData.VolumeRatio),
                    nameof(MlofiMlFeatureData.Rsi14))
                .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                    new FastTreeBinaryTrainer.Options
                    {
                        NumberOfLeaves = 20,
                        NumberOfTrees = 100,
                        MinimumExampleCountPerLeaf = 10,
                        LearningRate = 0.1,
                        LabelColumnName = nameof(MlofiMlFeatureData.Label),
                        FeatureColumnName = "Features"
                    }));

            log("🧠 Entraînement du modèle Microsoft.ML FastTree GBDT en cours...");
            _model = pipeline.Fit(dataView);

            var predictionsView = _model.Transform(dataView);
            var metrics = _mlContext.BinaryClassification.Evaluate(predictionsView, labelColumnName: nameof(MlofiMlFeatureData.Label));

            _predictionEngine = _mlContext.Model.CreatePredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>(_model);
            IsTrained = true;

            return (metrics.Accuracy, metrics.AreaUnderRocCurve, metrics.PositivePrecision, trainingData.Count);
        }

        public MlofiMlPredictionData Predict(MlofiMlFeatureData sample)
        {
            if (!IsTrained || _predictionEngine == null)
            {
                return new MlofiMlPredictionData { PredictedLabel = true, Probability = 0.5f, Score = 0f };
            }

            return _predictionEngine.Predict(sample);
        }
    }

    // ==========================================================================================
    // 2. CTRADER AUTOMATE BOT : SPY MLOFI FTMO SCALPER WITH LIVE ML TRAINING
    // ==========================================================================================
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class SpyMlofiFtmoBot : Robot
    {
        // === PARAMÈTRES ENTRÉE ===
        [Parameter("Symbol SPY Target", Group = "1. Target Setup", DefaultValue = "SPY")]
        public string TargetSymbol { get; set; } = "SPY";

        [Parameter("Capital Initial FTMO ($)", Group = "2. Risk Management FTMO", DefaultValue = 100000.0)]
        public double InitialCapital { get; set; } = 100000.0;

        [Parameter("Risque Par Trade (%)", Group = "2. Risk Management FTMO", DefaultValue = 0.30)]
        public double RiskPerTradePct { get; set; } = 0.30;

        [Parameter("Disjoncteur Jour FTMO (%)", Group = "2. Risk Management FTMO", DefaultValue = 2.5)]
        public double MaxDailyLossPct { get; set; } = 2.5;

        [Parameter("Seuil Réduction DD FTMO (%)", Group = "2. Risk Management FTMO", DefaultValue = 5.0)]
        public double MaxDrawdownPct { get; set; } = 5.0;

        [Parameter("Multiplicateur StopLoss (x ATR)", Group = "3. Bracket Orders", DefaultValue = 0.8)]
        public double SlAtrMultiplier { get; set; } = 0.8;

        [Parameter("Multiplicateur TakeProfit (x ATR)", Group = "3. Bracket Orders", DefaultValue = 1.6)]
        public double TpAtrMultiplier { get; set; } = 1.6;

        [Parameter("Activer Apprentissage ML FastTree", Group = "4. Machine Learning", DefaultValue = true)]
        public bool EnableMlTraining { get; set; } = true;

        [Parameter("Nombre de Barres Entraînement", Group = "4. Machine Learning", DefaultValue = 15000)]
        public int TrainingHistoryBars { get; set; } = 15000;

        // === ÉTAT INTERNE & ENGINES ===
        private MlofiMlPredictorEngine _mlPredictor = null!;
        private double _dailyStartEquity;
        private DateTime _currentDay = DateTime.MinValue;
        private double _peakEquity;

        protected override void OnStart()
        {
            Print("==========================================================================");
            Print($"🚀 DEMARRAGE BOT FTMO MLOFI SCALPER SPY (cTrader Automate)");
            Print($"Target: {TargetSymbol} | Capital: ${InitialCapital:N0} | Risk/Trade: {RiskPerTradePct}% | Daily Breaker: {MaxDailyLossPct}%");
            Print("==========================================================================");

            _dailyStartEquity = Account.Balance;
            _peakEquity = Account.Balance;
            _mlPredictor = new MlofiMlPredictorEngine();

            // PHASE 1 : ÉTAPE D'ENTRAÎNEMENT MACHINE LEARNING SUR L'HISTORIQUE DES BARRES
            if (EnableMlTraining)
            {
                RunTrainingPhase();
            }
        }

        private void RunTrainingPhase()
        {
            Print($"⚙️ PHASE 1 : Extraction des caractéristiques causales sur l'historique 1m ({TrainingHistoryBars} barres)...");

            var bars1m = MarketData.GetBars(TimeFrame.Minute);
            int totalBars = bars1m.Count;

            if (totalBars < 100)
            {
                Print("⚠️ Données historiques insuffisantes sur le symbole. Phase d'entraînement ignorée.");
                return;
            }

            int countToUse = Math.Min(TrainingHistoryBars, totalBars - 20);
            int startIndex = Math.Max(0, totalBars - countToUse - 20);

            List<MlofiMlFeatureData> trainingSamples = new List<MlofiMlFeatureData>();

            for (int i = startIndex + 50; i < totalBars - 20; i++)
            {
                var bar = bars1m[i];
                double closePrice = bar.Close;

                // Indice de Volume & Order Flow Imbalance
                double range = Math.Max(bar.High - bar.Low, 0.01);
                double closePos = (closePrice - bar.Low) / range;
                double buyVol = bar.TickVolume * (1.0 - closePos);
                double sellVol = bar.TickVolume * closePos;
                double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                // Technicals (EMA20, EMA50, Volume Average)
                double ema20 = 0, ema50 = 0, avgVolume = 30000;
                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = bars1m[i - k].Close;
                    double v = bars1m[i - k].TickVolume;
                    if (k < 20) { sum20 += c; sumVol += v; }
                    sum50 += c;
                }
                ema20 = sum20 / 20.0;
                ema50 = sum50 / 50.0;
                avgVolume = sumVol / 20.0;

                // RSI 14
                double gains = 0, losses = 0;
                for (int k = 0; k < 14; k++)
                {
                    double diff = bars1m[i - k].Close - bars1m[i - k - 1].Close;
                    if (diff > 0) gains += diff;
                    else losses -= diff;
                }
                double rs = losses > 0 ? gains / losses : 1.0;
                double rsi14 = 100.0 - (100.0 / (1.0 + rs));

                // VWAP approximé
                double vwap = ema20;

                // ATR 1m
                double trSum = 0;
                for (int k = 0; k < 14; k++)
                {
                    double tr1 = bars1m[i - k].High - bars1m[i - k].Low;
                    double tr2 = Math.Abs(bars1m[i - k].High - bars1m[i - k - 1].Close);
                    double tr3 = Math.Abs(bars1m[i - k].Low - bars1m[i - k - 1].Close);
                    trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                }
                double atr1m = trSum / 14.0;

                bool isVolumeSpike = bar.TickVolume >= avgVolume * 1.1;
                bool isBuy = closePrice > ema20 && mlofiScore >= 0.35 && isVolumeSpike;
                bool isSell = closePrice < ema20 && mlofiScore <= -0.35 && isVolumeSpike;

                if (!isBuy && !isSell) continue;

                // Label causale : Déterminer si le TP (+1.0 ATR) est atteint AVANT le SL (-1.0 ATR) dans les 15 barres futures
                bool label = false;
                double tp = isBuy ? closePrice + (1.0 * atr1m) : closePrice - (1.0 * atr1m);
                double sl = isBuy ? closePrice - (1.0 * atr1m) : closePrice + (1.0 * atr1m);

                for (int k = 1; k <= 15 && i + k < totalBars; k++)
                {
                    var fBar = bars1m[i + k];
                    if (isBuy)
                    {
                        if (fBar.High >= tp) { label = true; break; }
                        if (fBar.Low <= sl) { label = false; break; }
                    }
                    else
                    {
                        if (fBar.Low <= tp) { label = true; break; }
                        if (fBar.High >= sl) { label = false; break; }
                    }
                }

                var sample = MlofiMlFeatureExtractor.ExtractFeatures(
                    mlofiScore, closePrice, vwap, 0, ema20, ema50, atr1m, bar.TickVolume, avgVolume, rsi14, label);

                trainingSamples.Add(sample);
            }

            var res = _mlPredictor.TrainModel(trainingSamples, Print);

            Print("==========================================================================");
            Print($"📊 RESULTATS D'ENTRAINEMENT MACHINE LEARNING (FastTree GBDT)");
            Print($"Echantillons : {res.SampleCount} | Accuracy: {res.Accuracy * 100:F2}% | AUC: {res.Auc:F4} | Precision: {res.Precision * 100:F2}%");
            Print("==========================================================================");
        }

        // PHASE 2 : EXÉCUTION DU TRADING EN TEMPS RÉEL SUR CHAQUE NOUVELLE BARRE 1m
        protected override void OnBar()
        {
            // Reset Quotidien FTMO
            if (Server.Time.Date != _currentDay)
            {
                _currentDay = Server.Time.Date;
                _dailyStartEquity = Account.Equity;
                Print($"☀️ Nouveau jour FTMO : Equity de Départ = ${_dailyStartEquity:N2}");
            }

            if (Account.Equity > _peakEquity) _peakEquity = Account.Equity;

            // 1. Contrôle Gardien de Risque FTMO (Disjoncteur Quotidien -2.5%)
            double currentDailyDrawdownPct = (_dailyStartEquity - Account.Equity) / _dailyStartEquity * 100.0;
            if (currentDailyDrawdownPct >= MaxDailyLossPct)
            {
                Print($"⛔ DISJONCTEUR FTMO DECLENCHÉ : Perte Quotidienne = -{currentDailyDrawdownPct:F2}% (Limite = {MaxDailyLossPct}%). Trading Suspendu.");
                return;
            }

            // Gestion Break-Even sur les positions ouvertes
            ManageBreakEven();

            // Ne pas ouvrir de nouvelles positions s'il y en a déjà une active
            if (Positions.FindAll("MlofiFtmo").Length > 0) return;

            var bars1m = MarketData.GetBars(TimeFrame.Minute);
            int idx = bars1m.Count - 1;
            if (idx < 50) return;

            var bar = bars1m[idx];
            double currentPrice = bar.Close;

            // Calcul MLOFI Score synthétique
            double range = Math.Max(bar.High - bar.Low, 0.01);
            double closePos = (currentPrice - bar.Low) / range;
            double buyVol = bar.TickVolume * (1.0 - closePos);
            double sellVol = bar.TickVolume * closePos;
            double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

            // Technicals
            double sum20 = 0, sum50 = 0, sumVol = 0;
            for (int k = 0; k < 50; k++)
            {
                double c = bars1m[idx - k].Close;
                double v = bars1m[idx - k].TickVolume;
                if (k < 20) { sum20 += c; sumVol += v; }
                sum50 += c;
            }
            double ema20 = sum20 / 20.0;
            double ema50 = sum50 / 50.0;
            double avgVolume = sumVol / 20.0;

            // RSI 14
            double gains = 0, losses = 0;
            for (int k = 0; k < 14; k++)
            {
                double diff = bars1m[idx - k].Close - bars1m[idx - k - 1].Close;
                if (diff > 0) gains += diff;
                else losses -= diff;
            }
            double rs = losses > 0 ? gains / losses : 1.0;
            double rsi14 = 100.0 - (100.0 / (1.0 + rs));

            double vwap = ema20;

            // ATR 1m
            double trSum = 0;
            for (int k = 0; k < 14; k++)
            {
                double tr1 = bars1m[idx - k].High - bars1m[idx - k].Low;
                double tr2 = Math.Abs(bars1m[idx - k].High - bars1m[idx - k - 1].Close);
                double tr3 = Math.Abs(bars1m[idx - k].Low - bars1m[idx - k - 1].Close);
                trSum += Math.Max(tr1, Math.Max(tr2, tr3));
            }
            double atr1m = trSum / 14.0;

            bool isVolumeSpike = bar.TickVolume >= avgVolume * 1.1;

            bool isBuySetup = currentPrice > ema20 && mlofiScore >= 0.35 && isVolumeSpike;
            bool isSellSetup = currentPrice < ema20 && mlofiScore <= -0.35 && isVolumeSpike;

            if (!isBuySetup && !isSellSetup) return;

            // Filtre Inférence Machine Learning FastTree GBDT
            if (_mlPredictor != null && _mlPredictor.IsTrained)
            {
                var featureSample = MlofiMlFeatureExtractor.ExtractFeatures(
                    mlofiScore, currentPrice, vwap, 0, ema20, ema50, atr1m, bar.TickVolume, avgVolume, rsi14, false);

                var prediction = _mlPredictor.Predict(featureSample);

                if (prediction.Probability < 0.20f)
                {
                    return; // Rejet par le filtre ML
                }
            }

            // Calcul du Risque et Sizing
            double currentOverallDrawdownPct = (_peakEquity - Account.Equity) / _peakEquity * 100.0;
            double effectiveRiskPct = (currentOverallDrawdownPct >= MaxDrawdownPct) ? RiskPerTradePct * 0.5 : RiskPerTradePct;
            double riskBudgetDollars = Account.Equity * (effectiveRiskPct / 100.0);

            double slDistance = atr1m * SlAtrMultiplier;
            double tpDistance = atr1m * TpAtrMultiplier;

            if (slDistance <= 0.05) slDistance = 0.50;
            if (tpDistance <= 0.05) tpDistance = 1.00;

            double volumeLots = Symbol.VolumeInUnitsToQuantity(riskBudgetDollars / slDistance);
            volumeLots = Symbol.NormalizeVolumeInUnits(volumeLots, RoundingMode.ToNearest);

            if (volumeLots <= 0) return;

            TradeType tradeType = isBuySetup ? TradeType.Buy : TradeType.Sell;
            double slPips = slDistance / Symbol.PipSize;
            double tpPips = tpDistance / Symbol.PipSize;

            Print($"🎯 EXECUTION SIGNAL MLOFI FTMO : {tradeType} | Lots: {volumeLots} | SL: {slPips:F1} pips | TP: {tpPips:F1} pips");
            ExecuteMarketOrder(tradeType, Symbol.Name, volumeLots, "MlofiFtmo", slPips, tpPips);
        }

        private void ManageBreakEven()
        {
            var openPositions = Positions.FindAll("MlofiFtmo");
            foreach (var pos in openPositions)
            {
                double currentProfitPips = pos.Pips;
                double tpDistancePips = Math.Abs(pos.TakeProfit.GetValueOrDefault() - pos.EntryPrice) / Symbol.PipSize;

                // Si le profit atteint 40% de la cible TP, bouger le Stop Loss à Break-Even
                if (currentProfitPips >= tpDistancePips * 0.40)
                {
                    if (pos.TradeType == TradeType.Buy && pos.StopLoss < pos.EntryPrice)
                    {
                        ModifyPosition(pos, pos.EntryPrice, pos.TakeProfit);
                        Print($"🛡️ BREAK-EVEN APPLIQUÉ SUR POSITION #{pos.Id} (Buy @ {pos.EntryPrice})");
                    }
                    else if (pos.TradeType == TradeType.Sell && (pos.StopLoss == null || pos.StopLoss > pos.EntryPrice))
                    {
                        ModifyPosition(pos, pos.EntryPrice, pos.TakeProfit);
                        Print($"🛡️ BREAK-EVEN APPLIQUÉ SUR POSITION #{pos.Id} (Sell @ {pos.EntryPrice})");
                    }
                }
            }
        }
    }
}
