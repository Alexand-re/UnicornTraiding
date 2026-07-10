using cAlgo.API;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace cAlgo.Robots
{
    // ==========================================================================================
    // CONFIGURATION CLASSES
    // ==========================================================================================
    public class CrashCatcherMstpConfiguration
    {
        public int UniverseSize { get; set; } = 5;
        public double InitialDropPct { get; set; } = 0.02935121389541366;
        public double MaxCorrelationThreshold { get; set; } = 0.8189537877538027;
        public int CorrelationLookback { get; set; } = 10;
        public double SystemicCrashThreshold { get; set; } = 0.8285244438697232;
        public double SystemicMomentumFactor { get; set; } = 0.32077447344585064;
        public int MomentumLookback { get; set; } = 5;
        public int WindowSize { get; set; } = 16;
        public double GridSpacingPct { get; set; } = 0.011120756678805108;
        public double ScalingRatio { get; set; } = 4.869340749396636;
        public bool UseNormalizedLeverage { get; set; } = true;
        public int MaxTranchesPerSymbol { get; set; } = 2;
        public int MinDaysBetweenTranches { get; set; } = 2;
        public bool SlBasedOnFirstTranche { get; set; } = true;
        public List<double>? TrancheWeights { get; set; } = null;
        public double SymbolStopLossPct { get; set; } = 0.47731821875894365;
        public double TrailingStopPct { get; set; } = 0.07412747797748423;
        public double AtrTrailingStopMultiplier { get; set; } = 7.620128704989389;
        public double TargetProfitFinalPct { get; set; } = 0.09015617075849146;
        public List<TakeProfitStage> Stages { get; set; } = new()
        {
            new TakeProfitStage {ProfitThresholdPct = 0.014658137902697698, SellRatio = 0.3012148830158754},
            new TakeProfitStage {ProfitThresholdPct = 0.06381459372129122, SellRatio = 0.27301484802108916},
            new TakeProfitStage {ProfitThresholdPct = 0.07443756027241621, SellRatio = 0.10777701898293367}
        };
        public TrailingStopMode TsMode { get; set; } = TrailingStopMode.AllPosition;
        public bool ExitTrancheAtStageThreshold { get; set; } = true;
        public double LeverageMultiplier { get; set; } = 4.0;
        public double MaxLeverageLimit { get; set; } = 4.7;
        public double RecoveryLeverageMultiplier { get; set; } = 0.26615483312222865;
        public int RecoveryDurationDays { get; set; } = 22;
        public double CircuitBreakerPct { get; set; } = 0.9528249264009413;
        public double TrailingEquityStopPct { get; set; } = 0.03961103479359813;
        public double VolatilityTarget { get; set; } = 0.0114;
        public double VolatilityMinMultiplier { get; set; } = 1.0;
        public bool UseRegimeFilter { get; set; } = false;
        public bool DisableRotationExit { get; set; } = true;
        public double SlippagePct { get; set; } = 0.00025;
        public bool UseFractionalShares { get; set; } = true;
    }

    public class TakeProfitStage
    {
        public double ProfitThresholdPct { get; set; }
        public double SellRatio { get; set; }
    }

    public enum TrailingStopMode
    {
        None = 0,
        AllPosition = 1,
        IndividualTranche = 2
    }

    // ==========================================================================================
    // STATE CLASSES
    // ==========================================================================================
    [BsonIgnoreExtraElements]
    public class CrashCatcherDailyState
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public DateTime Date { get; set; }
        public double TotalEquity { get; set; }
        public double Cash { get; set; }
        public double MaxDrawdown { get; set; }
        public double PeakEquity { get; set; }
        public double DailyReturn { get; set; }
        public double TotalRealizedPnl { get; set; }
        public double DailyRealizedPnl { get; set; }
        public List<CrashCatcherOrder> ActiveOrders { get; set; } = new();
        public List<CrashCatcherClosedOrder> ClosedToday { get; set; } = new();
        public bool CircuitBreakerTriggered { get; set; }
        public bool ProfitFloorTriggered { get; set; }
        public double InitialCash { get; set; }
        public Dictionary<string, DateTime> DynamicBlacklist { get; set; } = new();
        public Dictionary<string, DateTime> LastEntryDates { get; set; } = new();
        public double PeakRealizedEquity { get; set; }
        public Dictionary<string, double> SymbolPnL { get; set; } = new();

        public double CurrentDrawdownPct { get; set; }
        public double TotalExposure { get; set; }
        public double EffectiveLeverage { get; set; }
        public double UnrealizedPnl { get; set; }
        public int ActiveSymbolCount { get; set; }
        public int BuyActionsToday { get; set; }
        public int SellActionsToday { get; set; }
        public bool SystemicCrashActive { get; set; }
        public int RecoveryDaysRemaining { get; set; }
        public int DaysSinceLastCrash { get; set; }
        public double RealizedEquity { get; set; }
        public double CumulativeCashInterest { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class CrashCatcherOrder
    {
        public string Symbol { get; set; } = string.Empty;
        public string Sector { get; set; } = string.Empty;
        public double Qty { get; set; }
        public double EntryPrice { get; set; }
        public double CurrentPrice { get; set; }
        public DateTime EntryDate { get; set; }
        public double HighPriceSinceEntry { get; set; }
        public int LastStageReached { get; set; } = 0;
        public int TrancheIndex { get; set; } = 0;
        public bool IsRunner { get; set; }
    }

    public class CrashCatcherClosedOrder
    {
        public string Symbol { get; set; } = string.Empty;
        public double Qty { get; set; }
        public double EntryPrice { get; set; }
        public double ExitPrice { get; set; }
        public double RealizedPnl { get; set; }
        public double GainPct { get; set; }
        public DateTime EntryDate { get; set; }
        public DateTime ExitDate { get; set; }
        public bool WasRunner { get; set; }
        public string ExitReason { get; set; } = string.Empty;
        public int HoldingDays { get; set; }
    }

    public class Bars
    {
        public DateTime Timestamp { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public long Volume { get; set; }
        public int TradeCount { get; set; }
        public double VolumeWeightedAveragePrice { get; set; }
    }

    // ==========================================================================================
    // TRADING ENGINE
    // ==========================================================================================
    public class CrashCatcherMstpEngine
    {
        public enum ActionType
        {
            Buy,
            Sell,
            Liquidate,
            PartialSell,
            Rebalance
        }

        public class TradingAction
        {
            public string Symbol { get; set; } = string.Empty;
            public ActionType Type { get; set; }
            public double Quantity { get; set; }
            public double Price { get; set; }
            public double Amount { get; set; }
            public int? NewStage { get; set; }
            public string Reason { get; set; } = string.Empty;
            public DateTime? NewBlacklistEnd { get; set; }
            public CrashCatcherOrder? TargetOrder { get; set; }
            public int TrancheIndex { get; set; }
        }

        public Action<string>? OnDebug { get; set; }

        public class EngineResult
        {
            public List<TradingAction> Actions { get; set; } = new();
            public bool CircuitBreakerTriggered { get; set; }
            public bool SystemicCrashActive { get; set; }
            public bool ProfitFloorTriggered { get; set; }
        }

        public class SimulationDailyDetails
        {
            public DateTime Date { get; set; }
            public double Equity { get; set; }
            public double Cash { get; set; }
            public double Exposure { get; set; }
            public double Leverage { get; set; }
            public bool IsKrach { get; set; }
            public int PositionCount { get; set; }
        }

        public class SimulationResult
        {
            public double TotalEquity { get; set; }
            public double FinalCash { get; set; }
            public double MaxDrawdown { get; set; }
            public double TotalReturn { get; set; }
            public int TradeCount { get; set; }
            public List<SimulationDailyDetails> DailyDetails { get; set; } = new();
            public List<TradingAction> TradeHistory { get; set; } = new();
        }

        public SimulationResult RunSimulation(CrashCatcherMstpConfiguration config,
            double initialCapital, DateTime startDate, DateTime endDate,
            Dictionary<string, List<Bars>> allBars, List<string> tradingUniverse,
            List<string> systemicUniverse, Dictionary<string, string>? sectorMap = null)
        {
            SimulationResult result = new SimulationResult();
            CrashCatcherDailyState state = new CrashCatcherDailyState
                {InitialCash = initialCapital, Cash = initialCapital, Date = startDate};

            List<string> symbols = tradingUniverse ?? allBars.Keys.Where(s => s != "SPY").ToList();
            List<string> systemicPool = systemicUniverse ?? symbols;
            int tradeCount = 0;

            List<DateTime> dRange = allBars["SPY"]
                .Where(b => b.Timestamp.Date >= startDate.Date && b.Timestamp.Date <= endDate.Date)
                .Select(b => b.Timestamp.Date)
                .OrderBy(d => d)
                .ToList();

            DateTime? lastDate = null;
            foreach (DateTime d in dRange)
            {
                lastDate = d;

                Dictionary<string, int> indices = new Dictionary<string, int>();
                foreach (KeyValuePair<string, List<Bars>> kvp in allBars)
                {
                    int idx = kvp.Value.FindLastIndex(b => b.Timestamp.Date <= d);
                    if (idx >= 0) indices[kvp.Key] = idx;
                }

                if (!indices.ContainsKey("SPY")) continue;

                double mtm = 0;
                foreach (CrashCatcherOrder o in state.ActiveOrders)
                {
                    if (indices.TryGetValue(o.Symbol, out int idx))
                    {
                        double previousClose = o.CurrentPrice;
                        double newClose = allBars[o.Symbol][idx].Close;

                        if (previousClose > 0 && newClose > 0)
                        {
                            double ratio = newClose / previousClose;

                            if (Math.Abs(ratio - 1.0) > 0.05)
                            {
                                double splitFactor = 1.0;
                                if (Math.Abs(ratio - 2.0) < 0.1) splitFactor = 2.0;
                                else if (Math.Abs(ratio - 3.0) < 0.1) splitFactor = 3.0;
                                else if (Math.Abs(ratio - 4.0) < 0.1) splitFactor = 4.0;
                                else if (Math.Abs(ratio - 1.5) < 0.1) splitFactor = 1.5;
                                else if (Math.Abs(ratio - 0.5) < 0.05) splitFactor = 0.5;
                                else if (Math.Abs(ratio - 0.333) < 0.05) splitFactor = 0.333;
                                else if (Math.Abs(ratio - 0.25) < 0.05) splitFactor = 0.25;
                                else if (Math.Abs(ratio - 0.1) < 0.05) splitFactor = 0.1;
                                else if (Math.Abs(ratio - 0.2) < 0.05) splitFactor = 0.2;
                                else if (Math.Abs(ratio - 20.0) < 1.0) splitFactor = 20.0;
                                else if (Math.Abs(ratio - 1.333) < 0.05) splitFactor = 1.333;
                                else if (Math.Abs(ratio - 0.666) < 0.05) splitFactor = 0.666;
                                else if (Math.Abs(ratio - 1.666) < 0.05) splitFactor = 1.666;

                                if (splitFactor != 1.0)
                                {
                                    Console.WriteLine(
                                        $"[SPLIT DETECTED] {o.Symbol} on {d:yyyy-MM-dd}: ratio={ratio:F4}, factor={splitFactor}. Adjusting position.");

                                    o.Qty = (o.Qty * splitFactor);
                                    o.EntryPrice /= splitFactor;
                                    o.HighPriceSinceEntry /= splitFactor;

                                    newClose = previousClose / splitFactor;
                                }
                            }
                        }

                        o.CurrentPrice = newClose;
                        mtm += o.Qty * o.CurrentPrice;
                        if (o.CurrentPrice > o.HighPriceSinceEntry) o.HighPriceSinceEntry = o.CurrentPrice;
                    }
                }

                state.TotalEquity = state.Cash + mtm;

                if (state.TotalEquity > state.PeakEquity)
                {
                    state.PeakEquity = state.TotalEquity;
                }

                double currentDd = (state.PeakEquity > 0)
                    ? (state.TotalEquity - state.PeakEquity) / state.PeakEquity
                    : 0;
                if (currentDd < state.MaxDrawdown)
                {
                    state.MaxDrawdown = currentDd;
                }

                List<string> rankedSymbols = symbols
                    .Where(s => indices.ContainsKey(s) && indices[s] >= config.MomentumLookback)
                    .Select(s => new
                    {
                        Symbol = s,
                        Momentum = allBars[s][indices[s]].Close /
                                   allBars[s][indices[s] - config.MomentumLookback].Close
                    })
                    .OrderByDescending(x => x.Momentum)
                    .Select(x => x.Symbol)
                    .ToList();

                if (!config.UseNormalizedLeverage)
                {
                    List<string> currentPortfolioSymbols = state.ActiveOrders.Select(o => o.Symbol).Distinct().ToList();
                    foreach (string sym in currentPortfolioSymbols)
                    {
                        int rank = rankedSymbols.IndexOf(sym);
                        if (rank > config.UniverseSize * 2)
                        {
                            double p = allBars[sym][indices[sym]].Close;
                            ApplyActions(state, new EngineResult
                            {
                                Actions = new List<TradingAction>
                                {
                                    new TradingAction
                                    {
                                        Symbol = sym, Type = ActionType.Liquidate, Price = p, Reason = "RotationLegacy"
                                    }
                                }
                            }, d, config.SlippagePct);
                        }
                    }
                }

                double targetLev = config.LeverageMultiplier;

                double spyAtr = GetAtr("SPY", allBars["SPY"], indices["SPY"]);
                double spyPrice = allBars["SPY"][indices["SPY"]].Close;
                double spyVolPct = spyAtr / spyPrice;

                double volMultiplier = (spyVolPct > 0) ? (config.VolatilityTarget / spyVolPct) : 1.0;

                volMultiplier = Math.Clamp(volMultiplier, config.VolatilityMinMultiplier, 1);
                targetLev *= volMultiplier;

                if (state.RecoveryDaysRemaining > 0) targetLev *= config.RecoveryLeverageMultiplier;

                bool isSpyRegimeSafe = true;
                if (config.UseRegimeFilter && allBars.ContainsKey("SPY"))
                {
                    List<Bars> spyBars = allBars["SPY"];
                    int spyIdx = indices["SPY"];
                    if (spyIdx >= 200)
                    {
                        double ma200 = spyBars.GetRange(spyIdx - 200, 200).Average(b => b.Close);
                        isSpyRegimeSafe = spyBars[spyIdx].Close > ma200;
                    }
                }

                EngineResult dayRes = ProcessDay(d, config, state, allBars, indices, rankedSymbols, isSpyRegimeSafe,
                    systemicPool, sectorMap);
                ApplyActions(state, dayRes, d, config.SlippagePct);

                foreach (TradingAction act in dayRes.Actions)
                {
                    result.TradeHistory.Add(act);
                    if (act.Type == ActionType.Buy || act.Type == ActionType.Sell || act.Type == ActionType.PartialSell)
                        tradeCount++;
                }

                result.DailyDetails.Add(new SimulationDailyDetails
                {
                    Date = d,
                    Equity = state.TotalEquity,
                    Cash = state.Cash,
                    Exposure = state.ActiveOrders.Sum(o => o.Qty * allBars[o.Symbol][indices[o.Symbol]].Close),
                    Leverage = state.TotalEquity > 0
                        ? state.ActiveOrders.Sum(o => o.Qty * allBars[o.Symbol][indices[o.Symbol]].Close) /
                          state.TotalEquity
                        : 0,
                    IsKrach = dayRes.SystemicCrashActive,
                    PositionCount = state.ActiveOrders.Count
                });

                if (dayRes.CircuitBreakerTriggered || dayRes.ProfitFloorTriggered) break;
            }

            result.TotalEquity = state.TotalEquity;
            result.FinalCash = state.Cash;
            result.MaxDrawdown = state.MaxDrawdown;
            result.TradeCount = tradeCount;
            result.TotalReturn = (initialCapital > 0) ? (state.TotalEquity - initialCapital) / initialCapital : 0;

            OnDebug?.Invoke(
                $"[END] Simulation terminée. Equity: {result.TotalEquity:F0}, DD Max: {result.MaxDrawdown:P2}");

            return result;
        }

        public EngineResult ProcessDay(DateTime today, CrashCatcherMstpConfiguration config,
            CrashCatcherDailyState state, Dictionary<string, List<Bars>> allBars,
            Dictionary<string, int> symbolIndices, List<string> currentUniverse, bool isSpyRegimeSafe,
            List<string> systemicUniverse, Dictionary<string, string>? sectorMap = null)
        {
            EngineResult res = new EngineResult();

            foreach (CrashCatcherOrder o in state.ActiveOrders)
            {
                if (symbolIndices.TryGetValue(o.Symbol, out int idx))
                {
                    double previousClose = o.CurrentPrice;
                    double newClose = allBars[o.Symbol][idx].Close;

                    if (previousClose > 0 && newClose > 0)
                    {
                        double ratio = newClose / previousClose;
                        if (Math.Abs(ratio - 1.0) > 0.05)
                        {
                            double splitFactor = 1.0;
                            if (Math.Abs(ratio - 2.0) < 0.1) splitFactor = 2.0;
                            else if (Math.Abs(ratio - 3.0) < 0.1) splitFactor = 3.0;
                            else if (Math.Abs(ratio - 4.0) < 0.1) splitFactor = 4.0;
                            else if (Math.Abs(ratio - 1.5) < 0.1) splitFactor = 1.5;
                            else if (Math.Abs(ratio - 0.5) < 0.05) splitFactor = 0.5;
                            else if (Math.Abs(ratio - 0.333) < 0.05) splitFactor = 0.333;
                            else if (Math.Abs(ratio - 0.25) < 0.05) splitFactor = 0.25;
                            else if (Math.Abs(ratio - 0.1) < 0.05) splitFactor = 0.1;
                            else if (Math.Abs(ratio - 0.2) < 0.05) splitFactor = 0.2;
                            else if (Math.Abs(ratio - 20.0) < 1.0) splitFactor = 20.0;
                            else if (Math.Abs(ratio - 1.333) < 0.05) splitFactor = 1.333;
                            else if (Math.Abs(ratio - 0.666) < 0.05) splitFactor = 0.666;
                            else if (Math.Abs(ratio - 1.666) < 0.05) splitFactor = 1.666;

                            if (splitFactor != 1.0)
                            {
                                Console.WriteLine(
                                    $"[SPLIT DETECTED] {o.Symbol} on {today:yyyy-MM-dd}: ratio={ratio:F4}, factor={splitFactor}. Adjusting position.");
                                o.Qty = (o.Qty * splitFactor);
                                o.EntryPrice /= splitFactor;
                                o.HighPriceSinceEntry /= splitFactor;
                                newClose = previousClose / splitFactor;
                            }
                        }
                    }

                    o.CurrentPrice = newClose;
                    if (o.CurrentPrice > o.HighPriceSinceEntry) o.HighPriceSinceEntry = o.CurrentPrice;
                }
            }

            res.SystemicCrashActive = DetectSystemicCrash(config, allBars, symbolIndices, systemicUniverse);
            HandleRecoveryPhase(config, state, res.SystemicCrashActive);

            if (CheckGlobalProtections(config, state, res, allBars, symbolIndices))
            {
                return res;
            }

            Dictionary<string, List<CrashCatcherOrder>> activeBySymbol = state.ActiveOrders.GroupBy(o => o.Symbol)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (currentUniverse != null && currentUniverse.Count > 0)
            {
                ProcessExits(today, config, res, allBars, symbolIndices, activeBySymbol, currentUniverse);

                double remainingExposure = 0;
                foreach (CrashCatcherOrder o in state.ActiveOrders)
                {
                    if (res.Actions.Any(a => a.Symbol == o.Symbol && a.Type == ActionType.Liquidate)) continue;
                    if (res.Actions.Any(a => a.TargetOrder == o && a.Type == ActionType.Sell)) continue;

                    double qty = o.Qty;
                    TradingAction? partialSellAction =
                        res.Actions.FirstOrDefault(a => a.TargetOrder == o && a.Type == ActionType.PartialSell);
                    if (partialSellAction != null)
                    {
                        qty -= partialSellAction.Quantity;
                    }

                    if (qty > 0)
                    {
                        remainingExposure += qty * o.CurrentPrice;
                    }
                }

                double targetLev = config.LeverageMultiplier;
                if (symbolIndices.TryGetValue("SPY", out int spyIdx) &&
                    allBars.TryGetValue("SPY", out List<Bars>? spyBars))
                {
                    double spyAtr = GetAtr("SPY", spyBars, spyIdx);
                    double spyPrice = spyBars[spyIdx].Close;
                    double spyVolPct = spyAtr / spyPrice;
                    double volMultiplier = (spyVolPct > 0) ? (config.VolatilityTarget / spyVolPct) : 1.0;
                    volMultiplier = Math.Clamp(volMultiplier, config.VolatilityMinMultiplier, 1);
                    targetLev *= volMultiplier;
                }

                if (state.RecoveryDaysRemaining > 0)
                {
                    targetLev *= config.RecoveryLeverageMultiplier;
                }

                double limit = config.UseNormalizedLeverage ? config.MaxLeverageLimit : (targetLev * 1.05);

                if (state.TotalEquity > 0 && remainingExposure > state.TotalEquity * limit)
                {
                    double toReduce = remainingExposure - (state.TotalEquity * limit);
                    List<(CrashCatcherOrder order, double remainingQty, double remainingExp)> candidates = new();
                    foreach (CrashCatcherOrder o in state.ActiveOrders)
                    {
                        if (res.Actions.Any(a => a.Symbol == o.Symbol && a.Type == ActionType.Liquidate)) continue;
                        if (res.Actions.Any(a => a.TargetOrder == o && a.Type == ActionType.Sell)) continue;

                        double qty = o.Qty;
                        TradingAction? partialSellAction =
                            res.Actions.FirstOrDefault(a => a.TargetOrder == o && a.Type == ActionType.PartialSell);
                        if (partialSellAction != null)
                        {
                            qty -= partialSellAction.Quantity;
                        }

                        if (qty > 0.0001)
                        {
                            candidates.Add((o, qty, qty * o.CurrentPrice));
                        }
                    }

                    double totalCandidateExposure = candidates.Sum(c => c.remainingExp);
                    if (totalCandidateExposure > 0)
                    {
                        foreach (var c in candidates)
                        {
                            double ratio = c.remainingExp / totalCandidateExposure;
                            double amtToSell = toReduce * ratio;
                            double qtyToSell = amtToSell / c.order.CurrentPrice;

                            if (!config.UseFractionalShares)
                            {
                                qtyToSell = Math.Ceiling(qtyToSell);
                            }

                            if (qtyToSell > c.remainingQty)
                            {
                                qtyToSell = c.remainingQty;
                                amtToSell = qtyToSell * c.order.CurrentPrice;
                            }

                            if (qtyToSell > 0.0001)
                            {
                                res.Actions.Add(new TradingAction
                                {
                                    Symbol = c.order.Symbol,
                                    Type = ActionType.PartialSell,
                                    Price = c.order.CurrentPrice,
                                    Quantity = qtyToSell,
                                    Amount = amtToSell,
                                    Reason = config.UseNormalizedLeverage ? "DeleverageDrawdown" : "DeleverageStandard",
                                    TargetOrder = c.order
                                });
                            }
                        }
                    }
                }

                if (!res.SystemicCrashActive && isSpyRegimeSafe)
                {
                    ProcessEntries(today, config, state, res, allBars, symbolIndices, currentUniverse, activeBySymbol,
                        sectorMap);
                }
            }

            if (state.ActiveOrders.Count == 0 && state.RecoveryDaysRemaining == 0 &&
                !res.CircuitBreakerTriggered && !res.SystemicCrashActive)
            {
                bool hadLiquidations = res.Actions.Any(a => a.Type == ActionType.Liquidate);
                if (hadLiquidations)
                {
                    state.RecoveryDaysRemaining = config.RecoveryDurationDays;
                }
            }

            return res;
        }

        public void UpdateDailyMetrics(CrashCatcherDailyState state, DateTime date, double prevNav, bool systemicActive,
            int buys, int sells)
        {
            state.Date = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
            state.SystemicCrashActive = systemicActive;
            state.DailyReturn = (prevNav > 0) ? (state.TotalEquity - prevNav) / prevNav : 0;
            state.BuyActionsToday = buys;
            state.SellActionsToday = sells;
            state.UpdatedAt = DateTime.UtcNow;

            if (state.TotalEquity > state.PeakEquity)
            {
                state.PeakEquity = state.TotalEquity;
            }

            double dd = (state.PeakEquity > 0) ? (state.TotalEquity - state.PeakEquity) / state.PeakEquity : 0;
            state.CurrentDrawdownPct = dd * 100;
            state.MaxDrawdown = Math.Min(state.MaxDrawdown, dd);

            double expo = state.ActiveOrders.Sum(o => o.Qty * o.CurrentPrice);
            state.TotalExposure = expo;
            state.TotalEquity = state.Cash + expo;
            state.ActiveSymbolCount = state.ActiveOrders.Select(o => o.Symbol).Distinct().Count();
            state.UnrealizedPnl = state.ActiveOrders.Sum(o => (o.CurrentPrice - o.EntryPrice) * o.Qty);
            state.EffectiveLeverage = (state.TotalEquity > 0) ? expo / state.TotalEquity : 0;
        }

        private bool DetectSystemicCrash(CrashCatcherMstpConfiguration config, Dictionary<string, List<Bars>> all,
            Dictionary<string, int> idxs, List<string> pool)
        {
            int validDataCount = 0;
            int crashes = 0;

            foreach (string s in pool)
            {
                if (idxs.TryGetValue(s, out int idx) && idx >= config.MomentumLookback)
                {
                    validDataCount++;
                    if (IsMomentumCrash(s, config.MomentumLookback, config.SystemicMomentumFactor, all, idxs))
                    {
                        crashes++;
                    }
                }
            }

            if (validDataCount == 0) return false;
            return (double) crashes / validDataCount >= config.SystemicCrashThreshold;
        }

        private void HandleRecoveryPhase(CrashCatcherMstpConfiguration config, CrashCatcherDailyState state,
            bool isKrach)
        {
            if (state.SystemicCrashActive && !isKrach) state.RecoveryDaysRemaining = config.RecoveryDurationDays;
            else if (state.RecoveryDaysRemaining > 0) state.RecoveryDaysRemaining--;
            state.SystemicCrashActive = isKrach;
        }

        private bool CheckGlobalProtections(CrashCatcherMstpConfiguration config, CrashCatcherDailyState state,
            EngineResult result, Dictionary<string, List<Bars>> all, Dictionary<string, int> idxs)
        {
            if (config.CircuitBreakerPct > 0 && state.PeakEquity > 0 &&
                state.TotalEquity <= state.PeakEquity * config.CircuitBreakerPct)
            {
                result.CircuitBreakerTriggered = true;
                LiquidateAll(result, all, idxs, state, "CircuitBreaker");
                state.RecoveryDaysRemaining = config.RecoveryDurationDays;
                state.PeakEquity = state.TotalEquity;
                return false;
            }

            if (config.TrailingEquityStopPct > 0 && state.PeakEquity > 0 &&
                state.TotalEquity <= state.PeakEquity * (1.0 - config.TrailingEquityStopPct))
            {
                LiquidateAll(result, all, idxs, state, "TrailingEquityStop");
                state.RecoveryDaysRemaining = config.RecoveryDurationDays;
                state.PeakEquity = state.TotalEquity;
                return true;
            }

            return false;
        }

        private void ProcessExits(DateTime day, CrashCatcherMstpConfiguration config, EngineResult res,
            Dictionary<string, List<Bars>> all, Dictionary<string, int> idxs,
            Dictionary<string, List<CrashCatcherOrder>> active, List<string> currentUniverse)
        {
            HashSet<string> topSymbols =
                currentUniverse?.Take(config.UniverseSize).ToHashSet() ?? new HashSet<string>();

            foreach (KeyValuePair<string, List<CrashCatcherOrder>> kvp in active)
            {
                string sym = kvp.Key;
                if (!idxs.TryGetValue(sym, out int idx)) continue;
                double price = all[sym][idx].Close;
                List<CrashCatcherOrder> orders = kvp.Value;
                double atr = GetAtr(sym, all[sym], idx);

                foreach (CrashCatcherOrder o in orders)
                {
                    o.HighPriceSinceEntry = Math.Max(o.HighPriceSinceEntry, price);
                    double peak = o.HighPriceSinceEntry;

                    bool exitSl = price <= (o.EntryPrice * (1 - config.SymbolStopLossPct));

                    bool exitTs = config.AtrTrailingStopMultiplier > 0 &&
                                  price <= (peak - (atr * config.AtrTrailingStopMultiplier));

                    double tpThreshold = config.TargetProfitFinalPct;
                    if (config.ExitTrancheAtStageThreshold && config.Stages is {Count: > 0})
                    {
                        int trancheIdx = Math.Min(o.TrancheIndex, config.Stages.Count - 1);
                        tpThreshold = config.Stages[trancheIdx].ProfitThresholdPct;
                    }

                    bool exitTpFinal = price >= (o.EntryPrice * (1 + tpThreshold));

                    bool exitRotation = !config.DisableRotationExit && !topSymbols.Contains(sym);

                    if (exitSl || exitTs || exitTpFinal || exitRotation)
                    {
                        string reason = exitSl ? "SL" : (exitTs ? "TS" : (exitTpFinal ? "TPFinal" : "Rotation"));
                        res.Actions.Add(new TradingAction
                        {
                            Symbol = sym,
                            Type = ActionType.Sell,
                            Price = price,
                            Quantity = o.Qty,
                            Amount = o.Qty * price,
                            Reason = reason,
                            TargetOrder = o
                        });
                    }
                    else if (!config.ExitTrancheAtStageThreshold && config.Stages != null && config.Stages.Count > 0)
                    {
                        for (int i = 0; i < config.Stages.Count; i++)
                        {
                            if (i > (o.LastStageReached - 1) &&
                                price >= o.EntryPrice * (1 + config.Stages[i].ProfitThresholdPct))
                            {
                                double qtyToSell = config.UseFractionalShares
                                    ? o.Qty * config.Stages[i].SellRatio
                                    : Math.Floor(o.Qty * config.Stages[i].SellRatio);
                                if (config.UseFractionalShares ? qtyToSell > 0 : qtyToSell >= 1)
                                {
                                    res.Actions.Add(new TradingAction
                                    {
                                        Symbol = sym, Type = ActionType.PartialSell,
                                        Price = price, Quantity = qtyToSell,
                                        Reason = $"MSTP_{i + 1}", TargetOrder = o,
                                        NewStage = i + 1
                                    });
                                }
                                else
                                {
                                    o.LastStageReached = i + 1;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ProcessEntries(DateTime day, CrashCatcherMstpConfiguration config, CrashCatcherDailyState state,
            EngineResult res, Dictionary<string, List<Bars>> all, Dictionary<string, int> idxs, List<string> universe,
            Dictionary<string, List<CrashCatcherOrder>> active, Dictionary<string, string>? sectorMap = null)
        {
            if (state.TotalEquity <= 0) return;

            double currentLev = config.LeverageMultiplier;
            double spyAtr = GetAtr("SPY", all["SPY"], idxs["SPY"]);
            double spyPrice = all["SPY"][idxs["SPY"]].Close;
            double spyVolPct = spyAtr / spyPrice;
            double volMultiplier = (spyVolPct > 0) ? (config.VolatilityTarget / spyVolPct) : 1.0;
            volMultiplier = Math.Clamp(volMultiplier, config.VolatilityMinMultiplier, 1);
            currentLev *= volMultiplier;

            if (state.RecoveryDaysRemaining > 0)
            {
                currentLev *= config.RecoveryLeverageMultiplier;
            }

            double totalWeightPerSymbol = 0;
            for (int i = 0; i < config.MaxTranchesPerSymbol; i++)
            {
                if (config.TrancheWeights != null && config.TrancheWeights.Count > i)
                    totalWeightPerSymbol += config.TrancheWeights[i];
                else
                    totalWeightPerSymbol += Math.Pow(config.ScalingRatio, i);
            }

            double baseRateRaw = currentLev / (config.UniverseSize * totalWeightPerSymbol);

            HashSet<string> topSymbols = universe.Take(config.UniverseSize).ToHashSet();
            HashSet<string> heldSymbols = state.ActiveOrders.Select(o => o.Symbol).Distinct().ToHashSet();

            foreach (string sym in topSymbols)
            {
                if (state.DynamicBlacklist.TryGetValue(sym, out DateTime bEnd) && day < bEnd) continue;
                if (!heldSymbols.Contains(sym) && heldSymbols.Count < config.UniverseSize)
                {
                    if (!idxs.TryGetValue(sym, out int idx)) continue;
                    double price = all[sym][idx].Close;

                    if (idx >= config.WindowSize)
                    {
                        double peak = 0;
                        for (int k = 0; k < config.WindowSize; k++)
                        {
                            if (all[sym][idx - k].High > peak) peak = all[sym][idx - k].High;
                        }

                        double requiredDrop = state.RecoveryDaysRemaining > 0
                            ? 0.0
                            : config.InitialDropPct;

                        if (price > peak * (1 - requiredDrop))
                        {
                            continue;
                        }
                    }

                    if (sectorMap != null && sectorMap.TryGetValue(sym, out string? sector))
                    {
                        int sectorCount = 0;
                        foreach (var symHeld in heldSymbols)
                        {
                            if (sectorMap.TryGetValue(symHeld, out string? sHeld) && sHeld == sector)
                            {
                                sectorCount++;
                            }
                        }
                    }

                    if (config.MaxCorrelationThreshold < 1.0 && heldSymbols.Count > 0)
                    {
                        bool correlationTooHigh = false;
                        foreach (var symHeld in heldSymbols)
                        {
                            double corr = CalculateCorrelation(sym, symHeld, config.CorrelationLookback, all, idxs);
                            if (corr > config.MaxCorrelationThreshold)
                            {
                                correlationTooHigh = true;
                                break;
                            }
                        }

                        if (correlationTooHigh)
                        {
                            continue;
                        }
                    }

                    double weight = (config.TrancheWeights != null && config.TrancheWeights.Count > 0)
                        ? config.TrancheWeights[0]
                        : 1.0;
                    double amt = (state.TotalEquity * baseRateRaw) * weight;

                    if (!config.UseFractionalShares && price > state.TotalEquity)
                    {
                        continue;
                    }

                    res.Actions.Add(new TradingAction
                    {
                        Symbol = sym, Type = ActionType.Buy,
                        Price = price, Amount = amt,
                        Quantity = config.UseFractionalShares ? amt / price : Math.Ceiling(amt / price),
                        Reason = "Entry1",
                        TrancheIndex = 0
                    });
                    heldSymbols.Add(sym);
                }
            }

            Dictionary<string, List<CrashCatcherOrder>> activeBySymbol =
                state.ActiveOrders.GroupBy(o => o.Symbol).ToDictionary(g => g.Key, g => g.ToList());
            foreach (KeyValuePair<string, List<CrashCatcherOrder>> kvp in activeBySymbol)
            {
                string sym = kvp.Key;
                if (!idxs.TryGetValue(sym, out int idx)) continue;
                double price = all[sym][idx].Close;
                List<CrashCatcherOrder> orders = kvp.Value;

                int maxTrancheIndex = orders.Max(o => o.TrancheIndex);
                int nextTrancheIndex = maxTrancheIndex + 1;

                if (nextTrancheIndex < config.MaxTranchesPerSymbol)
                {
                    double lastPrice = orders.Last().EntryPrice;
                    DateTime lastEntryDate = orders.Last().EntryDate;

                    int daysSinceLastTranche;
                    int lastEntryIdx = all[sym].FindLastIndex(b => b.Timestamp.Date <= lastEntryDate.Date);
                    if (lastEntryIdx != -1)
                    {
                        daysSinceLastTranche = idx - lastEntryIdx;
                    }
                    else
                    {
                        daysSinceLastTranche = (day.Date - lastEntryDate.Date).Days;
                    }

                    bool canDca = price <= lastPrice * (1 - config.GridSpacingPct) &&
                                  topSymbols.Contains(sym) &&
                                  daysSinceLastTranche >= config.MinDaysBetweenTranches;

                    if (canDca)
                    {
                        double weight =
                            (config.TrancheWeights != null && config.TrancheWeights.Count > nextTrancheIndex)
                                ? config.TrancheWeights[nextTrancheIndex]
                                : Math.Pow(config.ScalingRatio, nextTrancheIndex);

                        double amt = (state.TotalEquity * baseRateRaw) * weight;

                        if (!config.UseFractionalShares && price > state.TotalEquity)
                        {
                            continue;
                        }

                        res.Actions.Add(new TradingAction
                        {
                            Symbol = sym, Type = ActionType.Buy,
                            Price = price, Amount = amt,
                            Quantity = config.UseFractionalShares ? amt / price : Math.Ceiling(amt / price),
                            Reason = $"DCA{nextTrancheIndex + 1}",
                            TrancheIndex = nextTrancheIndex
                        });
                    }
                }
            }
        }

        public void ApplyActions(CrashCatcherDailyState state, EngineResult result, DateTime date, double slippage)
        {
            state.ClosedToday ??= new List<CrashCatcherClosedOrder>();
            state.ClosedToday.Clear();
            state.DailyRealizedPnl = 0;

            foreach (TradingAction act in result.Actions)
            {
                if (act.Type == ActionType.Liquidate)
                {
                    List<CrashCatcherOrder> oList = state.ActiveOrders.Where(o => o.Symbol == act.Symbol).ToList();
                    foreach (var o in oList)
                    {
                        double exitCash = o.Qty * act.Price * (1 - slippage);
                        double entryCost = o.Qty * o.EntryPrice;
                        double realized = exitCash - entryCost;

                        state.ClosedToday.Add(new CrashCatcherClosedOrder
                        {
                            Symbol = o.Symbol, Qty = o.Qty, EntryPrice = o.EntryPrice, ExitPrice = act.Price,
                            EntryDate = o.EntryDate, ExitDate = date, RealizedPnl = realized,
                            GainPct = (act.Price / o.EntryPrice) - 1, ExitReason = act.Reason
                        });

                        state.Cash += exitCash;
                        state.DailyRealizedPnl += realized;
                        state.TotalRealizedPnl += realized;
                        state.ActiveOrders.Remove(o);
                    }
                }
                else if (act.Type == ActionType.Sell)
                {
                    if (act.TargetOrder != null && state.ActiveOrders.Contains(act.TargetOrder))
                    {
                        double exitCash = act.TargetOrder.Qty * act.Price * (1 - slippage);
                        double entryCost = act.TargetOrder.Qty * act.TargetOrder.EntryPrice;
                        double realized = exitCash - entryCost;

                        state.ClosedToday.Add(new CrashCatcherClosedOrder
                        {
                            Symbol = act.Symbol, Qty = act.TargetOrder.Qty, EntryPrice = act.TargetOrder.EntryPrice,
                            ExitPrice = act.Price, EntryDate = act.TargetOrder.EntryDate, ExitDate = date,
                            RealizedPnl = realized, GainPct = (act.Price / act.TargetOrder.EntryPrice) - 1,
                            ExitReason = act.Reason
                        });

                        state.Cash += exitCash;
                        state.DailyRealizedPnl += realized;
                        state.TotalRealizedPnl += realized;
                        state.ActiveOrders.Remove(act.TargetOrder);
                    }
                }
                else if (act.Type == ActionType.PartialSell)
                {
                    if (act.TargetOrder != null && state.ActiveOrders.Contains(act.TargetOrder))
                    {
                        double soldQty = act.Quantity;
                        double exitCash = soldQty * act.Price * (1 - slippage);
                        double entryCost = soldQty * act.TargetOrder.EntryPrice;
                        double realized = exitCash - entryCost;

                        state.ClosedToday.Add(new CrashCatcherClosedOrder
                        {
                            Symbol = act.Symbol, Qty = soldQty, EntryPrice = act.TargetOrder.EntryPrice,
                            ExitPrice = act.Price, EntryDate = act.TargetOrder.EntryDate, ExitDate = date,
                            RealizedPnl = realized, GainPct = (act.Price / act.TargetOrder.EntryPrice) - 1,
                            ExitReason = act.Reason
                        });

                        act.TargetOrder.Qty -= soldQty;
                        if (act.NewStage.HasValue)
                        {
                            act.TargetOrder.LastStageReached =
                                Math.Max(act.TargetOrder.LastStageReached, act.NewStage.Value);
                        }

                        state.Cash += exitCash;
                        state.DailyRealizedPnl += realized;
                        state.TotalRealizedPnl += realized;
                    }
                }
                else if (act.Type == ActionType.Buy)
                {
                    state.Cash -= act.Quantity * act.Price * (1 + slippage);
                    state.ActiveOrders.Add(new CrashCatcherOrder
                    {
                        Symbol = act.Symbol, Qty = act.Quantity, EntryPrice = act.Price,
                        HighPriceSinceEntry = act.Price, EntryDate = date, CurrentPrice = act.Price,
                        TrancheIndex = act.TrancheIndex
                    });
                    state.LastEntryDates[act.Symbol] = date;
                }
            }

            state.ActiveOrders.RemoveAll(o => o.Qty <= 0.0001);
            state.RealizedEquity = state.InitialCash + state.TotalRealizedPnl;

            double mtm = state.ActiveOrders.Sum(o => o.Qty * o.CurrentPrice);
            state.TotalEquity = state.Cash + mtm;
        }

        private bool IsMomentumCrash(string s, int lookback, double thr, Dictionary<string, List<Bars>> all,
            Dictionary<string, int> idxs)
        {
            if (!idxs.TryGetValue(s, out int idx) || idx < lookback)
            {
                return false;
            }

            Bars firstBar = all[s][idx];
            Bars secondBar = all[s][idx - lookback];
            return firstBar.Close <= secondBar.Close * thr;
        }

        private double GetAtr(string sym, List<Bars> bars, int idx)
        {
            if (idx < 14) return bars[idx].Close * 0.05;
            double sum = 0;
            for (int i = 0; i < 14; i++)
            {
                Bars bar = bars[idx - i];
                Bars prev = bars[idx - i - 1];
                double tr = Math.Max(bar.High - bar.Low,
                    Math.Max(Math.Abs(bar.High - prev.Close),
                        Math.Abs(bar.Low - prev.Close)));
                sum += tr;
            }

            return sum / 14;
        }

        private void LiquidateAll(EngineResult res, Dictionary<string, List<Bars>> all, Dictionary<string, int> idxs,
            CrashCatcherDailyState st, string r)
        {
            foreach (IGrouping<string, CrashCatcherOrder> g in st.ActiveOrders.GroupBy(o => o.Symbol))
            {
                if (idxs.TryGetValue(g.Key, out int idx))
                    res.Actions.Add(new TradingAction
                    {
                        Symbol = g.Key, Type = ActionType.Liquidate, Price = all[g.Key][idx].Close, Reason = r,
                        Quantity = g.Sum(o => o.Qty)
                    });
            }
        }

        private double CalculateCorrelation(string s1, string s2, int lookback, Dictionary<string, List<Bars>> all,
            Dictionary<string, int> idxs)
        {
            if (!idxs.TryGetValue(s1, out int i1) || !idxs.TryGetValue(s2, out int i2) || i1 < lookback ||
                i2 < lookback)
                return 0;

            double[] r1 = new double[lookback];
            double[] r2 = new double[lookback];

            for (int k = 0; k < lookback; k++)
            {
                r1[k] = (all[s1][i1 - k].Close - all[s1][i1 - k - 1].Close) / all[s1][i1 - k - 1].Close;
                r2[k] = (all[s2][i2 - k].Close - all[s2][i2 - k - 1].Close) / all[s2][i2 - k - 1].Close;
            }

            double avg1 = r1.Average();
            double avg2 = r2.Average();

            double sumSq1 = 0, sumSq2 = 0, sumCo = 0;
            for (int k = 0; k < lookback; k++)
            {
                double d1 = r1[k] - avg1;
                double d2 = r2[k] - avg2;
                sumSq1 += d1 * d1;
                sumSq2 += d2 * d2;
                sumCo += d1 * d2;
            }

            double den = Math.Sqrt(sumSq1 * sumSq2);
            return den == 0 ? 0 : sumCo / den;
        }
    }

    // ==========================================================================================
    // CCTRADER ROBOT MAIN CLASS
    // ==========================================================================================
    [Robot(AccessRights = AccessRights.FullAccess, AddIndicators = true)]
    public class UnicornTraiding : Robot
    {
        [Parameter("Mongo Conn String", Group = "MongoDB", DefaultValue = "mongodb://dataestimate:OtG4j93VHWsQLbXZYCai2fLjqnohmn@31.37.184.68:27017/?directConnection=true")]
        public string MongoConnectionString { get; set; }

        [Parameter("Database Name", Group = "MongoDB", DefaultValue = "SwingTradingAccumulation")]
        public string DatabaseName { get; set; }

        [Parameter("Collection Suffix", Group = "MongoDB", DefaultValue = "_cTrader_LIVE")]
        public string CollectionSuffix { get; set; }

        [Parameter("Position Label", Group = "Strategy", DefaultValue = "UnicornMstp")]
        public string PositionLabel { get; set; }

        [Parameter("Trigger Hour EST (MOC)", Group = "Schedule", DefaultValue = 15)]
        public int TriggerHour { get; set; }

        [Parameter("Trigger Minute EST (MOC)", Group = "Schedule", DefaultValue = 50)]
        public int TriggerMinute { get; set; }

        [Parameter("Symbol Prefix", Group = "Broker Settings", DefaultValue = "")]
        public string SymbolPrefix { get; set; }

        [Parameter("Symbol Suffix", Group = "Broker Settings", DefaultValue = "")]
        public string SymbolSuffix { get; set; }

        [Parameter("SPY Symbol Override", Group = "Broker Settings", DefaultValue = "SPY")]
        public string SpySymbolOverride { get; set; }

        private IMongoDatabase _database;
        private IMongoCollection<BsonDocument> _universeHistoryCollection;
        private IMongoCollection<BsonDocument> _assetInfoCollection;
        private IMongoCollection<CrashCatcherDailyState> _stateCollection;

        private bool _mocProcessedForToday = false;
        private DateTime _lastResetDate = DateTime.MinValue;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private readonly CrashCatcherMstpEngine _engine = new();

        private readonly HashSet<string> _availableBrokerSymbols = new(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeZoneInfo EasternTimeZone = GetEasternTimeZone();

        private static TimeZoneInfo GetEasternTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
        }

        private static DateTime GetEasternNow()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EasternTimeZone);
        }

        protected override void OnStart()
        {
            Print("[UnicornTrading cTrader] Initializing...");

            // Cache available symbol names from broker
            try
            {
                foreach (var symbol in Symbols)
                {
                    if (!string.IsNullOrEmpty(symbol))
                    {
                        _availableBrokerSymbols.Add(symbol);
                    }
                }
                Print($"[UnicornTrading cTrader] Loaded {_availableBrokerSymbols.Count} available symbols from broker.");
            }
            catch (Exception ex)
            {
                Print("[UnicornTrading cTrader] Failed to cache broker symbols: " + ex.Message);
            }

            try
            {
                var client = new MongoClient(MongoConnectionString);
                _database = client.GetDatabase(DatabaseName);
                _universeHistoryCollection = _database.GetCollection<BsonDocument>("UnicornUniverseHistory");
                _assetInfoCollection = _database.GetCollection<BsonDocument>("AssetInformation");
                _stateCollection = _database.GetCollection<CrashCatcherDailyState>("CrashCatcherDailyState" + CollectionSuffix);

                Print("[UnicornTrading cTrader] Connected to MongoDB successfully.");
            }
            catch (Exception ex)
            {
                Print("[UnicornTrading cTrader] MongoDB connection failed: " + ex.Message);
            }

            // Perform startup verification checks
            PerformStartupVerification();

            // Start 10-second scheduler timer
            Timer.Start(TimeSpan.FromSeconds(10));
        }

        protected override void OnTimer()
        {
            try
            {
                var nowEst = GetEasternNow();

                // Heartbeat every 5 minutes
                if (DateTime.UtcNow - _lastHeartbeatUtc >= TimeSpan.FromMinutes(5))
                {
                    _lastHeartbeatUtc = DateTime.UtcNow;
                    Print(string.Format("[UnicornTrading cTrader] Heartbeat - Machine Local: {0:HH:mm:ss} | Machine UTC: {1:HH:mm:ss} | Market EST: {2:HH:mm:ss}",
                        DateTime.Now, DateTime.UtcNow, nowEst));
                }

                // Daily Reset of flags
                if (nowEst.Date != _lastResetDate)
                {
                    _mocProcessedForToday = false;
                    _lastResetDate = nowEst.Date;
                    Print("[UnicornTrading cTrader] Resetting execution flags for date: " + nowEst.Date.ToString("yyyy-MM-dd"));
                }

                // Standard trading hours check: Mon-Fri only
                if (nowEst.DayOfWeek == DayOfWeek.Saturday || nowEst.DayOfWeek == DayOfWeek.Sunday)
                {
                    return;
                }

                // Trigger MOC logic (typically 15:50 EST)
                if (!_mocProcessedForToday && nowEst.Hour == TriggerHour && nowEst.Minute >= TriggerMinute && nowEst.Minute < TriggerMinute + 8)
                {
                    Print(string.Format("[UnicornTrading cTrader] MOC trigger reached. EST: {0:HH:mm:ss}", nowEst));
                    _mocProcessedForToday = true;
                    
                    // Run MOC logic on the main thread (required for ExecuteMarketOrder/ClosePosition)
                    BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            ProcessMocLogic(nowEst.Date);
                        }
                        catch (Exception ex)
                        {
                            Print("[UnicornTrading cTrader] ProcessMocLogic Error: " + ex.Message);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Print("[UnicornTrading cTrader] Scheduler Error: " + ex.Message);
            }
        }

        private string GetBrokerSymbol(string standardSymbol)
        {
            // Special resolution for SPY (S&P 500 Index)
            if (standardSymbol == "SPY")
            {
                // Try override first if specified
                if (!string.IsNullOrEmpty(SpySymbolOverride) && _availableBrokerSymbols.Contains(SpySymbolOverride))
                {
                    return SpySymbolOverride;
                }

                // Check exact SPY match first
                if (_availableBrokerSymbols.Contains("SPY"))
                {
                    return "SPY";
                }

                // Try common index CFD names
                string[] spyFallbacks = { "US500", "SPX500", "USA500", "SPX", "US 500", "S&P500", "US500.cfd", "#US500" };
                foreach (var fallback in spyFallbacks)
                {
                    if (_availableBrokerSymbols.Contains(fallback))
                    {
                        return fallback;
                    }
                }

                // Scan for any available symbol containing "500" or "SPX"
                string dynamicFallback = _availableBrokerSymbols.FirstOrDefault(s => s.Contains("500") || s.Contains("SPX", StringComparison.OrdinalIgnoreCase));
                if (dynamicFallback != null)
                {
                    return dynamicFallback;
                }
            }

            // 1. Check with configured prefix/suffix if defined and exists
            if (!string.IsNullOrEmpty(SymbolPrefix) || !string.IsNullOrEmpty(SymbolSuffix))
            {
                string configuredName = SymbolPrefix + standardSymbol + SymbolSuffix;
                if (_availableBrokerSymbols.Contains(configuredName))
                {
                    return configuredName;
                }
            }

            // 2. Check exact match
            if (_availableBrokerSymbols.Contains(standardSymbol))
            {
                return standardSymbol;
            }

            // 3. Auto-resolve common suffixes
            string[] commonSuffixes = { ".US", ".US-Cash", "-Cash", ".m", ".uk", ".de" };
            foreach (var suffix in commonSuffixes)
            {
                string candidate = standardSymbol + suffix;
                if (_availableBrokerSymbols.Contains(candidate))
                {
                    return candidate;
                }
            }

            // 4. Try case-insensitive lookup
            string found = _availableBrokerSymbols.FirstOrDefault(s => s.Equals(standardSymbol, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return found;
            }

            // Fallback: return configured format
            return SymbolPrefix + standardSymbol + SymbolSuffix;
        }

        private string GetStandardSymbol(string brokerSymbol)
        {
            if (brokerSymbol == SpySymbolOverride)
            {
                return "SPY";
            }
            string result = brokerSymbol;
            if (!string.IsNullOrEmpty(SymbolPrefix) && result.StartsWith(SymbolPrefix))
            {
                result = result.Substring(SymbolPrefix.Length);
            }
            if (!string.IsNullOrEmpty(SymbolSuffix) && result.EndsWith(SymbolSuffix))
            {
                result = result.Substring(0, result.Length - SymbolSuffix.Length);
                return result;
            }

            // Auto strip common suffixes
            string[] commonSuffixes = { ".US", ".US-Cash", "-Cash", ".m", ".uk", ".de" };
            foreach (var suffix in commonSuffixes)
            {
                if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    break;
                }
            }
            return result;
        }

        private void ProcessMocLogic(DateTime today)
        {
            Print("[UnicornTrading cTrader] Running MOC Logic...");

            try
            {
                // 1. Get latest state
                CrashCatcherDailyState latestState = GetLatestState();
                if (latestState == null)
                {
                    double initialBalance = Account.Balance;
                    Print($"[UnicornTrading cTrader] No previous state found. Initializing with account balance: {initialBalance:N2}");
                    latestState = new CrashCatcherDailyState
                    {
                        Date = today.AddDays(-1),
                        Cash = initialBalance,
                        InitialCash = initialBalance,
                        PeakEquity = initialBalance,
                        PeakRealizedEquity = initialBalance,
                        TotalEquity = initialBalance
                    };
                }

                // Protect against double processing
                if (latestState.Date.Date >= today.Date)
                {
                    Print($"[UnicornTrading cTrader] Today {today:yyyy-MM-dd} already processed (last state: {latestState.Date:yyyy-MM-dd}). Aborting.");
                    return;
                }

                CrashCatcherMstpConfiguration config = new CrashCatcherMstpConfiguration();

                // 2. Load universe and sector maps
                Print("[UnicornTrading cTrader] Loading universe history and sector map from MongoDB...");
                var universeDocs = _universeHistoryCollection.Find(Builders<BsonDocument>.Filter.Empty).ToList();
                var universeHistoryMap = new SortedDictionary<DateTime, List<string>>();
                foreach (var doc in universeDocs)
                {
                    if (doc.Contains("Date") && doc.Contains("TopSymbols"))
                    {
                        DateTime dateVal = doc["Date"].ToUniversalTime().Date;
                        var topSymbols = doc["TopSymbols"].AsBsonArray.Select(s => s.AsString).ToList();
                        universeHistoryMap[dateVal] = topSymbols;
                    }
                }

                var assetDocs = _assetInfoCollection.Find(Builders<BsonDocument>.Filter.Empty).ToList();
                var sectorMap = new Dictionary<string, string>();
                foreach (var doc in assetDocs)
                {
                    if (doc.Contains("Symbol") && doc.Contains("Sector"))
                    {
                        string symbol = doc["Symbol"].AsString;
                        string sector = doc["Sector"].AsString;
                        sectorMap[symbol] = sector;
                    }
                }

                // 3. Compile list of symbols
                var symbolsToFetch = new HashSet<string> { "SPY" };
                foreach (var h in universeHistoryMap.Values)
                {
                    foreach (var s in h) symbolsToFetch.Add(s);
                }
                foreach (var o in latestState.ActiveOrders)
                {
                    symbolsToFetch.Add(o.Symbol);
                }

                // 4. Fetch daily bars from cTrader
                Print($"[UnicornTrading cTrader] Loading historical bars for {symbolsToFetch.Count} symbols from cTrader...");
                var barsCache = new Dictionary<string, List<Bars>>();
                foreach (var standardSym in symbolsToFetch)
                {
                    string brokerSym = GetBrokerSymbol(standardSym);
                    if (!_availableBrokerSymbols.Contains(brokerSym)) continue;
                    var symbolInfo = Symbols.GetSymbol(brokerSym);
                    if (symbolInfo == null) continue;

                    var ctraderBars = MarketData.GetBars(TimeFrame.Daily, brokerSym);
                    if (ctraderBars == null || ctraderBars.Count == 0) continue;

                    var localBarsList = new List<Bars>();
                    int startIdx = Math.Max(0, ctraderBars.Count - 500);
                    for (int i = startIdx; i < ctraderBars.Count; i++)
                    {
                        localBarsList.Add(new Bars
                        {
                            Timestamp = DateTime.SpecifyKind(ctraderBars.OpenTimes[i].Date, DateTimeKind.Utc),
                            Open = ctraderBars.OpenPrices[i],
                            High = ctraderBars.HighPrices[i],
                            Low = ctraderBars.LowPrices[i],
                            Close = ctraderBars.ClosePrices[i],
                            Volume = (long)ctraderBars.TickVolumes[i]
                        });
                    }
                    barsCache[standardSym] = localBarsList;
                }

                if (!barsCache.ContainsKey("SPY"))
                {
                    Print("[UnicornTrading cTrader] Error: SPY historical data is missing. Aborting.");
                    return;
                }

                var availableDates = barsCache["SPY"]
                    .Select(b => b.Timestamp.Date)
                    .Where(d => d >= latestState.Date.Date && d <= today.Date)
                    .OrderBy(x => x)
                    .ToList();

                if (!availableDates.Any())
                {
                    Print("[UnicornTrading cTrader] No available daily dates to process. Aborting.");
                    return;
                }

                // 5. Synchronize real positions (cTrader -> State)
                Print("[UnicornTrading cTrader] Synchronizing broker positions...");
                var activePositions = Positions.FindAll(PositionLabel);
                foreach (var grp in latestState.ActiveOrders.GroupBy(o => o.Symbol))
                {
                    string sym = grp.Key;
                    var tranches = grp.ToList();
                    double stateQty = tranches.Sum(o => o.Qty);

                    string brokerSym = GetBrokerSymbol(sym);
                    var realPos = activePositions.FirstOrDefault(p => p.SymbolName == brokerSym);

                    if (realPos == null)
                    {
                        foreach (var o in tranches) o.Qty = 0;
                        continue;
                    }

                    double realQty = realPos.VolumeInUnits;
                    if (stateQty > 0 && Math.Abs(stateQty - realQty) > 0.001)
                    {
                        double scale = realQty / stateQty;
                        Print(string.Format("[UnicornTrading Sync] {0} aggregate mismatch. State: {1:F2}, cTrader: {2:F2}. Scaling tranches by {3:F4}.",
                            sym, stateQty, realQty, scale));

                        foreach (var o in tranches) o.Qty = o.Qty * scale;
                    }
                }
                latestState.ActiveOrders.RemoveAll(o => o.Qty <= 0.0001);

                // Synchronize Cash
                double brokerCash = Account.Balance;
                if (Math.Abs(brokerCash - latestState.Cash) > 0.01)
                {
                    Print(string.Format("[UnicornTrading Sync] Cash mismatch. State: {0:N2}, cTrader: {1:N2}. Aligning.",
                        latestState.Cash, brokerCash));
                    latestState.Cash = brokerCash;
                }

                latestState.TotalExposure = latestState.ActiveOrders.Sum(o => o.Qty * o.CurrentPrice);
                latestState.TotalEquity = latestState.Cash + latestState.TotalExposure;
                Print($"[UnicornTrading Sync] Cash: {latestState.Cash:N2} | Exposure: {latestState.TotalExposure:N2} | TotalEquity: {latestState.TotalEquity:N2}");

                DateTime targetExecutionDate = availableDates.Last();

                Dictionary<string, int> indices = new Dictionary<string, int>();
                foreach (var kvp in barsCache)
                {
                    for (int i = kvp.Value.Count - 1; i >= 0; i--)
                    {
                        if (kvp.Value[i].Timestamp.Date == targetExecutionDate)
                        {
                            indices[kvp.Key] = i;
                            break;
                        }
                    }
                }

                if (!indices.ContainsKey("SPY"))
                {
                    Print("[UnicornTrading cTrader] SPY data missing for target date. Aborting.");
                    return;
                }

                latestState.DailyRealizedPnl = 0;
                latestState.ClosedToday = new List<CrashCatcherClosedOrder>();

                // 6. Mark-To-Market
                double mtm = 0;
                foreach (var o in latestState.ActiveOrders)
                {
                    if (indices.TryGetValue(o.Symbol, out int idx))
                    {
                        o.CurrentPrice = barsCache[o.Symbol][idx].Close;
                        mtm += o.Qty * o.CurrentPrice;
                        if (o.CurrentPrice > o.HighPriceSinceEntry)
                        {
                            o.HighPriceSinceEntry = o.CurrentPrice;
                        }
                    }
                    else
                    {
                        o.CurrentPrice = 0;
                    }
                }

                latestState.TotalExposure = mtm;
                latestState.TotalEquity = latestState.Cash + latestState.TotalExposure;
                double prevNav = latestState.TotalEquity;

                // Load active universe (filtering out symbols not available on the broker)
                List<string> rawUniverse = universeHistoryMap.TryGetValue(targetExecutionDate.Date, out var v) ? v : new List<string>();
                List<string> currentUniverse = new List<string>();
                foreach (var sym in rawUniverse)
                {
                    string brokerSym = GetBrokerSymbol(sym);
                    if (_availableBrokerSymbols.Contains(brokerSym))
                    {
                        currentUniverse.Add(sym);
                    }
                }
                currentUniverse = currentUniverse.Take(config.UniverseSize).ToList();

                // Regime filter
                bool isSpyRegimeSafe = true;
                if (config.UseRegimeFilter && barsCache.TryGetValue("SPY", out var spyBars) && indices.TryGetValue("SPY", out int spyIdx))
                {
                    if (spyIdx >= 200)
                    {
                        double ma200 = spyBars.GetRange(spyIdx - 200, 200).Average(b => b.Close);
                        isSpyRegimeSafe = spyBars[spyIdx].Close > ma200;
                    }
                }

                List<string> systemicPool = barsCache.Keys.ToList();

                var sortedBars = barsCache.Where(kv => indices.ContainsKey(kv.Key)).ToDictionary(k => k.Key, k => k.Value);
                var sortedIndices = indices.ToDictionary(k => k.Key, k => k.Value);

                // 7. Run Engine Process
                CrashCatcherMstpEngine.EngineResult dayRes = _engine.ProcessDay(
                    targetExecutionDate, config, latestState, sortedBars, sortedIndices,
                    currentUniverse, isSpyRegimeSafe, systemicPool, sectorMap);

                List<CrashCatcherMstpEngine.TradingAction> executedActions = new();

                // 8. Execute Sells (Liquidations / Sells / Partial Sells)
                var sellActions = dayRes.Actions.Where(a => a.Type != CrashCatcherMstpEngine.ActionType.Buy).ToList();
                foreach (var action in sellActions)
                {
                    try
                    {
                        string brokerSym = GetBrokerSymbol(action.Symbol);
                        var pos = activePositions.FirstOrDefault(p => p.SymbolName == brokerSym);

                        if (pos == null)
                        {
                            Print($"[UnicornTrading Sell] Skipping exit for {action.Symbol}: cTrader position not found.");
                            continue;
                        }

                        double desiredQty = action.Type == CrashCatcherMstpEngine.ActionType.Liquidate
                            ? pos.VolumeInUnits
                            : action.Quantity;

                        double sellQty = Math.Min(pos.VolumeInUnits, desiredQty);
                        if (sellQty <= 0) continue;

                        Print(string.Format("[UnicornTrading Sell] Closing position for {0} (Qty: {1:F2}) Reason: {2}",
                            action.Symbol, sellQty, action.Reason));

                        TradeResult closeRes;
                        if (Math.Abs(sellQty - pos.VolumeInUnits) < 0.001)
                        {
                            closeRes = ClosePosition(pos);
                        }
                        else
                        {
                            closeRes = ClosePosition(pos, sellQty);
                        }

                        if (closeRes.IsSuccessful)
                        {
                            action.Quantity = sellQty;
                            executedActions.Add(action);
                        }
                        else
                        {
                            Print($"[UnicornTrading Sell] Order failed for {action.Symbol}: " + closeRes.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[UnicornTrading Sell] Exception exiting {action.Symbol}: " + ex.Message);
                    }
                }

                // 9. Execute Buys
                var buyActions = dayRes.Actions.Where(a => a.Type == CrashCatcherMstpEngine.ActionType.Buy).ToList();
                foreach (var action in buyActions)
                {
                    try
                    {
                        string brokerSym = GetBrokerSymbol(action.Symbol);
                        if (!_availableBrokerSymbols.Contains(brokerSym))
                        {
                            Print($"[UnicornTrading Buy] Error: Symbol {brokerSym} not supported by broker.");
                            continue;
                        }
                        var symbolInfo = Symbols.GetSymbol(brokerSym);
                        if (symbolInfo == null)
                        {
                            Print($"[UnicornTrading Buy] Error: Symbol {brokerSym} failed to load.");
                            continue;
                        }

                        double qty = action.Amount / action.Price;
                        double volume = symbolInfo.NormalizeVolumeInUnits(qty, RoundingMode.ToNearest);

                        if (volume < symbolInfo.VolumeInUnitsMin)
                        {
                            Print(string.Format("[UnicornTrading Buy] Skipping {0}: normalized volume {1} below broker min {2}",
                                action.Symbol, volume, symbolInfo.VolumeInUnitsMin));
                            continue;
                        }

                        Print(string.Format("[UnicornTrading Buy] Opening BUY order for {0} (Volume: {1} @ {2})",
                            action.Symbol, volume, action.Price));

                        var buyRes = ExecuteMarketOrder(TradeType.Buy, brokerSym, volume, PositionLabel);
                        if (buyRes.IsSuccessful)
                        {
                            action.Quantity = volume;
                            action.Amount = volume * action.Price;
                            executedActions.Add(action);
                        }
                        else
                        {
                            Print($"[UnicornTrading Buy] Order rejected for {action.Symbol}: " + buyRes.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        Print($"[UnicornTrading Buy] Exception buying {action.Symbol}: " + ex.Message);
                    }
                }

                // 10. Update State and Save
                dayRes.Actions = executedActions;
                int buys = executedActions.Count(a => a.Type == CrashCatcherMstpEngine.ActionType.Buy);
                int sellsAll = executedActions.Count(a => a.Type != CrashCatcherMstpEngine.ActionType.Buy);

                _engine.ApplyActions(latestState, dayRes, targetExecutionDate, config.SlippagePct);

                double mtmAfter = latestState.ActiveOrders.Sum(o => o.Qty * o.CurrentPrice);
                latestState.TotalExposure = mtmAfter;
                latestState.TotalEquity = latestState.Cash + latestState.TotalExposure;

                _engine.UpdateDailyMetrics(latestState, targetExecutionDate, prevNav, dayRes.SystemicCrashActive, buys, sellsAll);

                // Save back to Mongo
                SaveState(latestState);
                Print(string.Format("[UnicornTrading cTrader] MOC Logic Complete. NAV: {0:N2} | Positions: {1}",
                    latestState.TotalEquity, latestState.ActiveOrders.Count));
            }
            catch (Exception ex)
            {
                Print("[UnicornTrading cTrader] Critical Error in MOC logic execution: " + ex.Message);
            }
        }

        private CrashCatcherDailyState GetLatestState()
        {
            return _stateCollection.Find(Builders<CrashCatcherDailyState>.Filter.Empty)
                .SortByDescending(d => d.UpdatedAt)
                .FirstOrDefault();
        }

        private void SaveState(CrashCatcherDailyState state)
        {
            state.UpdatedAt = DateTime.UtcNow;
            var filter = Builders<CrashCatcherDailyState>.Filter.Eq(s => s.Id, state.Id);
            _stateCollection.ReplaceOne(filter, state, new ReplaceOptions { IsUpsert = true });
        }

        private void PerformStartupVerification()
        {
            Print("[UnicornTrading cTrader] Performing startup verification checks...");

            try
            {
                if (_database == null)
                {
                    Print("[Verification] FAILED: MongoDB database is not initialized.");
                    return;
                }

                // 1. Check MongoDB state retrieval
                Print("[Verification] Fetching latest state from MongoDB...");
                CrashCatcherDailyState latestState = GetLatestState();
                if (latestState == null)
                {
                    Print("[Verification] WARNING: No previous state found in MongoDB. Will initialize on trigger.");
                }
                else
                {
                    Print($"[Verification] Found state for date: {latestState.Date:yyyy-MM-dd} with {latestState.ActiveOrders.Count} active orders.");
                }

                // 2. Compile list of symbols to test
                var symbolsToTest = new HashSet<string> { "SPY" };
                
                if (latestState != null && latestState.ActiveOrders != null)
                {
                    foreach (var o in latestState.ActiveOrders)
                    {
                        if (!string.IsNullOrEmpty(o.Symbol))
                        {
                            symbolsToTest.Add(o.Symbol);
                        }
                    }
                }

                try
                {
                    var latestUniverseDoc = _universeHistoryCollection.Find(Builders<BsonDocument>.Filter.Empty)
                        .Sort(Builders<BsonDocument>.Sort.Descending("Date"))
                        .FirstOrDefault();
                    if (latestUniverseDoc != null && latestUniverseDoc.Contains("TopSymbols"))
                    {
                        var topSymbols = latestUniverseDoc["TopSymbols"].AsBsonArray.Select(s => s.AsString).ToList();
                        foreach (var s in topSymbols)
                        {
                            symbolsToTest.Add(s);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Print("[Verification] WARNING: Could not fetch latest universe history from MongoDB: " + ex.Message);
                }

                Print($"[Verification] Testing broker access for {symbolsToTest.Count} symbols...");

                int successes = 0;
                int failures = 0;

                foreach (var standardSym in symbolsToTest)
                {
                    string brokerSym = GetBrokerSymbol(standardSym);
                    if (!_availableBrokerSymbols.Contains(brokerSym))
                    {
                        Print($"[Verification] ERROR: Symbol '{standardSym}' (resolved to '{brokerSym}') is NOT supported by broker.");
                        failures++;
                        continue;
                    }
                    var symbolInfo = Symbols.GetSymbol(brokerSym);

                    if (symbolInfo == null)
                    {
                        Print($"[Verification] ERROR: Symbol '{standardSym}' (resolved to '{brokerSym}') failed to load.");
                        failures++;
                        continue;
                    }

                    var ctraderBars = MarketData.GetBars(TimeFrame.Daily, brokerSym);
                    if (ctraderBars == null || ctraderBars.Count == 0)
                    {
                        Print($"[Verification] ERROR: Found '{brokerSym}' but failed to fetch historical daily bars.");
                        failures++;
                    }
                    else
                    {
                        Print($"[Verification] SUCCESS: '{standardSym}' mapped to '{brokerSym}' (Bid: {symbolInfo.Bid}, Daily Bars: {ctraderBars.Count})");
                        successes++;
                    }
                }

                Print($"[Verification] Verification complete. Successes: {successes}/{symbolsToTest.Count}. Failures: {failures}.");

                string spyBrokerSym = GetBrokerSymbol("SPY");
                if (!_availableBrokerSymbols.Contains(spyBrokerSym))
                {
                    Print($"[Verification] CRITICAL ERROR: SPY (mapped to '{spyBrokerSym}') is not accessible in the broker database. Strategy will crash at MOC!");
                    
                    // Search for indices containing S&P, 500, SPX, SPY, USA or US
                    var indexSuggestions = new List<string>();
                    string[] keywords = { "500", "SPX", "SPY", "US500", "USA500", "US Tech 100", "NAS100", "USTEC", "US30" };
                    foreach (var s in _availableBrokerSymbols)
                    {
                        foreach (var kw in keywords)
                        {
                            if (s.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            {
                                indexSuggestions.Add(s);
                                break;
                            }
                        }
                    }

                    if (indexSuggestions.Any())
                    {
                        Print("[Verification] SUGGESTION: Your broker doesn't have SPY. You can try setting the 'SPY Symbol Override' parameter in the cBot UI to one of these available indices: " + string.Join(", ", indexSuggestions.Take(15)));
                    }
                }
            }
            catch (Exception ex)
            {
                Print("[Verification] Critical exception during verification: " + ex.Message);
            }
        }
    }
}
