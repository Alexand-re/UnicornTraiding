using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using cAlgo.API;
using cAlgo.API.Internals;
using MongoDB.Bson;
using MongoDB.Driver;

namespace cAlgo.Robots
{
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
                    
                    // Run MOC logic asynchronously in a background thread to prevent UI freezing
                    Task.Run(async () =>
                    {
                        try
                        {
                            await ProcessMocLogicAsync(nowEst.Date);
                        }
                        catch (Exception ex)
                        {
                            Print("[UnicornTrading cTrader] ProcessMocLogicAsync Error: " + ex.Message);
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
            if (standardSymbol == "SPY")
            {
                return SpySymbolOverride;
            }
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
            }
            return result;
        }

        private async Task ProcessMocLogicAsync(DateTime today)
        {
            Print("[UnicornTrading cTrader] Running MOC Logic...");

            try
            {
                // 1. Get latest state
                CrashCatcherDailyState latestState = await GetLatestStateAsync();
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
                var universeDocs = await _universeHistoryCollection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
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

                var assetDocs = await _assetInfoCollection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
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
                    var symbolInfo = Symbols.GetSymbol(brokerSym);
                    if (symbolInfo == null) continue;

                    var ctraderBars = MarketData.GetBars(TimeFrame.Daily, brokerSym);
                    if (ctraderBars == null || ctraderBars.Count == 0) continue;

                    var localBarsList = new List<Bars>();
                    // Take up to 500 latest bars to optimize performance while providing enough history (e.g. for MA200 & ATR)
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
                        // Position was manually closed
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
                    if (Symbols.GetSymbol(brokerSym) != null)
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

                // Sort bars for engine
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
                        var symbolInfo = Symbols.GetSymbol(brokerSym);
                        if (symbolInfo == null)
                        {
                            Print($"[UnicornTrading Buy] Error: Symbol {brokerSym} not found in broker database.");
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
                            // Align the quantities with broker filled quantity
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
                await SaveStateAsync(latestState);
                Print(string.Format("[UnicornTrading cTrader] MOC Logic Complete. NAV: {0:N2} | Positions: {1}",
                    latestState.TotalEquity, latestState.ActiveOrders.Count));
            }
            catch (Exception ex)
            {
                Print("[UnicornTrading cTrader] Critical Error in MOC logic execution: " + ex.Message);
            }
        }

        private async Task<CrashCatcherDailyState> GetLatestStateAsync()
        {
            return await _stateCollection.Find(Builders<CrashCatcherDailyState>.Filter.Empty)
                .SortByDescending(d => d.UpdatedAt)
                .FirstOrDefaultAsync();
        }

        private async Task SaveStateAsync(CrashCatcherDailyState state)
        {
            state.UpdatedAt = DateTime.UtcNow;
            var filter = Builders<CrashCatcherDailyState>.Filter.Eq(s => s.Id, state.Id);
            await _stateCollection.ReplaceOneAsync(filter, state, new ReplaceOptions { IsUpsert = true });
        }
    }
}
