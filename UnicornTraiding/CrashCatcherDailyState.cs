using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace cAlgo.Robots
{
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
}
