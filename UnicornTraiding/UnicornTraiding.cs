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
    public struct SimpleBar
    {
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double TickVolume { get; set; }
    }

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

            int trainCount = (int)(trainingData.Count * 0.80);
            var trainSamples = trainingData.Take(trainCount).ToList();
            var testSamples = trainingData.Skip(trainCount).ToList();

            var trainDataView = _mlContext.Data.LoadFromEnumerable(trainSamples);
            var testDataView = _mlContext.Data.LoadFromEnumerable(testSamples.Count > 0 ? testSamples : trainSamples);

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
                        NumberOfLeaves = 15,
                        NumberOfTrees = 100,
                        MinimumExampleCountPerLeaf = 10,
                        LearningRate = 0.05,
                        LabelColumnName = nameof(MlofiMlFeatureData.Label),
                        FeatureColumnName = "Features"
                    }));

            log($"🧠 Entraînement FastTree GBDT sur {trainSamples.Count} échantillons (Évaluation Hors-Éch. sur {testSamples.Count})...");
            _model = pipeline.Fit(trainDataView);

            var predictionsView = _model.Transform(testDataView);
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
    public class UnicornTraiding : Robot
    {
        // === PARAMÈTRES ENTRÉE ===
        [Parameter("Symbol SPY Target", Group = "1. Target Setup", DefaultValue = "SPY")]
        public string TargetSymbol { get; set; } = "SPY";

        [Parameter("Capital Initial FTMO ($)", Group = "2. Risk Management FTMO", DefaultValue = 100000.0)]
        public double InitialCapital { get; set; } = 100000.0;

        [Parameter("Risque Par Trade (%)", Group = "2. Risk Management FTMO", DefaultValue = 0.70)]
        public double RiskPerTradePct { get; set; } = 0.70;

        [Parameter("Max Positions Simultanées", Group = "2. Risk Management FTMO", DefaultValue = 2)]
        public int MaxConcurrentTrades { get; set; } = 2;

        [Parameter("Disjoncteur Jour FTMO (%)", Group = "2. Risk Management FTMO", DefaultValue = 4.0)]
        public double MaxDailyLossPct { get; set; } = 4.0;

        [Parameter("Seuil Réduction DD FTMO (%)", Group = "2. Risk Management FTMO", DefaultValue = 5.0)]
        public double MaxDrawdownPct { get; set; } = 5.0;

        [Parameter("Seuil MLOFI Score", Group = "1. Target Setup", DefaultValue = 0.20)]
        public double MlofiThreshold { get; set; } = 0.20;

        [Parameter("Seuil ADX Tendance", Group = "1. Target Setup", DefaultValue = 20.0)]
        public double AdxThreshold { get; set; } = 20.0;

        [Parameter("Multiplicateur StopLoss (x ATR)", Group = "3. Bracket Orders", DefaultValue = 1.0)]
        public double SlAtrMultiplier { get; set; } = 1.0;

        [Parameter("Multiplicateur TakeProfit (x ATR)", Group = "3. Bracket Orders", DefaultValue = 1.6)]
        public double TpAtrMultiplier { get; set; } = 1.6;

        [Parameter("Utiliser Ordres Limite", Group = "3. Bracket Orders", DefaultValue = true)]
        public bool UseLimitOrders { get; set; } = true;

        [Parameter("Slippage / Buffer Max (Pips)", Group = "3. Bracket Orders", DefaultValue = 0.5)]
        public double MaxSlippagePips { get; set; } = 0.5;

        [Parameter("Expiration Ordre Limite (Minutes)", Group = "3. Bracket Orders", DefaultValue = 1.0)]
        public double LimitOrderTimeoutMinutes { get; set; } = 1.0;

        [Parameter("Activer Apprentissage ML FastTree", Group = "4. Machine Learning", DefaultValue = true)]
        public bool EnableMlTraining { get; set; } = true;

        [Parameter("Nombre de Barres Entraînement", Group = "4. Machine Learning", DefaultValue = 15000)]
        public int TrainingHistoryBars { get; set; } = 15000;

        [Parameter("Alpaca Key ID (Optionnel)", Group = "5. Alpaca Historical Data", DefaultValue = "")]
        public string AlpacaKeyId { get; set; } = "";

        [Parameter("Alpaca Secret Key (Optionnel)", Group = "5. Alpaca Historical Data", DefaultValue = "")]
        public string AlpacaSecretKey { get; set; } = "";

        [Parameter("Alpaca Feed", Group = "5. Alpaca Historical Data", DefaultValue = "sip")]
        public string AlpacaFeed { get; set; } = "sip";

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
            if (!string.IsNullOrEmpty(AlpacaKeyId)) Print("📡 Données Alpaca API activées pour l'entraînement ML !");
            Print("==========================================================================");

            _dailyStartEquity = Account.Balance;
            _peakEquity = Account.Balance;
            _mlPredictor = new MlofiMlPredictorEngine();

            if (EnableMlTraining)
            {
                RunTrainingPhase();
            }
        }

        private List<SimpleBar> FetchBarsForTraining()
        {
            var result = new List<SimpleBar>();

            if (!string.IsNullOrEmpty(AlpacaKeyId) && !string.IsNullOrEmpty(AlpacaSecretKey))
            {
                try
                {
                    Print("📥 Téléchargement historique SPY 1m depuis Alpaca (pagination, ~1 an)...");
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", AlpacaKeyId.Trim());
                        client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", AlpacaSecretKey.Trim());
                        client.Timeout = TimeSpan.FromSeconds(120);

                        string startDate = DateTime.UtcNow.AddDays(-365).ToString("yyyy-MM-ddTHH:mm:ssZ");
                        string nextPageToken = null;
                        int maxPages = 10;
                        int page = 0;

                        while (page < maxPages)
                        {
                            string url = $"https://data.alpaca.markets/v2/stocks/bars?symbols=SPY&timeframe=1Min&limit=10000&start={startDate}&feed={AlpacaFeed}";
                            if (nextPageToken != null)
                                url += $"&page_token={Uri.EscapeDataString(nextPageToken)}";

                            var response = client.GetAsync(url).Result;
                            if (!response.IsSuccessStatusCode)
                            {
                                Print($"⚠️ Échec Alpaca page {page + 1} (Status: {response.StatusCode}).");
                                break;
                            }

                            string json = response.Content.ReadAsStringAsync().Result;
                            using (var doc = System.Text.Json.JsonDocument.Parse(json))
                            {
                                if (doc.RootElement.TryGetProperty("bars", out var barsElem) &&
                                    barsElem.TryGetProperty("SPY", out var spyBars))
                                {
                                    int countBefore = result.Count;
                                    foreach (var item in spyBars.EnumerateArray())
                                    {
                                        result.Add(new SimpleBar
                                        {
                                            Open   = item.GetProperty("o").GetDouble(),
                                            High   = item.GetProperty("h").GetDouble(),
                                            Low    = item.GetProperty("l").GetDouble(),
                                            Close  = item.GetProperty("c").GetDouble(),
                                            TickVolume = item.GetProperty("v").GetDouble()
                                        });
                                    }
                                    Print($"📄 Page {page + 1} : +{result.Count - countBefore} barres (Total: {result.Count})");

                                    nextPageToken = null;
                                    if (doc.RootElement.TryGetProperty("next_page_token", out var npt) &&
                                        npt.ValueKind != System.Text.Json.JsonValueKind.Null)
                                        nextPageToken = npt.GetString();
                                }
                                else break;
                            }

                            page++;
                            if (nextPageToken == null) break;
                        }

                        if (result.Count > 0)
                        {
                            Print($"✅ {result.Count} barres 1m SPY chargées depuis Alpaca API !");
                            return result;
                        }
                        Print("⚠️ Aucune barre Alpaca. Repli sur barres locales cTrader...");
                    }
                }
                catch (Exception ex)
                {
                    Print($"⚠️ Exception Alpaca API: {ex.Message}. Repli sur barres locales cTrader...");
                }
            }

            var localBars = MarketData.GetBars(TimeFrame.Minute);
            foreach (var b in localBars) result.Add(new SimpleBar { Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, TickVolume = b.TickVolume });
            return result;
        }

        private void RunTrainingPhase()
        {
            Print($"⚙️ PHASE 1 : Extraction des caractéristiques causales ({TrainingHistoryBars} barres)...");

            List<SimpleBar> barsList = FetchBarsForTraining();
            int totalBars = barsList.Count;

            if (totalBars < 100)
            {
                Print("⚠️ Données historiques insuffisantes. Phase d'entraînement ignorée.");
                return;
            }

            int countToUse = Math.Min(TrainingHistoryBars, totalBars - 20);
            int startIndex = Math.Max(0, totalBars - countToUse - 20);

            List<MlofiMlFeatureData> trainingSamples = new List<MlofiMlFeatureData>();

            for (int i = startIndex + 50; i < totalBars - 20; i++)
            {
                var bar = barsList[i];
                double closePrice = bar.Close;

                double range = Math.Max(bar.High - bar.Low, 0.01);
                double closePos = (closePrice - bar.Low) / range;
                double buyVol = bar.TickVolume * closePos;
                double sellVol = bar.TickVolume * (1.0 - closePos);
                double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = barsList[i - k].Close;
                    double v = barsList[i - k].TickVolume;
                    if (k < 20) { sum20 += c; sumVol += v; }
                    sum50 += c;
                }
                double ema20 = sum20 / 20.0;
                double ema50 = sum50 / 50.0;
                double avgVolume = sumVol / 20.0;

                double gains = 0, losses = 0;
                for (int k = 0; k < 14; k++)
                {
                    double diff = barsList[i - k].Close - barsList[i - k - 1].Close;
                    if (diff > 0) gains += diff; else losses -= diff;
                }
                double rs = losses > 0 ? gains / losses : 1.0;
                double rsi14 = 100.0 - (100.0 / (1.0 + rs));

                double trSum = 0;
                for (int k = 0; k < 14; k++)
                {
                    double tr1 = barsList[i - k].High - barsList[i - k].Low;
                    double tr2 = Math.Abs(barsList[i - k].High - barsList[i - k - 1].Close);
                    double tr3 = Math.Abs(barsList[i - k].Low - barsList[i - k - 1].Close);
                    trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                }
                double atr1m = trSum / 14.0;

                bool isVolumeSpike = bar.TickVolume >= avgVolume * 1.1;
                bool isBuy = closePrice > ema20 && mlofiScore >= MlofiThreshold && isVolumeSpike;
                bool isSell = closePrice < ema20 && mlofiScore <= -MlofiThreshold && isVolumeSpike;

                if (!isBuy && !isSell) continue;

                bool label = false;
                double tp = isBuy ? closePrice + (TpAtrMultiplier * atr1m) : closePrice - (TpAtrMultiplier * atr1m);
                double sl = isBuy ? closePrice - (SlAtrMultiplier * atr1m) : closePrice + (SlAtrMultiplier * atr1m);

                for (int k = 1; k <= 15 && i + k < totalBars; k++)
                {
                    var fBar = barsList[i + k];
                    if (isBuy) { if (fBar.High >= tp) { label = true; break; } if (fBar.Low <= sl) break; }
                    else { if (fBar.Low <= tp) { label = true; break; } if (fBar.High >= sl) break; }
                }

                trainingSamples.Add(MlofiMlFeatureExtractor.ExtractFeatures(mlofiScore, closePrice, ema20, 0, ema20, ema50, atr1m, bar.TickVolume, avgVolume, rsi14, label));
            }

            var res = _mlPredictor.TrainModel(trainingSamples, Print);
            Print($"Echantillons : {res.SampleCount} | Accuracy: {res.Accuracy * 100:F2}% | AUC: {res.Auc:F4} | Precision: {res.Precision * 100:F2}%");
        }

        private List<SimpleBar> FetchLiveSpyBars()
        {
            var result = new List<SimpleBar>();
            if (string.IsNullOrEmpty(AlpacaKeyId) || string.IsNullOrEmpty(AlpacaSecretKey)) return result;

            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", AlpacaKeyId.Trim());
                    client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", AlpacaSecretKey.Trim());
                    client.Timeout = TimeSpan.FromSeconds(5);

                    string startDate = DateTime.UtcNow.AddHours(-4).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    string url = $"https://data.alpaca.markets/v2/stocks/bars?symbols=SPY&timeframe=1Min&limit=55&start={startDate}&feed={AlpacaFeed}";
                    var response = client.GetAsync(url).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("bars", out var barsElem) &&
                                barsElem.TryGetProperty("SPY", out var spyBars))
                            {
                                foreach (var item in spyBars.EnumerateArray())
                                {
                                    result.Add(new SimpleBar
                                    {
                                        Open = item.GetProperty("o").GetDouble(),
                                        High = item.GetProperty("h").GetDouble(),
                                        Low = item.GetProperty("l").GetDouble(),
                                        Close = item.GetProperty("c").GetDouble(),
                                        TickVolume = item.GetProperty("v").GetDouble()
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return result;
        }

        protected override void OnBar()
        {
            if (Server.Time.Date != _currentDay)
            {
                _currentDay = Server.Time.Date;
                _dailyStartEquity = Account.Equity;
                Print($"☀️ Nouveau jour FTMO : Equity de Départ = ${_dailyStartEquity:N2}");
            }

            if (Account.Equity > _peakEquity) _peakEquity = Account.Equity;

            double currentDailyDrawdownPct = (_dailyStartEquity - Account.Equity) / _dailyStartEquity * 100.0;
            if (currentDailyDrawdownPct >= MaxDailyLossPct)
            {
                var activePositions = Positions.FindAll("MlofiFtmo");
                foreach (var pos in activePositions)
                {
                    ClosePosition(pos);
                    Print($"🚨 DISJONCTEUR FTMO ACTIVÉ : Position #{pos.Id} fermée d'urgence à -{currentDailyDrawdownPct:F2}%.");
                }
                Print($"⛔ DISJONCTEUR FTMO DÉCLENCHÉ : Perte Quotidienne = -{currentDailyDrawdownPct:F2}% (Limite = {MaxDailyLossPct}%). Vente totale & arrêt du trading pour la journée.");
                return;
            }

            TimeSpan timeOfDay = Server.Time.TimeOfDay;
            if (timeOfDay >= new TimeSpan(21, 45, 0) || timeOfDay < new TimeSpan(15, 45, 0))
            {
                var activePositions = Positions.FindAll("MlofiFtmo");
                foreach (var pos in activePositions)
                {
                    ClosePosition(pos);
                    Print($"🌙 CLÔTURE HORS-SESSION US : Position #{pos.Id} fermée à {timeOfDay:hh\\:mm} pour éviter les spreads larges.");
                }

                var pendingOrders = PendingOrders.Where(o => o.Label == "MlofiFtmo").ToArray();
                foreach (var order in pendingOrders)
                {
                    CancelPendingOrder(order);
                }

                if (timeOfDay >= new TimeSpan(21, 45, 0) || timeOfDay < new TimeSpan(15, 45, 0)) return;
            }

            ManageBreakEven();

            int activeTotalCount = Positions.FindAll("MlofiFtmo").Length + PendingOrders.Where(o => o.Label == "MlofiFtmo").Count();
            if (activeTotalCount >= MaxConcurrentTrades) return;

            // Récupérer les données SPY réelles en direct via Alpaca si configuré
            List<SimpleBar> liveBars = FetchLiveSpyBars();
            bool usingAlpacaLive = liveBars.Count >= 50;

            double currentPrice;
            double mlofiScore;
            double ema20, ema50, avgVolume, rsi14, atr1m, currentBarVolume;
            bool isVolumeSpike;

            if (usingAlpacaLive)
            {
                int idx = liveBars.Count >= 2 ? liveBars.Count - 2 : liveBars.Count - 1;
                var bar = liveBars[idx];
                currentPrice = bar.Close;
                currentBarVolume = bar.TickVolume;

                double range = Math.Max(bar.High - bar.Low, 0.01);
                double closePos = (currentPrice - bar.Low) / range;
                double buyVol = bar.TickVolume * closePos;
                double sellVol = bar.TickVolume * (1.0 - closePos);
                mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = liveBars[idx - k].Close;
                    double v = liveBars[idx - k].TickVolume;
                    if (k < 20) { sum20 += c; sumVol += v; }
                    sum50 += c;
                }
                ema20 = sum20 / 20.0;
                ema50 = sum50 / 50.0;
                avgVolume = sumVol / 20.0;

                double gains = 0, losses = 0;
                for (int k = 0; k < 14; k++)
                {
                    double diff = liveBars[idx - k].Close - liveBars[idx - k - 1].Close;
                    if (diff > 0) gains += diff; else losses -= diff;
                }
                double rs = losses > 0 ? gains / losses : 1.0;
                rsi14 = 100.0 - (100.0 / (1.0 + rs));

                double trSum = 0;
                for (int k = 0; k < 14; k++)
                {
                    double tr1 = liveBars[idx - k].High - liveBars[idx - k].Low;
                    double tr2 = Math.Abs(liveBars[idx - k].High - liveBars[idx - k - 1].Close);
                    double tr3 = Math.Abs(liveBars[idx - k].Low - liveBars[idx - k - 1].Close);
                    trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                }
                atr1m = trSum / 14.0;
                isVolumeSpike = bar.TickVolume >= avgVolume * 1.1;
            }
            else
            {
                var bars1m = MarketData.GetBars(TimeFrame.Minute);
                int idx = bars1m.Count - 1;
                if (idx < 50) return;

                var bar = bars1m[idx];
                currentPrice = bar.Close;
                currentBarVolume = bar.TickVolume;

                double range = Math.Max(bar.High - bar.Low, 0.01);
                double closePos = (currentPrice - bar.Low) / range;
                double buyVol = bar.TickVolume * closePos;
                double sellVol = bar.TickVolume * (1.0 - closePos);
                mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = bars1m[idx - k].Close;
                    double v = bars1m[idx - k].TickVolume;
                    if (k < 20) { sum20 += c; sumVol += v; }
                    sum50 += c;
                }
                ema20 = sum20 / 20.0;
                ema50 = sum50 / 50.0;
                avgVolume = sumVol / 20.0;

                double gains = 0, losses = 0;
                for (int k = 0; k < 14; k++)
                {
                    double diff = bars1m[idx - k].Close - bars1m[idx - k - 1].Close;
                    if (diff > 0) gains += diff; else losses -= diff;
                }
                double rs = losses > 0 ? gains / losses : 1.0;
                rsi14 = 100.0 - (100.0 / (1.0 + rs));

                double trSum = 0;
                for (int k = 0; k < 14; k++)
                {
                    double tr1 = bars1m[idx - k].High - bars1m[idx - k].Low;
                    double tr2 = Math.Abs(bars1m[idx - k].High - bars1m[idx - k - 1].Close);
                    double tr3 = Math.Abs(bars1m[idx - k].Low - bars1m[idx - k - 1].Close);
                    trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                }
                atr1m = trSum / 14.0;
                isVolumeSpike = bar.TickVolume >= avgVolume * 1.1;
            }

            bool isBuySetup = currentPrice > ema20 && mlofiScore >= MlofiThreshold && isVolumeSpike;
            bool isSellSetup = currentPrice < ema20 && mlofiScore <= -MlofiThreshold && isVolumeSpike;

            double volRatio = avgVolume > 0 ? (currentBarVolume / avgVolume) : 1.0;

            string blockReason = "";
            if (!isBuySetup && !isSellSetup)
            {
                if (mlofiScore >= MlofiThreshold && currentPrice <= ema20) blockReason = $"Prix < EMA20 (${currentPrice:F2} < ${ema20:F2})";
                else if (mlofiScore <= -MlofiThreshold && currentPrice >= ema20) blockReason = $"Prix > EMA20 (${currentPrice:F2} > ${ema20:F2})";
                else if (Math.Abs(mlofiScore) >= MlofiThreshold && !isVolumeSpike) blockReason = $"Vol Faible ({volRatio:F1}x < 1.1x)";
                else blockReason = $"MLOFI Neutre ({mlofiScore:+0.00;-0.00})";
            }

            Print($"🔍 [Analyse {Server.Time:HH:mm:ss}] SPY: ${currentPrice:F2} (EMA20: ${ema20:F2}) | MLOFI: {mlofiScore:+0.00;-0.00} | Vol: {volRatio:F1}x | Status: {(isBuySetup ? "BUY SETUP 🟢" : isSellSetup ? "SELL SETUP 🔴" : $"EN ATTENTE ⏳ [{blockReason}]")}");

            if (!isBuySetup && !isSellSetup) return;

            if (_mlPredictor != null && _mlPredictor.IsTrained)
            {
                var featureSample = MlofiMlFeatureExtractor.ExtractFeatures(
                    mlofiScore, currentPrice, ema20, 0, ema20, ema50, atr1m, 0, avgVolume, rsi14, false);

                var prediction = _mlPredictor.Predict(featureSample);

                Print($"🧠 [ML FastTree] Évaluation du Signal : Probabilité = {prediction.Probability * 100:F1}% (Seuil Requis = 20.0%) -> {(prediction.Probability >= 0.20f ? "VALIDÉ ✅" : "REJETÉ ❌")}");

                if (prediction.Probability < 0.20f)
                {
                    return;
                }
            }

            double currentOverallDrawdownPct = (_peakEquity - Account.Equity) / _peakEquity * 100.0;
            double effectiveRiskPct = (currentOverallDrawdownPct >= MaxDrawdownPct) ? RiskPerTradePct * 0.5 : RiskPerTradePct;
            double riskBudgetDollars = Account.Equity * (effectiveRiskPct / 100.0);

            // Ratio d'échelle si l'analyse est faite sur SPY et l'exécution sur le symbole local cTrader (ex: US500.cash)
            double localPrice = Symbol.Ask;
            double ratio = (usingAlpacaLive && currentPrice > 0) ? (localPrice / currentPrice) : 1.0;

            double slDistance = atr1m * SlAtrMultiplier * ratio;
            double tpDistance = atr1m * TpAtrMultiplier * ratio;

            if (slDistance <= (0.05 * ratio)) slDistance = 0.50 * ratio;
            if (tpDistance <= (0.05 * ratio)) tpDistance = 1.00 * ratio;

            double volumeLots = Symbol.VolumeInUnitsToQuantity(riskBudgetDollars / slDistance);
            volumeLots = Symbol.NormalizeVolumeInUnits(volumeLots, RoundingMode.ToNearest);

            if (volumeLots <= 0) return;

            TradeType tradeType = isBuySetup ? TradeType.Buy : TradeType.Sell;
            double slPips = slDistance / Symbol.PipSize;
            double tpPips = tpDistance / Symbol.PipSize;

            Print($"🎯 EXECUTION SIGNAL MLOFI FTMO ({(usingAlpacaLive ? "Alpaca SPY Live" : "Local")}) : {tradeType} | Lots: {volumeLots} | SL: {slPips:F1} pips | TP: {tpPips:F1} pips");

            if (UseLimitOrders)
            {
                double targetPrice = isBuySetup ? (Symbol.Ask + (MaxSlippagePips * Symbol.PipSize)) : (Symbol.Bid - (MaxSlippagePips * Symbol.PipSize));
                DateTime expirationTime = Server.Time.AddMinutes(LimitOrderTimeoutMinutes);
                Print($"📌 PLACEMENT ORDRE LIMITE : {tradeType} @ {targetPrice:F2} (Slippage Buffer = {MaxSlippagePips} pips, Expiration = {expirationTime:HH\\:mm\\:ss})");
                PlaceLimitOrder(tradeType, Symbol.Name, volumeLots, targetPrice, "MlofiFtmo", slPips, tpPips, expirationTime);
            }
            else
            {
                ExecuteMarketOrder(tradeType, Symbol.Name, volumeLots, "MlofiFtmo", slPips, tpPips);
            }
        }

        private void ManageBreakEven()
        {
            var openPositions = Positions.FindAll("MlofiFtmo");
            foreach (var pos in openPositions)
            {
                double currentProfitPips = pos.Pips;
                double tpDistancePips = Math.Abs(pos.TakeProfit.GetValueOrDefault() - pos.EntryPrice) / Symbol.PipSize;

                if (currentProfitPips >= tpDistancePips * 0.75)
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
