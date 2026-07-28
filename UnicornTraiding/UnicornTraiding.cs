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
        public float MacdHist { get; set; }
        public float BollingerWidth { get; set; }
        public float MicroSlope5 { get; set; }
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
            double macdHist = 0.0,
            double bbWidth = 0.01,
            double microSlope5 = 0.0,
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
                MacdHist = (float)macdHist,
                BollingerWidth = (float)bbWidth,
                MicroSlope5 = (float)microSlope5,
                Label = label
            };
        }
    }

    public class MlofiMlPredictorEngine
    {
        private readonly MLContext _mlContext;
        private ITransformer? _modelFastTree;
        private ITransformer? _modelFastForest;
        private PredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>? _predEngineFastTree;
        private PredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>? _predEngineFastForest;

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

            var prepPipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(MlofiMlFeatureData.MlofiScore),
                nameof(MlofiMlFeatureData.VwapDistancePercent),
                nameof(MlofiMlFeatureData.VwapSlope),
                nameof(MlofiMlFeatureData.EmaRatio),
                nameof(MlofiMlFeatureData.NormalizedAtr),
                nameof(MlofiMlFeatureData.VolumeRatio),
                nameof(MlofiMlFeatureData.Rsi14),
                nameof(MlofiMlFeatureData.MacdHist),
                nameof(MlofiMlFeatureData.BollingerWidth),
                nameof(MlofiMlFeatureData.MicroSlope5));

            var pipelineFastTree = prepPipeline.Append(_mlContext.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    NumberOfLeaves = 20,
                    NumberOfTrees = 150,
                    MinimumExampleCountPerLeaf = 10,
                    LearningRate = 0.03,
                    LabelColumnName = nameof(MlofiMlFeatureData.Label),
                    FeatureColumnName = "Features"
                }));

            var pipelineFastForest = prepPipeline.Append(_mlContext.BinaryClassification.Trainers.FastForest(
                new FastForestBinaryTrainer.Options
                {
                    NumberOfLeaves = 20,
                    NumberOfTrees = 150,
                    MinimumExampleCountPerLeaf = 10,
                    LabelColumnName = nameof(MlofiMlFeatureData.Label),
                    FeatureColumnName = "Features"
                }));

            log($"🧠 Entraînement de l'IA Hybride Ensemble (FastTree GBDT + FastForest RF) sur {trainSamples.Count} échantillons...");
            _modelFastTree = pipelineFastTree.Fit(trainDataView);
            _modelFastForest = pipelineFastForest.Fit(trainDataView);

            var testPredsView = _modelFastTree.Transform(testDataView);
            var metrics = _mlContext.BinaryClassification.Evaluate(testPredsView, labelColumnName: nameof(MlofiMlFeatureData.Label));

            _predEngineFastTree = _mlContext.Model.CreatePredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>(_modelFastTree);
            _predEngineFastForest = _mlContext.Model.CreatePredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>(_modelFastForest);
            IsTrained = true;

            return (metrics.Accuracy, metrics.AreaUnderRocCurve, metrics.PositivePrecision, trainingData.Count);
        }

        public MlofiMlPredictionData Predict(MlofiMlFeatureData sample)
        {
            if (!IsTrained || _predEngineFastTree == null || _predEngineFastForest == null)
            {
                return new MlofiMlPredictionData { PredictedLabel = true, Probability = 0.5f, Score = 0f };
            }

            var predTree = _predEngineFastTree.Predict(sample);
            var predForest = _predEngineFastForest.Predict(sample);

            // Vote pondéré Soft-Voting (60% FastTree GBDT + 40% FastForest Random Forest)
            float combinedProb = (0.60f * predTree.Probability) + (0.40f * predForest.Probability);

            return new MlofiMlPredictionData
            {
                PredictedLabel = combinedProb >= 0.35f,
                Probability = combinedProb,
                Score = (predTree.Score + predForest.Score) / 2.0f
            };
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

        [Parameter("Risque Par Trade (%)", Group = "2. Risk Management FTMO", DefaultValue = 0.95)]
        public double RiskPerTradePct { get; set; } = 0.95;

        [Parameter("Max Positions Simultanées", Group = "2. Risk Management FTMO", DefaultValue = 3)]
        public int MaxConcurrentTrades { get; set; } = 3;

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

        [Parameter("Multiplicateur TakeProfit (x ATR)", Group = "3. Bracket Orders", DefaultValue = 2.0)]
        public double TpAtrMultiplier { get; set; } = 2.0;

        [Parameter("Utiliser Ordres Limite", Group = "3. Bracket Orders", DefaultValue = false)]
        public bool UseLimitOrders { get; set; } = false;

        [Parameter("Slippage / Buffer Max (Pips)", Group = "3. Bracket Orders", DefaultValue = 0.5)]
        public double MaxSlippagePips { get; set; } = 0.5;

        [Parameter("Expiration Ordre Limite (Minutes)", Group = "3. Bracket Orders", DefaultValue = 1.0)]
        public double LimitOrderTimeoutMinutes { get; set; } = 1.0;

        [Parameter("Activer Apprentissage ML FastTree", Group = "4. Machine Learning", DefaultValue = true)]
        public bool EnableMlTraining { get; set; } = true;

        [Parameter("Nombre de Barres Entraînement", Group = "4. Machine Learning", DefaultValue = 60000)]
        public int TrainingHistoryBars { get; set; } = 60000;

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
        private DateTime _lastTrainingDate = DateTime.MinValue;
        private DateTime _lastReplayDate = DateTime.MinValue;
        private double _peakEquity;

        protected override void OnStart()
        {
            Print("==========================================================================");
            Print($"🚀 DEMARRAGE BOT FTMO UNICORN SCALPER ({Symbol.Name})");
            Print($"Symbol: {Symbol.Name} | Capital: ${InitialCapital:N0} | Risk/Trade: {RiskPerTradePct}% | Daily Breaker: {MaxDailyLossPct}%");
            if (!string.IsNullOrEmpty(AlpacaKeyId)) Print("📡 Données Alpaca API activées pour l'entraînement ML !");
            Print("==========================================================================");

            _dailyStartEquity = Account.Balance;
            _peakEquity = Account.Balance;
            _mlPredictor = new MlofiMlPredictorEngine();

            if (EnableMlTraining)
            {
                _lastTrainingDate = Server.Time.Date;
                RunTrainingPhase();
            }

            // Vérification native cTrader si le marché est fermé lors du lancement
            bool isClosedOnStart = !Symbol.MarketHours.IsOpened() || Symbol.MarketHours.TimeTillClose() <= TimeSpan.FromMinutes(5);

            if (isClosedOnStart && _lastReplayDate.Date != Server.Time.Date)
            {
                _lastReplayDate = Server.Time.Date;
                Print($"🌙 [LANCEMENT HORS-SESSION cTrader] Le marché {Symbol.Name} est fermé (IsOpened: {Symbol.MarketHours.IsOpened()}). Exécution immédiate du Replay Backtest...");
                RunEndOfDayReplayBacktest();
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
            Print($"📥 Chargement de l'historique M1 FTMO ({TrainingHistoryBars} barres requises)...");

            if (TimeFrame != TimeFrame.Minute)
            {
                Print($"⚠️ ATTENTION : Votre graphique cTrader est réglé sur {TimeFrame}. Pour permettre à cTrader de charger l'historique M1 complet (15 000 barres), réglez la période de votre graphique cTrader sur M1 !");
            }

            for (int i = 0; i < 120 && localBars.Count < TrainingHistoryBars; i++)
            {
                int loaded = localBars.LoadMoreHistory();
                if (loaded == 0) break;
            }

            Print($"✅ {localBars.Count} barres M1 chargées depuis le serveur cTrader FTMO !");

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

                trainingSamples.Add(MlofiMlFeatureExtractor.ExtractFeatures(mlofiScore, closePrice, ema20, 0, ema20, ema50, atr1m, bar.TickVolume, avgVolume, rsi14, label: label));
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

                double todayRealizedPnL = 0;
                foreach (var trade in History)
                {
                    if (trade.ClosingTime >= _currentDay)
                    {
                        todayRealizedPnL += trade.NetProfit;
                    }
                }

                _dailyStartEquity = Account.Balance - todayRealizedPnL;
                Print($"☀️ Nouveau jour FTMO : Solde de Départ 00:00 UTC = ${_dailyStartEquity:N2} | PnL Déjà Réalisé Aujourd'hui = ${todayRealizedPnL:+$#,##0.00;-$#,##0.00}");
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
                Print($"⛔ DISJONCTEUR FTMO DÉCLENCHÉ : Perte Quotidienne Cumulée = -{currentDailyDrawdownPct:F2}% (Limite = {MaxDailyLossPct}%). Vente totale & arrêt du trading pour la journée.");
                return;
            }

            // Vérification native cTrader des horaires de session du symbole
            bool isSessionClosed = !Symbol.MarketHours.IsOpened() || Symbol.MarketHours.TimeTillClose() <= TimeSpan.FromMinutes(5);

            // Clôture automatique hors-session pour éliminer les spreads larges
            if (isSessionClosed)
            {
                var activePositions = Positions.FindAll("MlofiFtmo");
                foreach (var pos in activePositions)
                {
                    ClosePosition(pos);
                    Print($"🌙 CLÔTURE HORS-SESSION : Position #{pos.Id} fermée automatiquement.");
                }

                var pendingOrders = PendingOrders.Where(o => o.Label == "MlofiFtmo").ToArray();
                foreach (var order in pendingOrders)
                {
                    CancelPendingOrder(order);
                }

                if (_lastReplayDate.Date != Server.Time.Date)
                {
                    _lastReplayDate = Server.Time.Date;
                    RunEndOfDayReplayBacktest();
                }

                return;
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
                int idx = bars1m.Count >= 2 ? bars1m.Count - 2 : bars1m.Count - 1;
                if (idx < 50) return;

                var bar = bars1m[idx];
                currentPrice = Symbol.Ask;
                currentBarVolume = bar.TickVolume > 0 ? bar.TickVolume : 1.0;

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
                    mlofiScore, currentPrice, ema20, 0, ema20, ema50, atr1m, 0, avgVolume, rsi14, label: false);

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
                // Buy Limit à Ask - Buffer, Sell Limit à Ask + Buffer pour être au-dessus du marché
                double targetPrice = isBuySetup ? (Symbol.Ask - (MaxSlippagePips * Symbol.PipSize)) : (Symbol.Ask + (MaxSlippagePips * Symbol.PipSize));
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

                // Déplacement du Stop Loss à Break-Even dès 50% du TP atteint (sécurise les trades très tôt)
                if (currentProfitPips >= tpDistancePips * 0.50)
                {
                    if (pos.TradeType == TradeType.Buy && (pos.StopLoss == null || pos.StopLoss < pos.EntryPrice))
                    {
                        ModifyPosition(pos, pos.EntryPrice, pos.TakeProfit);
                        Print($"🛡️ BREAK-EVEN APPLIQUÉ SUR POSITION BUY #{pos.Id} (@ {pos.EntryPrice})");
                    }
                    else if (pos.TradeType == TradeType.Sell && (pos.StopLoss == null || pos.StopLoss > pos.EntryPrice))
                    {
                        ModifyPosition(pos, pos.EntryPrice, pos.TakeProfit);
                        Print($"🛡️ BREAK-EVEN APPLIQUÉ SUR POSITION SELL #{pos.Id} (@ {pos.EntryPrice})");
                    }
                }
            }
        }

        private AlpacaClock FetchAlpacaClock()
        {
            if (string.IsNullOrEmpty(AlpacaKeyId) || string.IsNullOrEmpty(AlpacaSecretKey)) return null;
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", AlpacaKeyId.Trim());
                    client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", AlpacaSecretKey.Trim());
                    client.Timeout = TimeSpan.FromSeconds(5);

                    string url = "https://api.alpaca.markets/v2/clock";
                    var response = client.GetAsync(url).Result;
                    if (response.IsSuccessStatusCode)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            var root = doc.RootElement;
                            return new AlpacaClock
                            {
                                IsOpen = root.GetProperty("is_open").GetBoolean(),
                                NextOpen = root.GetProperty("next_open").GetString() ?? "",
                                NextClose = root.GetProperty("next_close").GetString() ?? "",
                                Timestamp = root.GetProperty("timestamp").GetString() ?? ""
                            };
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void RunEndOfDayReplayBacktest()
        {
            Print($"==========================================================================");
            Print($"📊 [BACKTEST DYNAMIQUE REPLAY DU JOUR - {Server.Time:yyyy-MM-dd}]");
            Print($"==========================================================================");

            var todaysBars = Bars.Where(b => b.OpenTime.Date == Server.Time.Date).ToList();
            if (todaysBars.Count < 10)
            {
                Print("⚠️ Pas assez de barres sur la journée courante pour la simulation.");
                Print($"==========================================================================");
                return;
            }

            double simCapital = InitialCapital;
            int totalTrades = 0;
            int winTrades = 0;
            int lossTrades = 0;
            double simPeakCapital = simCapital;
            double maxDrawdownPct = 0;

            bool inPos = false;
            string posSide = "";
            double entryPrice = 0;
            double slPrice = 0;
            double tpPrice = 0;
            bool isBreakEven = false;
            double posQty = 0;

            for (int i = 14; i < todaysBars.Count; i++)
            {
                var bar = todaysBars[i];
                double closePrice = bar.Close;

                if (inPos)
                {
                    double tpDist = Math.Abs(tpPrice - entryPrice);
                    double currentProfit = posSide == "buy" ? (closePrice - entryPrice) : (entryPrice - closePrice);

                    if (!isBreakEven && currentProfit >= tpDist * 0.50)
                    {
                        isBreakEven = true;
                        slPrice = entryPrice;
                    }

                    bool hitTP = posSide == "buy" ? (bar.High >= tpPrice) : (bar.Low <= tpPrice);
                    bool hitSL = posSide == "buy" ? (bar.Low <= slPrice) : (bar.High >= slPrice);

                    if (hitTP || hitSL)
                    {
                        double pnl = posSide == "buy" ? (tpPrice - entryPrice) * posQty : (entryPrice - slPrice) * posQty;
                        if (hitSL) pnl = isBreakEven ? 0 : (posSide == "buy" ? (slPrice - entryPrice) * posQty : (entryPrice - slPrice) * posQty);

                        simCapital += pnl;
                        totalTrades++;
                        if (pnl > 0) winTrades++; else if (pnl < 0) lossTrades++;

                        if (simCapital > simPeakCapital) simPeakCapital = simCapital;
                        double dd = (simPeakCapital - simCapital) / simPeakCapital * 100.0;
                        if (dd > maxDrawdownPct) maxDrawdownPct = dd;

                        inPos = false;
                    }
                }

                if (!inPos)
                {
                    double range = Math.Max(bar.High - bar.Low, 0.01);
                    double closePos = (closePrice - bar.Low) / range;
                    double buyVol = bar.TickVolume * closePos;
                    double sellVol = bar.TickVolume * (1.0 - closePos);
                    double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                    double sum20 = 0, sumVol = 0;
                    for (int k = 0; k < Math.Min(20, i); k++) { sum20 += todaysBars[i - k].Close; sumVol += todaysBars[i - k].TickVolume; }
                    double ema20 = sum20 / Math.Min(20, i);
                    double avgVolume = sumVol / Math.Min(20, i);

                    bool isVolumeSpike = bar.TickVolume >= avgVolume * 1.1;
                    bool isBuySetup = closePrice > ema20 && mlofiScore >= MlofiThreshold && isVolumeSpike;
                    bool isSellSetup = closePrice < ema20 && mlofiScore <= -MlofiThreshold && isVolumeSpike;

                    if (isBuySetup || isSellSetup)
                    {
                        double trSum = 0;
                        int atrPeriod = Math.Min(14, i);
                        for (int k = 0; k < atrPeriod; k++)
                        {
                            double tr1 = todaysBars[i - k].High - todaysBars[i - k].Low;
                            double tr2 = Math.Abs(todaysBars[i - k].High - todaysBars[i - k - 1].Close);
                            double tr3 = Math.Abs(todaysBars[i - k].Low - todaysBars[i - k - 1].Close);
                            trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                        }
                        double atr1m = trSum / atrPeriod;
                        double slDist = Math.Max(atr1m * SlAtrMultiplier, 0.50);
                        double tpDist = Math.Max(atr1m * TpAtrMultiplier, 1.00);

                        double riskDollars = simCapital * (RiskPerTradePct / 100.0);
                        posQty = Math.Max(1.0, Math.Floor(riskDollars / slDist));

                        inPos = true;
                        posSide = isBuySetup ? "buy" : "sell";
                        entryPrice = closePrice;
                        slPrice = isBuySetup ? closePrice - slDist : closePrice + slDist;
                        tpPrice = isBuySetup ? closePrice + tpDist : closePrice - tpDist;
                        isBreakEven = false;
                    }
                }
            }

            double winRate = totalTrades > 0 ? ((double)winTrades / totalTrades * 100.0) : 0.0;
            double netPnL = simCapital - InitialCapital;

            Print($"Barres M1 Analysées      : {todaysBars.Count} Barres");
            Print($"Trades Simulés du Jour  : {totalTrades} (Gagnants: {winTrades}, Perdants: {lossTrades})");
            Print($"Win Rate Simulée Journée : {winRate:F1} %");
            Print($"PnL Réalisé (Simulation) : ${netPnL:+$#,##0.00;-$#,##0.00} ({(netPnL / InitialCapital * 100.0):F2} %)");
            Print($"Max Daily Drawdown       : -{maxDrawdownPct:F2} %");
            Print($"Conformité FTMO          : {(maxDrawdownPct < MaxDailyLossPct ? "VALIDÉ ✅ (Respect des limites)" : "ATTENTION 🚨 (Seuil dépassé)")}");
            Print($"==========================================================================");
        }
    }

    public class AlpacaClock
    {
        public bool IsOpen { get; set; }
        public string NextOpen { get; set; } = "";
        public string NextClose { get; set; } = "";
        public string Timestamp { get; set; } = "";
    }

    public static class HelperMarket
    {
        public static int NextMarkClose(AlpacaClock clock)
        {
            if (clock == null || string.IsNullOrEmpty(clock.NextClose)) return 9999;
            DateTime dateTime = DateTime.Parse(clock.NextClose, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            DateTime now = DateTime.UtcNow;
            return (int)Math.Abs((dateTime - now).TotalMinutes);
        }

        public static float NextMarkOpen(AlpacaClock clock)
        {
            if (clock == null || string.IsNullOrEmpty(clock.NextOpen)) return 9999;
            DateTime dateTime = DateTime.Parse(clock.NextOpen, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            DateTime now = DateTime.UtcNow;
            return (float)Math.Abs((dateTime - now).TotalMinutes);
        }

        public static int NextTargetValueInSeconds(AlpacaClock clock, int seconds)
        {
            if (clock == null || string.IsNullOrEmpty(clock.Timestamp) || string.IsNullOrEmpty(clock.NextOpen)) return seconds;
            System.DateTimeOffset dto = System.DateTimeOffset.Parse(clock.Timestamp, System.Globalization.CultureInfo.InvariantCulture);
            DateTime currentDate = dto.UtcDateTime;
            DateTime dateTime = DateTime.Parse(clock.NextOpen, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
            TimeSpan openSpan = new TimeSpan(0, dateTime.Hour, dateTime.Minute, dateTime.Second);

            int minutesPassed = (int)(currentDate.TimeOfDay - openSpan).TotalSeconds;
            int halfHoursPassed = minutesPassed / seconds;
            int minutesSincePrevious = minutesPassed - halfHoursPassed * seconds;
            return seconds - minutesSincePrevious;
        }

        public static int NextTargetValueInSeconds(AlpacaClock clock)
        {
            if (clock == null || string.IsNullOrEmpty(clock.Timestamp)) return 60;
            System.DateTimeOffset dto = System.DateTimeOffset.Parse(clock.Timestamp, System.Globalization.CultureInfo.InvariantCulture);
            int secondsRemaining = 60 - dto.Second;
            return secondsRemaining == 0 ? 60 : secondsRemaining;
        }

        public static (DateTime PreMarketStart, DateTime PreMarketEnd, DateTime PostMarketStart, DateTime PostMarketEnd) GetExtraMarketHours(DateTime nextOpenTime, DateTime nextCloseTime)
        {
            DateTime preMarketStart = nextOpenTime.AddHours(-4);
            DateTime preMarketEnd = nextOpenTime;
            DateTime postMarketStart = nextCloseTime;
            DateTime postMarketEnd = nextCloseTime.AddHours(4);
            return (preMarketStart, preMarketEnd, postMarketStart, postMarketEnd);
        }
    }
}
