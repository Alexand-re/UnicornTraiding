using System;
using System.Collections.Generic;
using System.Linq;

namespace cAlgo.Robots
{
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
}
