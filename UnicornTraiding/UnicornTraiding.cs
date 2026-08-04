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
        public DateTime Timestamp { get; set; }
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
                nameof(MlofiMlFeatureData.Rsi14));

            var pipelineFastTree = prepPipeline.Append(_mlContext.BinaryClassification.Trainers.FastTree(
                new FastTreeBinaryTrainer.Options
                {
                    NumberOfLeaves = 15,
                    NumberOfTrees = 100,
                    MinimumExampleCountPerLeaf = 10,
                    LearningRate = 0.05,
                    LabelColumnName = nameof(MlofiMlFeatureData.Label),
                    FeatureColumnName = "Features"
                }));

            log($"🧠 Entraînement de l'IA FastTree GBDT sur {trainSamples.Count} échantillons...");
            _modelFastTree = pipelineFastTree.Fit(trainDataView);

            var testPredsView = _modelFastTree.Transform(testDataView);
            var metrics = _mlContext.BinaryClassification.Evaluate(testPredsView, labelColumnName: nameof(MlofiMlFeatureData.Label));

            _predEngineFastTree = _mlContext.Model.CreatePredictionEngine<MlofiMlFeatureData, MlofiMlPredictionData>(_modelFastTree);
            
            IsTrained = true;
            log($"🧠 IA Entraînée & Activée : {trainSamples.Count} échantillons | Accuracy: {metrics.Accuracy * 100:F1}% | AUC: {metrics.AreaUnderRocCurve:F4}");

            return (metrics.Accuracy, metrics.AreaUnderRocCurve, metrics.PositivePrecision, trainingData.Count);
        }

        public MlofiMlPredictionData Predict(MlofiMlFeatureData sample)
        {
            if (!IsTrained || _predEngineFastTree == null)
            {
                return new MlofiMlPredictionData { PredictedLabel = false, Probability = 0.0f, Score = 0f };
            }

            var predTree = _predEngineFastTree.Predict(sample);

            return new MlofiMlPredictionData
            {
                PredictedLabel = predTree.PredictedLabel,
                Probability = predTree.Probability,
                Score = predTree.Score
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
        [Parameter("Symbol Target (ex: AMZN)", Group = "1. Target Setup", DefaultValue = "AMZN")]
        public string TargetSymbol { get; set; } = "AMZN";

        [Parameter("Capital Initial FTMO ($)", Group = "2. Risk Management FTMO", DefaultValue = 100000.0)]
        public double InitialCapital { get; set; } = 100000.0;

        [Parameter("Risque Par Trade (%)", Group = "2. Risk Management FTMO", DefaultValue = 0.10)]
        public double RiskPerTradePct { get; set; } = 0.10;

        [Parameter("Max Positions Simultanées", Group = "2. Risk Management FTMO", DefaultValue = 10)]
        public int MaxConcurrentTrades { get; set; } = 10;

        [Parameter("Levier Notionnel Max (x Equity)", Group = "2. Risk Management FTMO", DefaultValue = 1.0)]
        public double MaxLeverage { get; set; } = 1.0;

        [Parameter("Cutoff Entrée UTC (heures)", Group = "2. Risk Management FTMO", DefaultValue = 18.5)]
        public double EntryCutoffUtcHours { get; set; } = 18.5;

        [Parameter("Clôture EOD UTC (heures)", Group = "2. Risk Management FTMO", DefaultValue = 21.0)]
        public double EodCloseUtcHours { get; set; } = 21.0;

        [Parameter("Seuil Break-Even (fraction du TP)", Group = "3. Bracket Orders", DefaultValue = 0.80)]
        public double BreakEvenTriggerPct { get; set; } = 0.80;

        [Parameter("Disjoncteur Jour FTMO (%)", Group = "2. Risk Management FTMO", DefaultValue = 5.0)]
        public double MaxDailyLossPct { get; set; } = 5.0;

        [Parameter("Seuil Réduction DD FTMO (%)", Group = "2. Risk Management FTMO", DefaultValue = 10.0)]
        public double MaxDrawdownPct { get; set; } = 10.0;

        [Parameter("Seuil MLOFI Score", Group = "1. Target Setup", DefaultValue = 0.20)]
        public double MlofiThreshold { get; set; } = 0.20;

        [Parameter("Seuil ADX Tendance", Group = "1. Target Setup", DefaultValue = 20.0)]
        public double AdxThreshold { get; set; } = 20.0;

        [Parameter("Multiplicateur StopLoss (x ATR)", Group = "3. Bracket Orders", DefaultValue = 0.50)]
        public double SlAtrMultiplier { get; set; } = 0.50;

        [Parameter("Multiplicateur TakeProfit (x ATR)", Group = "3. Bracket Orders", DefaultValue = 1.50)]
        public double TpAtrMultiplier { get; set; } = 1.50;

        [Parameter("Utiliser Ordres Limite", Group = "3. Bracket Orders", DefaultValue = false)]
        public bool UseLimitOrders { get; set; } = false;

        [Parameter("Slippage / Buffer Max (Pips)", Group = "3. Bracket Orders", DefaultValue = 0.5)]
        public double MaxSlippagePips { get; set; } = 0.5;

        [Parameter("Expiration Ordre Limite (Minutes)", Group = "3. Bracket Orders", DefaultValue = 1.0)]
        public double LimitOrderTimeoutMinutes { get; set; } = 1.0;

        [Parameter("Activer Apprentissage ML FastTree", Group = "4. Machine Learning", DefaultValue = false)]
        public bool EnableMlTraining { get; set; } = false;

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
        private List<SimpleBar> _trainingBars = new List<SimpleBar>();
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

            // Exécution systématique du Replay Backtest du jour + Backtest 1 An lors du lancement du bot
            _lastReplayDate = Server.Time.Date;
            Print("📊 [DEMARRAGE BOT] Exécution du Replay Backtest de la journée avec le modèle IA ré-entraîné...");
            RunEndOfDayReplayBacktest();

            Print("📊 [DEMARRAGE BOT] Exécution du Backtest 1 An Historique (60 000 barres M1)...");
            RunOneYearWalkForwardBacktest();
        }

        private List<SimpleBar> FetchBarsForTraining()
        {
            var result = new List<SimpleBar>();

            if (!string.IsNullOrEmpty(AlpacaKeyId) && !string.IsNullOrEmpty(AlpacaSecretKey))
            {
                try
                {
                    string alpacaSymbol = string.IsNullOrEmpty(TargetSymbol) ? "NVDA" : TargetSymbol.Trim().ToUpper();
                    if (alpacaSymbol.Contains("US500") || alpacaSymbol.Contains("SP500") || alpacaSymbol.Contains("INDEX") || alpacaSymbol.Contains("CASH"))
                        alpacaSymbol = "SPY";

                    Print($"📥 Téléchargement historique {alpacaSymbol} 1m depuis Alpaca (pagination, ~1 an)...");
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
                            string url = $"https://data.alpaca.markets/v2/stocks/bars?symbols={alpacaSymbol}&timeframe=1Min&limit=10000&start={startDate}&feed={AlpacaFeed}";
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
                                    barsElem.TryGetProperty(alpacaSymbol, out var spyBars))
                                {
                                    int countBefore = result.Count;
                                    foreach (var item in spyBars.EnumerateArray())
                                    {
                                        DateTime ts = item.TryGetProperty("t", out var tProp) ? tProp.GetDateTime() : DateTime.MinValue;
                                        result.Add(new SimpleBar
                                        {
                                            Timestamp = ts,
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
                            result = result.OrderBy(b => b.Timestamp).ToList();
                            Print($"✅ {result.Count} barres 1m {alpacaSymbol} chargées et triées chronologiquement depuis Alpaca API !");
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

            foreach (var b in localBars) result.Add(new SimpleBar { Timestamp = b.OpenTime, Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, TickVolume = b.TickVolume });
            result = result.OrderBy(b => b.Timestamp).ToList();
            return result;
        }

        private void RunTrainingPhase()
        {
            Print($"⚙️ PHASE 1 : Extraction des caractéristiques causales ({TrainingHistoryBars} barres)...");

            List<SimpleBar> barsList = FetchBarsForTraining();
            _trainingBars = barsList;
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
                double effectiveVol = bar.TickVolume > 1.0 ? bar.TickVolume : (range * 1000.0);
                double closePos = (closePrice - bar.Low) / range;
                double buyVol = effectiveVol * closePos;
                double sellVol = effectiveVol * (1.0 - closePos);
                double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = barsList[i - k].Close;
                    double vk = barsList[i - k].TickVolume > 1.0 ? barsList[i - k].TickVolume : (Math.Max(barsList[i - k].High - barsList[i - k].Low, 0.01) * 1000.0);
                    if (k < 20) { sum20 += c; sumVol += vk; }
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

                bool isVolumeSpike = effectiveVol >= avgVolume * 1.1;
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
                string alpacaSymbol = string.IsNullOrEmpty(TargetSymbol) ? "NVDA" : TargetSymbol.Trim().ToUpper();
                if (alpacaSymbol.Contains("US500") || alpacaSymbol.Contains("SP500") || alpacaSymbol.Contains("INDEX") || alpacaSymbol.Contains("CASH"))
                    alpacaSymbol = "SPY";

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("APCA-API-KEY-ID", AlpacaKeyId.Trim());
                    client.DefaultRequestHeaders.Add("APCA-API-SECRET-KEY", AlpacaSecretKey.Trim());
                    client.Timeout = TimeSpan.FromSeconds(5);

                    string startDate = DateTime.UtcNow.AddHours(-4).ToString("yyyy-MM-ddTHH:mm:ssZ");
                    string url = $"https://data.alpaca.markets/v2/stocks/bars?symbols={alpacaSymbol}&timeframe=1Min&limit=55&start={startDate}&feed={AlpacaFeed}";
                    var response = client.GetAsync(url).Result;

                    if (response.IsSuccessStatusCode)
                    {
                        string json = response.Content.ReadAsStringAsync().Result;
                        using (var doc = System.Text.Json.JsonDocument.Parse(json))
                        {
                            if (doc.RootElement.TryGetProperty("bars", out var barsElem) &&
                                barsElem.TryGetProperty(alpacaSymbol, out var spyBars))
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

            // Clôture EOD à heure UTC fixe (21h00 par défaut), identique aux backtests C#.
            // L'horloge broker (MarketHours) reste utilisée en second garde-fou, mais elle ne
            // peut pas servir de référence de parité : elle dépend du symbole et du courtier.
            TimeSpan nowUtcTod = Server.TimeInUtc.TimeOfDay;
            TimeSpan eodCloseUtc = TimeSpan.FromHours(EodCloseUtcHours);
            TimeSpan entryCutoffUtc = TimeSpan.FromHours(EntryCutoffUtcHours);

            bool isAfterEodUtc = nowUtcTod >= eodCloseUtc;
            bool isSessionClosed = isAfterEodUtc
                                   || !Symbol.MarketHours.IsOpened()
                                   || Symbol.MarketHours.TimeTillClose() <= TimeSpan.FromMinutes(5);

            // Clôture automatique hors-session pour éliminer les spreads larges
            if (isSessionClosed)
            {
                if (isAfterEodUtc && Positions.FindAll("MlofiFtmo").Length > 0)
                    Print($"🌙 CLÔTURE EOD {EodCloseUtcHours:F2}h UTC atteinte — liquidation des positions.");

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

            // Cutoff d'entrée (18h30 UTC par défaut) : aucune NOUVELLE position au-delà, afin de
            // laisser du temps de session avant la clôture EOD. Le BE ci-dessus continue de
            // gérer les positions déjà ouvertes. Absent jusqu'ici côté cBot alors que les deux
            // backtests C# l'appliquent — c'était une divergence de parité.
            if (nowUtcTod >= entryCutoffUtc)
            {
                return;
            }

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

                double range = Math.Max(bar.High - bar.Low, 0.01);
                double effectiveVol = bar.TickVolume > 1.0 ? bar.TickVolume : (range * 1000.0);
                currentBarVolume = effectiveVol;

                double closePos = (currentPrice - bar.Low) / range;
                double buyVol = effectiveVol * closePos;
                double sellVol = effectiveVol * (1.0 - closePos);
                mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = bars1m[idx - k].Close;
                    double vk = bars1m[idx - k].TickVolume > 1.0 ? bars1m[idx - k].TickVolume : (Math.Max(bars1m[idx - k].High - bars1m[idx - k].Low, 0.01) * 1000.0);
                    if (k < 20) { sum20 += c; sumVol += vk; }
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
                isVolumeSpike = effectiveVol >= avgVolume * 1.1;
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
                    mlofiScore, currentPrice, ema20, 0, ema20, ema50, atr1m, currentBarVolume, avgVolume, rsi14, label: false);

                var prediction = _mlPredictor.Predict(featureSample);

                Print($"🧠 [ML FastTree] Évaluation du Signal : Probabilité = {prediction.Probability * 100:F1}% (Seuil Requis = 40.0%) -> {(prediction.Probability >= 0.40f ? "VALIDÉ ✅" : "REJETÉ ❌")}");

                if (prediction.Probability < 0.40f)
                {
                    return;
                }
            }

            double currentOverallDrawdownPct = (_peakEquity - Account.Equity) / _peakEquity * 100.0;
            double dailyDrawdownPct = (_dailyStartEquity - Account.Equity) / _dailyStartEquity * 100.0;
            double effectiveRiskPct = (currentOverallDrawdownPct >= MaxDrawdownPct) ? (RiskPerTradePct * 0.5) : RiskPerTradePct;

            // Filtre préventif : Ne pas ouvrir de trade si un Stop Loss potentiel ferait franchir la limite de perte quotidienne
            if (dailyDrawdownPct + effectiveRiskPct >= MaxDailyLossPct)
            {
                Print($"🛡️ FILTRE PRÉVENTIF FTMO : Perte actuelle (-{dailyDrawdownPct:F2}%) + Risque du trade ({effectiveRiskPct:F2}%) >= Limite Jour ({MaxDailyLossPct}%). Trade ignoré pour protéger le compte !");
                return;
            }

            double riskBudgetDollars = Account.Equity * (effectiveRiskPct / 100.0);

            // Ratio d'échelle si l'analyse est faite sur SPY et l'exécution sur le symbole local cTrader (ex: US500.cash)
            double localPrice = Symbol.Ask;
            double ratio = (usingAlpacaLive && currentPrice > 0) ? (localPrice / currentPrice) : 1.0;

            double slDistance = atr1m * SlAtrMultiplier * ratio;
            double tpDistance = atr1m * TpAtrMultiplier * ratio;

            if (slDistance <= (0.05 * ratio)) slDistance = 0.50 * ratio;
            if (tpDistance <= (0.05 * ratio)) tpDistance = 1.00 * ratio;

            // Dual-cap sizing : risque ET valeur notionnelle.
            // Sans le cap notionnel, un SL très serré (ATR faible) produisait une taille
            // arbitrairement grande — aucun garde-fou de levier n'existait côté cBot.
            double unitsByRisk = riskBudgetDollars / slDistance;

            int maxPositions = Math.Max(1, MaxConcurrentTrades);
            double maxNotionalPerPosition = Account.Equity * (MaxLeverage / maxPositions);
            double unitsByNotionalCap = maxNotionalPerPosition / localPrice;

            double unitsCapped = Math.Min(unitsByRisk, unitsByNotionalCap);

            double volumeLots = Symbol.VolumeInUnitsToQuantity(unitsCapped);
            volumeLots = Symbol.NormalizeVolumeInUnits(volumeLots, RoundingMode.ToNearest);

            if (volumeLots <= 0) return;

            if (unitsByNotionalCap < unitsByRisk)
            {
                Print($"⚖️ CAP NOTIONNEL APPLIQUÉ : sizing risque={unitsByRisk:N0} unités -> plafonné à {unitsByNotionalCap:N0} " +
                      $"(levier max {MaxLeverage:F1}x / {maxPositions} positions = {MaxLeverage / maxPositions:F2}x equity par trade).");
            }

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

                // Déplacement du Stop Loss à Break-Even. Seuil paramétrable, aligné sur les
                // backtests C# (0.80 du TP) : à 0.50 le live coupait les trades plus tôt que
                // le backtest, ce qui rendait les deux non comparables.
                if (currentProfitPips >= tpDistancePips * BreakEvenTriggerPct)
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

            // Utiliser les barres d'entraînement (Alpaca avec vrai volume) si disponibles
            List<SimpleBar> sourceBars;
            if (_trainingBars.Count > 500)
            {
                // Filtrer uniquement la journée courante à partir des barres Alpaca
                sourceBars = _trainingBars.Where(b => b.Timestamp.Date == Server.Time.Date &&
                                                       b.Timestamp.TimeOfDay >= new TimeSpan(14, 30, 0) &&
                                                       b.Timestamp.TimeOfDay < new TimeSpan(20, 55, 0)).ToList();
                if (sourceBars.Count < 50)
                {
                    // Fallback : dernières barres disponibles des données d'entraînement
                    sourceBars = _trainingBars.TakeLast(390).ToList();
                }
            }
            else
            {
                // Fallback sur les barres cTrader locales (session régulière US)
                var todaysBars = Bars.Where(b => b.OpenTime.Date == Server.Time.Date &&
                                                  b.OpenTime.TimeOfDay >= new TimeSpan(14, 30, 0) &&
                                                  b.OpenTime.TimeOfDay < new TimeSpan(20, 55, 0)).ToList();
                sourceBars = new List<SimpleBar>();
                foreach (var b in todaysBars)
                    sourceBars.Add(new SimpleBar { Timestamp = b.OpenTime, Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, TickVolume = b.TickVolume });

                if (sourceBars.Count < 50)
                {
                    sourceBars = new List<SimpleBar>();
                    foreach (var b in Bars.TakeLast(390))
                        sourceBars.Add(new SimpleBar { Timestamp = b.OpenTime, Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, TickVolume = b.TickVolume });
                }
            }

            if (sourceBars.Count < 50)
            {
                Print("⚠️ Pas assez de barres sur la journée courante pour la simulation.");
                Print($"==========================================================================");
                return;
            }

            double simCapital = InitialCapital;
            int totalTrades = 0, winTrades = 0, lossTrades = 0;
            double simPeakCapital = simCapital;
            double maxDrawdownPct = 0;

            bool inPos = false;
            string posSide = "";
            double entryPrice = 0, slDist = 0, tpDist = 0;
            bool isBreakEven = false;
            double riskBudgetAtEntry = 0;

            for (int i = 50; i < sourceBars.Count; i++)
            {
                var bar = sourceBars[i];
                double closePrice = bar.Close;

                if (inPos)
                {
                    double currentProfit = posSide == "buy" ? (closePrice - entryPrice) : (entryPrice - closePrice);

                    // Break-even à 40% du TP (comme le projet C#)
                    if (!isBreakEven && currentProfit >= tpDist * 0.40)
                    {
                        isBreakEven = true;
                    }

                    bool hitTP = posSide == "buy" ? (bar.High >= entryPrice + tpDist) : (bar.Low <= entryPrice - tpDist);
                    bool hitSL = posSide == "buy" ? (bar.Low <= (isBreakEven ? entryPrice : entryPrice - slDist))
                                                  : (bar.High >= (isBreakEven ? entryPrice : entryPrice + slDist));

                    if (hitTP || hitSL)
                    {
                        double pnl;
                        if (hitTP) pnl = +riskBudgetAtEntry * (tpDist / slDist); // Gain = Risk * RR
                        else if (isBreakEven) pnl = 0; // Break-even
                        else pnl = -riskBudgetAtEntry; // Perte = -Risk

                        simCapital += pnl;
                        totalTrades++;
                        if (pnl > 0) winTrades++; else if (pnl < 0) lossTrades++;

                        if (simCapital > simPeakCapital) simPeakCapital = simCapital;
                        double dd = (simPeakCapital - simCapital) / simPeakCapital * 100.0;
                        if (dd > maxDrawdownPct) maxDrawdownPct = dd;

                        double currentDailyLossPct = (InitialCapital - simCapital) / InitialCapital * 100.0;
                        if (currentDailyLossPct >= MaxDailyLossPct)
                        {
                            Print($"⛔ [DISJONCTEUR SIMULATION REPLAY] Perte Max du Jour atteinte (-{currentDailyLossPct:F2}% >= {MaxDailyLossPct}%). Arrêt des trades simulés pour le reste de la journée.");
                            break;
                        }

                        inPos = false;
                    }
                }

                if (!inPos)
                {
                    double range = Math.Max(bar.High - bar.Low, 0.01);
                    double effectiveVol = bar.TickVolume > 1.0 ? bar.TickVolume : (range * 1000.0);
                    double closePos = (closePrice - bar.Low) / range;
                    double buyVol = effectiveVol * closePos;
                    double sellVol = effectiveVol * (1.0 - closePos);
                    double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                    double sum20 = 0, sum50 = 0, sumVol = 0;
                    for (int k = 0; k < 50; k++)
                    {
                        double c = sourceBars[i - k].Close;
                        double v = sourceBars[i - k].TickVolume > 1.0 ? sourceBars[i - k].TickVolume : 1.0;
                        if (k < 20) { sum20 += c; sumVol += v; }
                        sum50 += c;
                    }
                    double ema20 = sum20 / 20.0;
                    double ema50 = sum50 / 50.0;
                    double avgVolume = sumVol / 20.0;

                    double gains = 0, losses = 0;
                    for (int k = 0; k < 14; k++)
                    {
                        double diff = sourceBars[i - k].Close - sourceBars[i - k - 1].Close;
                        if (diff > 0) gains += diff; else losses -= diff;
                    }
                    double rs = losses > 0 ? gains / losses : 1.0;
                    double rsi14 = 100.0 - (100.0 / (1.0 + rs));

                    double trSum = 0;
                    for (int k = 0; k < 14; k++)
                    {
                        double tr1 = sourceBars[i - k].High - sourceBars[i - k].Low;
                        double tr2 = Math.Abs(sourceBars[i - k].High - sourceBars[i - k - 1].Close);
                        double tr3 = Math.Abs(sourceBars[i - k].Low - sourceBars[i - k - 1].Close);
                        trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                    }
                    double atr1m = trSum / 14.0;

                    bool isVolumeSpike = effectiveVol >= avgVolume * 1.1;
                    bool isBuySetup = closePrice > ema20 && mlofiScore >= MlofiThreshold && isVolumeSpike;
                    bool isSellSetup = closePrice < ema20 && mlofiScore <= -MlofiThreshold && isVolumeSpike;

                    if (isBuySetup || isSellSetup)
                    {
                        // Validation ML obligatoire (si ML rejeté → pas de trades)
                        if (_mlPredictor != null && _mlPredictor.IsTrained)
                        {
                            var featureSample = MlofiMlFeatureExtractor.ExtractFeatures(
                                mlofiScore, closePrice, ema20, 0, ema20, ema50, atr1m, 0, avgVolume, rsi14, label: false);

                            var prediction = _mlPredictor.Predict(featureSample);
                            if (prediction.Probability < 0.20f)
                            {
                                continue;
                            }
                        }

                        slDist = Math.Max(atr1m * SlAtrMultiplier, closePrice * 0.001); // Min 0.1% du prix
                        tpDist = Math.Max(atr1m * TpAtrMultiplier, closePrice * 0.002); // Min 0.2% du prix

                        riskBudgetAtEntry = simCapital * (RiskPerTradePct / 100.0);

                        inPos = true;
                        posSide = isBuySetup ? "buy" : "sell";
                        entryPrice = closePrice;
                        isBreakEven = false;
                    }
                }
            }

            double winRate = totalTrades > 0 ? ((double)winTrades / totalTrades * 100.0) : 0.0;
            double netPnL = simCapital - InitialCapital;

            Print($"Barres M1 Analysées      : {sourceBars.Count} Barres");
            Print($"Trades Simulés du Jour  : {totalTrades} (Gagnants: {winTrades}, Perdants: {lossTrades})");
            Print($"Win Rate Simulée Journée : {winRate:F1} %");
            Print($"PnL Réalisé (Simulation) : ${netPnL:+$#,##0.00;-$#,##0.00} ({(netPnL / InitialCapital * 100.0):F2} %)");
            Print($"Max Daily Drawdown       : -{maxDrawdownPct:F2} %");
            Print($"Conformité FTMO          : {(maxDrawdownPct < MaxDailyLossPct ? "VALIDÉ ✅ (Respect des limites)" : "ATTENTION 🚨 (Seuil dépassé)")}");
            Print($"==========================================================================");
        }

        private void TrainModelForWindow(List<SimpleBar> barsList)
        {
            int totalBars = barsList.Count;
            if (totalBars < 100) return;

            int countToUse = Math.Min(TrainingHistoryBars, totalBars - 20);
            int startIndex = Math.Max(0, totalBars - countToUse - 20);

            List<MlofiMlFeatureData> trainingSamples = new List<MlofiMlFeatureData>();

            for (int i = startIndex + 50; i < totalBars - 20; i++)
            {
                var bar = barsList[i];
                double closePrice = bar.Close;

                double range = Math.Max(bar.High - bar.Low, 0.01);
                double effectiveVol = bar.TickVolume > 1.0 ? bar.TickVolume : (range * 1000.0);
                double closePos = (closePrice - bar.Low) / range;
                double buyVol = effectiveVol * closePos;
                double sellVol = effectiveVol * (1.0 - closePos);
                double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                double sum20 = 0, sum50 = 0, sumVol = 0;
                for (int k = 0; k < 50; k++)
                {
                    double c = barsList[i - k].Close;
                    double vk = barsList[i - k].TickVolume > 1.0 ? barsList[i - k].TickVolume : (Math.Max(barsList[i - k].High - barsList[i - k].Low, 0.01) * 1000.0);
                    if (k < 20) { sum20 += c; sumVol += vk; }
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

                bool isVolumeSpike = effectiveVol >= avgVolume * 1.1;
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

                trainingSamples.Add(MlofiMlFeatureExtractor.ExtractFeatures(mlofiScore, closePrice, ema20, 0, ema20, ema50, atr1m, effectiveVol, avgVolume, rsi14, label: label));
            }

            _mlPredictor = new MlofiMlPredictorEngine();
            var res = _mlPredictor.TrainModel(trainingSamples, Print);
            Print($"[Walk-Forward ML] Echantillons : {res.SampleCount} | Accuracy: {res.Accuracy * 100:F2}%");
        }

        private void RunOneYearWalkForwardBacktest()
        {
            var historicalBars = _trainingBars.Count > 500 ? _trainingBars : Bars.Select(b => 
                new SimpleBar { Timestamp = b.OpenTime, Open = b.Open, High = b.High, Low = b.Low, Close = b.Close, TickVolume = b.TickVolume }).ToList();

            Print($"==========================================================================");
            Print($"📊 [BACKTEST 1 AN HISTORIQUE - {historicalBars.Count} BARRES M1 ({Symbol.Name})]");
            Print($"==========================================================================");

            if (historicalBars.Count < 500)
            {
                Print("⚠️ Historique insuffisant pour le backtest 1 An.");
                Print($"==========================================================================");
                return;
            }

            double simCapital = InitialCapital;
            int totalTrades = 0, winTrades = 0, lossTrades = 0;
            double simPeakCapital = simCapital;
            double maxDrawdownPct = 0;

            var activePositions = new List<(string Side, double Entry, double Sl, double Tp, double Risk, double SlDist)>();

            DateTime currentDay = DateTime.MinValue;
            double dayStartCapital = simCapital;
            bool dayHalted = false;

            int windowTrainSize = 25000;
            int windowTestSize = 6000;

            for (int windowStart = 0; windowStart + windowTrainSize < historicalBars.Count; windowStart += windowTestSize)
            {
                int trainStart = windowStart;
                int trainEnd = windowStart + windowTrainSize;
                int testStart = trainEnd;
                int testEnd = Math.Min(testStart + windowTestSize, historicalBars.Count);

                if (testEnd <= testStart) break;

                var trainBars = historicalBars.Skip(trainStart).Take(windowTrainSize).ToList();
                TrainModelForWindow(trainBars);

                for (int i = Math.Max(50, testStart); i < testEnd; i++)
                {
                var bar = historicalBars[i];
                double closePrice = bar.Close;

                if (bar.Timestamp.Date != currentDay)
                {
                    currentDay = bar.Timestamp.Date;
                    dayStartCapital = simCapital;
                    dayHalted = false;
                }

                if (simCapital > simPeakCapital) simPeakCapital = simCapital;
                double dd = (simPeakCapital - simCapital) / simPeakCapital * 100.0;
                if (dd > maxDrawdownPct) maxDrawdownPct = dd;

                // Session US Regular 13h30 - 21h00 UTC (9h30 - 16h00 EST)
                var tod = bar.Timestamp.TimeOfDay;
                if (tod < new TimeSpan(13, 30, 0) || tod >= new TimeSpan(21, 0, 0))
                {
                    for (int p = activePositions.Count - 1; p >= 0; p--)
                    {
                        var pos = activePositions[p];
                        double pnlPoints = pos.Side == "buy" ? (closePrice - pos.Entry) : (pos.Entry - closePrice);
                        double rMult = pos.SlDist > 0 ? (pnlPoints / pos.SlDist) : 0;
                        double pnl = pos.Risk * rMult;
                        simCapital += pnl;
                        totalTrades++;
                        if (pnl > 0) winTrades++; else if (pnl < 0) lossTrades++;
                        activePositions.RemoveAt(p);
                    }
                    continue;
                }

                if (dayHalted) continue;

                // 1. Évaluer les positions en cours
                for (int p = activePositions.Count - 1; p >= 0; p--)
                {
                    var pos = activePositions[p];
                    bool hitTP = pos.Side == "buy" ? (bar.High >= pos.Tp) : (bar.Low <= pos.Tp);
                    bool hitSL = pos.Side == "buy" ? (bar.Low <= pos.Sl) : (bar.High >= pos.Sl);

                    if (hitTP || hitSL)
                    {
                        double pnl = hitTP ? (+pos.Risk * (TpAtrMultiplier / SlAtrMultiplier)) : -pos.Risk;
                        simCapital += pnl;
                        totalTrades++;
                        if (pnl > 0) winTrades++; else if (pnl < 0) lossTrades++;

                        double dayLossPct = (dayStartCapital - simCapital) / dayStartCapital * 100.0;
                        if (dayLossPct >= MaxDailyLossPct) dayHalted = true;

                        activePositions.RemoveAt(p);
                    }
                }

                // 2. Chercher de nouvelles entrées (jusqu'à MaxConcurrentTrades)
                if (activePositions.Count < MaxConcurrentTrades && !dayHalted)
                {
                    double range = Math.Max(bar.High - bar.Low, 0.01);
                    double effectiveVol = bar.TickVolume > 1.0 ? bar.TickVolume : (range * 1000.0);
                    double closePos = (closePrice - bar.Low) / range;
                    double buyVol = effectiveVol * closePos;
                    double sellVol = effectiveVol * (1.0 - closePos);
                    double mlofiScore = (buyVol + sellVol > 0) ? (buyVol - sellVol) / (buyVol + sellVol) : 0.0;

                    double sum20 = 0, sum50 = 0, sumVol = 0;
                    for (int k = 0; k < 50; k++)
                    {
                        double c = historicalBars[i - k].Close;
                        double v = historicalBars[i - k].TickVolume > 1.0 ? historicalBars[i - k].TickVolume : 1.0;
                        if (k < 20) { sum20 += c; sumVol += v; }
                        sum50 += c;
                    }
                    double ema20 = sum20 / 20.0;
                    double ema50 = sum50 / 50.0;
                    double avgVolume = sumVol / 20.0;

                    double gains = 0, losses = 0;
                    for (int k = 0; k < 14; k++)
                    {
                        double diff = historicalBars[i - k].Close - historicalBars[i - k - 1].Close;
                        if (diff > 0) gains += diff; else losses -= diff;
                    }
                    double rs = losses > 0 ? gains / losses : 1.0;
                    double rsi14 = 100.0 - (100.0 / (1.0 + rs));

                    double trSum = 0;
                    for (int k = 0; k < 14; k++)
                    {
                        double tr1 = historicalBars[i - k].High - historicalBars[i - k].Low;
                        double tr2 = Math.Abs(historicalBars[i - k].High - historicalBars[i - k - 1].Close);
                        double tr3 = Math.Abs(historicalBars[i - k].Low - historicalBars[i - k - 1].Close);
                        trSum += Math.Max(tr1, Math.Max(tr2, tr3));
                    }
                    double atr1m = trSum / 14.0;

                    bool isVolumeSpike = effectiveVol >= avgVolume * 1.1;
                    bool isBuySetup = closePrice > ema20 && mlofiScore >= MlofiThreshold && isVolumeSpike;
                    bool isSellSetup = closePrice < ema20 && mlofiScore <= -MlofiThreshold && isVolumeSpike;

                    if (isBuySetup || isSellSetup)
                    {
                        if (_mlPredictor != null && _mlPredictor.IsTrained)
                        {
                            var featureSample = MlofiMlFeatureExtractor.ExtractFeatures(
                                mlofiScore, closePrice, ema20, 0, ema20, ema50, atr1m, effectiveVol, avgVolume, rsi14, label: false);

                            var prediction = _mlPredictor.Predict(featureSample);
                            if (prediction.Probability < 0.40f) continue;
                        }

                        double currentDdFromPeak = (simPeakCapital - simCapital) / simPeakCapital * 100.0;
                        double effectiveRiskPct = (currentDdFromPeak >= MaxDrawdownPct) ? (RiskPerTradePct * 0.5) : RiskPerTradePct;

                        double currentDayLossPct = (dayStartCapital - simCapital) / dayStartCapital * 100.0;
                        if (currentDayLossPct + effectiveRiskPct >= MaxDailyLossPct) continue;

                        double slDist = atr1m * SlAtrMultiplier; if (slDist <= 0.05) slDist = 0.50;
                        double tpDist = atr1m * TpAtrMultiplier; if (tpDist <= 0.05) tpDist = 1.00;
                        double slPrice = isBuySetup ? closePrice - slDist : closePrice + slDist;
                        double tpPrice = isBuySetup ? closePrice + tpDist : closePrice - tpDist;
                        double riskBudget = simCapital * (effectiveRiskPct / 100.0);

                        activePositions.Add((isBuySetup ? "buy" : "sell", closePrice, slPrice, tpPrice, riskBudget, slDist));
                    }
                }
                } // End inner test loop
            } // End outer walk-forward loop

            double winRate = totalTrades > 0 ? ((double)winTrades / totalTrades * 100.0) : 0.0;
            double netPnL = simCapital - InitialCapital;

            Print($"Période Analysée          : ~1 An ({historicalBars.Count} Barres M1)");
            Print($"Total Trades Exécutés     : {totalTrades} (Gagnants: {winTrades}, Perdants: {lossTrades})");
            Print($"Win Rate Global 1 An      : {winRate:F1} %");
            Print($"Gain Net Total 1 An       : ${netPnL:+$#,##0.00;-$#,##0.00} ({(netPnL / InitialCapital * 100.0):F2} %)");
            Print($"Max DD Peak (HighWater)   : -{maxDrawdownPct:F2} %");
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
//
