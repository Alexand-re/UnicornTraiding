using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    /// <summary>
    /// Robot cTrader Prêt-à-l'Emploi : Institutional Intraday Engine (Version Optimale Levier 4x & Volume Scanner 15m).
    /// Spécifiquement conçu pour maximiser le rendement des comptes Prop Firm (FTMO / cTrader / MyForexFunds).
    /// - Max 10% Absolute Drawdown (Hard Lock de sécurité interne à 8.5%)
    /// - Max 4.5% Daily Loss Limit (Hard Lock de sécurité interne à 3.8%)
    /// - Levier jusqu'à 4.0x exploité avec Sizing Dynamique (0.8% Base Risk)
    /// - Scanner Automatique de Volume / Volatilité 15m (Filtrage des symboles anémiques)
    /// - Fenêtre horaire : 14h45 UTC à 21h00 UTC
    /// - 0 Position Overnight (Liquidation automatique à 21h45 UTC)
    /// </summary>
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class InstitutionalIntradayEngineBot : Robot
    {
        // =========================================================================
        // PARAMÈTRES OPTIMISÉS CTRADER (PROPFIRM RISK & SETUPS)
        // =========================================================================

        [Parameter("Max Absolute DD Limit (%)", Group = "Prop Firm Risk Management", DefaultValue = 8.5, MinValue = 1.0, MaxValue = 9.9)]
        public double TargetAbsMaxDDLimitPct { get; set; }

        [Parameter("Max Daily Loss Limit (%)", Group = "Prop Firm Risk Management", DefaultValue = 3.8, MinValue = 0.5, MaxValue = 4.4)]
        public double TargetDailyLossLimitPct { get; set; }

        [Parameter("Base Risk per Trade (%)", Group = "Prop Firm Risk Management", DefaultValue = 1.2, MinValue = 0.1, MaxValue = 2.0)]
        public double BaseRiskPerTradePct { get; set; }

        [Parameter("Max Leverage Multiplier", Group = "Prop Firm Risk Management", DefaultValue = 4.0, MinValue = 1.0, MaxValue = 4.0)]
        public double MaxLeverageMultiplier { get; set; }

        [Parameter("Min Volume Spike Ratio (15m)", Group = "15m Auto Volume Scanner", DefaultValue = 1.2, MinValue = 0.5, MaxValue = 5.0)]
        public double MinVolumeSpikeRatio { get; set; }

        [Parameter("Start Trading Hour (UTC)", Group = "Timing", DefaultValue = 14)]
        public int StartTradingHourUtc { get; set; }

        [Parameter("Start Trading Minute (UTC)", Group = "Timing", DefaultValue = 45)]
        public int StartTradingMinuteUtc { get; set; }

        [Parameter("Stop New Trades Hour (UTC)", Group = "Timing", DefaultValue = 21)]
        public int StopNewTradesHourUtc { get; set; }

        [Parameter("EOD Force Close Hour (UTC)", Group = "Timing", DefaultValue = 21)]
        public int ForceCloseHourUtc { get; set; }

        [Parameter("EOD Force Close Minute (UTC)", Group = "Timing", DefaultValue = 45)]
        public int ForceCloseMinuteUtc { get; set; }

        [Parameter("Opening Range Minutes", Group = "Strategy Setups", DefaultValue = 15)]
        public int OpeningRangeMinutes { get; set; }

        [Parameter("Min Risk/Reward Ratio", Group = "Strategy Setups", DefaultValue = 2.5)]
        public double MinRiskRewardRatio { get; set; }

        [Parameter("Breakeven Trigger (R)", Group = "Trade Management", DefaultValue = 1.5)]
        public double BreakevenTriggerR { get; set; }

        // =========================================================================
        // ÉTAT INTERNE & VARIABLES DE SESSION
        // =========================================================================

        private double _startOfDayEquity;
        private double _absolutePeakEquity;
        private double _absoluteMaxDD;
        private DateTime _currentSessionDate;
        private bool _isDailyLockoutActive;
        private bool _isAbsDDLockoutActive;

        private double _orbHigh = double.MinValue;
        private double _orbLow = double.MaxValue;
        private double _orbVolume = 0;
        private bool _isOrbFormed;
        private bool _isSymbolValidatedByScanner;

        protected override void OnStart()
        {
            Print("==========================================================================");
            Print("🚀 BOT INSTITUTIONAL INTRADAY ENGINE (OPTIMISÉ RENDEMENT LEVIER 4X & SCANNER 15M)");
            Print($"Account Equity: {Account.Equity} {Account.Asset.Name} | Balance: {Account.Balance}");
            Print($"Hard Locks : Absolute Max DD < {TargetAbsMaxDDLimitPct}% | Daily Loss < {TargetDailyLossLimitPct}%");
            Print($"Config : Base Risk = {BaseRiskPerTradePct}% | Levier Max = {MaxLeverageMultiplier}x | Scanner 15m Min Spike = {MinVolumeSpikeRatio}x");
            Print("==========================================================================");

            _startOfDayEquity = Account.Equity;
            _absolutePeakEquity = Account.Equity;
            _absoluteMaxDD = 0;
            _currentSessionDate = Server.Time.Date;
        }

        protected override void OnBar()
        {
            DateTime nowUtc = Server.Time;

            // 1. Réinitialisation de début de journée (00h00 UTC)
            if (nowUtc.Date != _currentSessionDate)
            {
                _currentSessionDate = nowUtc.Date;
                _startOfDayEquity = Account.Equity;
                _isDailyLockoutActive = false;
                _isOrbFormed = false;
                _isSymbolValidatedByScanner = false;
                _orbHigh = double.MinValue;
                _orbLow = double.MaxValue;
                _orbVolume = 0;
                Print($"[Nouveau Jour {nowUtc:yyyy-MM-dd}] Start Of Day Equity réinitialisée à : {_startOfDayEquity:F2}");
            }

            // 2. Mise à jour du High Water Mark et du Drawdown Absolu
            if (Account.Equity > _absolutePeakEquity)
            {
                _absolutePeakEquity = Account.Equity;
            }

            if (_absolutePeakEquity > 0)
            {
                double currentAbsDD = (Account.Equity - _absolutePeakEquity) / _absolutePeakEquity;
                if (currentAbsDD < _absoluteMaxDD) _absoluteMaxDD = currentAbsDD;
            }

            // 3. Contrôle des Hard Locks Prop Firm (Absolute DD & Daily Loss)
            double currentDailyReturn = _startOfDayEquity > 0 ? (Account.Equity - _startOfDayEquity) / _startOfDayEquity : 0;

            if (currentDailyReturn <= -(TargetDailyLossLimitPct / 100.0))
            {
                if (!_isDailyLockoutActive)
                {
                    _isDailyLockoutActive = true;
                    Print($"🚨 HARD LOCK DAILY DÉCLENCHÉ ({currentDailyReturn:P2} <= -{TargetDailyLossLimitPct}%). Clôture de toutes les positions !");
                    CloseAllPositions("Daily Hard Lock Limit Triggered");
                }
                return;
            }

            if (Math.Abs(_absoluteMaxDD) >= (TargetAbsMaxDDLimitPct / 100.0))
            {
                if (!_isAbsDDLockoutActive)
                {
                    _isAbsDDLockoutActive = true;
                    Print($"🚨 HARD LOCK ABSOLU DÉCLENCHÉ ({_absoluteMaxDD:P2} >= -{TargetAbsMaxDDLimitPct}%). Bot désactivé !");
                    CloseAllPositions("Absolute Max DD Hard Lock Triggered");
                }
                return;
            }

            // 4. Force Close EOD (21h45 UTC) : Clôture de force pour éviter tout risque overnight
            TimeSpan forceCloseTime = new TimeSpan(ForceCloseHourUtc, ForceCloseMinuteUtc, 0);
            if (nowUtc.TimeOfDay >= forceCloseTime)
            {
                if (Positions.Count > 0)
                {
                    Print($"⏰ HEURE FERMETURE EOD ({nowUtc:HH:mm} UTC). Liquidation forcée des positions.");
                    CloseAllPositions("EOD Force Close 21h45 UTC");
                }
                return;
            }

            // 5. Accumulation & Validation du Scanner de Volume 15m (14h30 - 14h45 UTC)
            DateTime marketOpenTime = nowUtc.Date.AddHours(14).AddMinutes(30); // 14h30 UTC = Open US
            DateTime orbEndTime = marketOpenTime.AddMinutes(OpeningRangeMinutes);

            if (nowUtc >= marketOpenTime && nowUtc < orbEndTime)
            {
                var barIndex = Bars.Count - 1;
                if (barIndex >= 0)
                {
                    double high = Bars.HighPrices[barIndex];
                    double low = Bars.LowPrices[barIndex];
                    double vol = Bars.TickVolumes[barIndex];

                    if (high > _orbHigh) _orbHigh = high;
                    if (low < _orbLow) _orbLow = low;
                    _orbVolume += vol;
                }
            }
            else if (nowUtc >= orbEndTime && !_isOrbFormed)
            {
                _isOrbFormed = true;

                // Calcul du scanner automatique : Évaluation du Volume 15m par rapport à la moyenne 20 jours
                double avgHistorical15mVol = CalculateAverageHistorical15mVolume();
                double volumeSpikeRatio = avgHistorical15mVol > 0 ? (_orbVolume / avgHistorical15mVol) : 1.5;

                if (volumeSpikeRatio >= MinVolumeSpikeRatio)
                {
                    _isSymbolValidatedByScanner = true;
                    Print($"🎯 SCANNER 15M VALIDÉ #{SymbolName} | Volume Spike: {volumeSpikeRatio:F2}x >= {MinVolumeSpikeRatio}x | ORB High: {_orbHigh:F5} | ORB Low: {_orbLow:F5}");
                }
                else
                {
                    _isSymbolValidatedByScanner = false;
                    Print($"⚠️ SCANNER 15M REJETÉ #{SymbolName} | Volume Anémique ({volumeSpikeRatio:F2}x < {MinVolumeSpikeRatio}x). Symbol ignoré aujourd'hui pour préserver le capital.");
                }
            }

            // 6. Gestion dynamique des positions ouvertes (Breakeven & Trailing Stop VWAP)
            ManageActivePositions();

            // 7. Évaluation des opportunités d'entrée si le symbole est validé par le scanner
            TimeSpan startTradingTime = new TimeSpan(StartTradingHourUtc, StartTradingMinuteUtc, 0);
            TimeSpan stopTradingTime = new TimeSpan(StopNewTradesHourUtc, 0, 0);

            if (!_isDailyLockoutActive && !_isAbsDDLockoutActive && _isOrbFormed && _isSymbolValidatedByScanner && Positions.Count == 0 &&
                nowUtc.TimeOfDay >= startTradingTime && nowUtc.TimeOfDay < stopTradingTime)
            {
                EvaluateEntrySignals();
            }
        }

        private void ManageActivePositions()
        {
            if (Bars.Count < 20) return;

            double vwap = CalculateIntradayVwap();

            foreach (var pos in Positions)
            {
                if (!pos.StopLoss.HasValue) continue;

                double initialRisk = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);
                if (initialRisk <= 0) continue;

                // Breakeven à +1.5R
                if (pos.TradeType == TradeType.Buy && Symbol.Bid >= pos.EntryPrice + (BreakevenTriggerR * initialRisk))
                {
                    if (pos.StopLoss < pos.EntryPrice)
                    {
                        ModifyPosition(pos, pos.EntryPrice, pos.TakeProfit);
                        Print($"🛡️ Position #{pos.Id} passée au Breakeven à {pos.EntryPrice:F5}");
                    }
                }
                else if (pos.TradeType == TradeType.Sell && Symbol.Ask <= pos.EntryPrice - (BreakevenTriggerR * initialRisk))
                {
                    if (pos.StopLoss > pos.EntryPrice)
                    {
                        ModifyPosition(pos, pos.EntryPrice, pos.TakeProfit);
                        Print($"🛡️ Position #{pos.Id} passée au Breakeven à {pos.EntryPrice:F5}");
                    }
                }

                // Trailing Stop VWAP à +2.0R
                if (pos.TradeType == TradeType.Buy && Symbol.Bid >= pos.EntryPrice + (2.0 * initialRisk))
                {
                    if (vwap > pos.StopLoss)
                    {
                        ModifyPosition(pos, vwap, pos.TakeProfit);
                    }
                }
                else if (pos.TradeType == TradeType.Sell && Symbol.Ask <= pos.EntryPrice - (2.0 * initialRisk))
                {
                    if (vwap < pos.StopLoss)
                    {
                        ModifyPosition(pos, vwap, pos.TakeProfit);
                    }
                }
            }
        }

        private void EvaluateEntrySignals()
        {
            int count = Bars.Count;
            if (count < 20) return;

            int currIdx = count - 1;
            int prevIdx = count - 2;

            double currClose = Bars.ClosePrices[currIdx];
            double prevHigh = Bars.HighPrices[prevIdx];
            double prevLow = Bars.LowPrices[prevIdx];

            double vwap = CalculateIntradayVwap();
            double atr = CalculateAtr(14);

            if (vwap <= 0 || atr <= 0) return;

            // Sizing Dynamique réactif au buffer de Drawdown restant (Risque de base = 0.8%)
            double remainingBuffer = Math.Max(0.10, ((TargetAbsMaxDDLimitPct / 100.0) - Math.Abs(_absoluteMaxDD)) / (TargetAbsMaxDDLimitPct / 100.0));
            double dynamicRiskPct = (BaseRiskPerTradePct / 100.0) * remainingBuffer;
            double riskAmountDollars = Account.Equity * dynamicRiskPct;

            // SETUP A : VWAP Pullback Continuation
            if (currClose > _orbHigh && currClose > vwap)
            {
                // Pullback haussier
                if (prevLow <= vwap && currClose > prevHigh)
                {
                    double entry = Symbol.Ask;
                    double sl = Math.Min(prevLow, vwap - (1.0 * atr));
                    double riskPerUnit = entry - sl;

                    if (riskPerUnit > 0)
                    {
                        double tp = entry + (riskPerUnit * MinRiskRewardRatio);
                        double maxNotional = Account.Equity * MaxLeverageMultiplier;
                        double rawVolume = riskAmountDollars / riskPerUnit;
                        double cappedVolume = Math.Min(rawVolume, maxNotional / entry);
                        double volume = Symbol.NormalizeVolumeInUnits(cappedVolume);

                        if (volume > 0)
                        {
                            ExecuteMarketOrder(TradeType.Buy, SymbolName, volume, "VWAP_Pullback_Bull", sl, tp);
                            Print($"🟢 ACHAT VWAP Pullback #{SymbolName} | Vol: {volume} | SL: {sl:F5} | TP: {tp:F5}");
                        }
                    }
                }
            }
            else if (currClose < _orbLow && currClose < vwap)
            {
                // Pullback baissier
                if (prevHigh >= vwap && currClose < prevLow)
                {
                    double entry = Symbol.Bid;
                    double sl = Math.Max(prevHigh, vwap + (1.0 * atr));
                    double riskPerUnit = sl - entry;

                    if (riskPerUnit > 0)
                    {
                        double tp = entry - (riskPerUnit * MinRiskRewardRatio);
                        double maxNotional = Account.Equity * MaxLeverageMultiplier;
                        double rawVolume = riskAmountDollars / riskPerUnit;
                        double cappedVolume = Math.Min(rawVolume, maxNotional / entry);
                        double volume = Symbol.NormalizeVolumeInUnits(cappedVolume);

                        if (volume > 0)
                        {
                            ExecuteMarketOrder(TradeType.Sell, SymbolName, volume, "VWAP_Pullback_Bear", sl, tp);
                            Print($"🔴 VENTE VWAP Pullback #{SymbolName} | Vol: {volume} | SL: {sl:F5} | TP: {tp:F5}");
                        }
                    }
                }
            }

            // SETUP B : Liquidity Sweep & Reversal (Fakeout)
            if (prevHigh > _orbHigh && currClose < _orbHigh)
            {
                double entry = Symbol.Bid;
                double sl = prevHigh + (0.2 * atr);
                double riskPerUnit = sl - entry;

                if (riskPerUnit > 0)
                {
                    double tp = vwap;
                    if (tp < entry && (entry - tp) / riskPerUnit >= MinRiskRewardRatio)
                    {
                        double maxNotional = Account.Equity * MaxLeverageMultiplier;
                        double rawVolume = riskAmountDollars / riskPerUnit;
                        double cappedVolume = Math.Min(rawVolume, maxNotional / entry);
                        double volume = Symbol.NormalizeVolumeInUnits(cappedVolume);
                        if (volume > 0)
                        {
                            ExecuteMarketOrder(TradeType.Sell, SymbolName, volume, "Liquidity_Sweep_Bear", sl, tp);
                            Print($"🔴 VENTE Liquidity Sweep #{SymbolName} | Vol: {volume} | SL: {sl:F5} | TP: {tp:F5}");
                        }
                    }
                }
            }
            else if (prevLow < _orbLow && currClose > _orbLow)
            {
                double entry = Symbol.Ask;
                double sl = prevLow - (0.2 * atr);
                double riskPerUnit = entry - sl;

                if (riskPerUnit > 0)
                {
                    double tp = vwap;
                    if (tp > entry && (tp - entry) / riskPerUnit >= MinRiskRewardRatio)
                    {
                        double maxNotional = Account.Equity * MaxLeverageMultiplier;
                        double rawVolume = riskAmountDollars / riskPerUnit;
                        double cappedVolume = Math.Min(rawVolume, maxNotional / entry);
                        double volume = Symbol.NormalizeVolumeInUnits(cappedVolume);
                        if (volume > 0)
                        {
                            ExecuteMarketOrder(TradeType.Buy, SymbolName, volume, "Liquidity_Sweep_Bull", sl, tp);
                            Print($"🟢 ACHAT Liquidity Sweep #{SymbolName} | Vol: {volume} | SL: {sl:F5} | TP: {tp:F5}");
                        }
                    }
                }
            }
        }

        private double CalculateAverageHistorical15mVolume()
        {
            int count = Bars.Count;
            if (count < 100) return 10000;

            List<double> past15mVolumes = new List<double>();
            for (int i = 0; i < count - 3; i++)
            {
                var time = Bars.OpenTimes[i];
                if (time.Hour == 14 && time.Minute == 30)
                {
                    double vol15m = Bars.TickVolumes[i] + Bars.TickVolumes[i + 1] + Bars.TickVolumes[i + 2];
                    past15mVolumes.Add(vol15m);
                }
            }

            return past15mVolumes.Count > 0 ? past15mVolumes.Average() : 10000;
        }

        private double CalculateIntradayVwap()
        {
            DateTime today = Server.Time.Date;
            int count = Bars.Count;
            if (count == 0) return Symbol.Bid;

            double sumVolumePrice = 0;
            double sumVolume = 0;

            for (int i = 0; i < count; i++)
            {
                if (Bars.OpenTimes[i].Date == today)
                {
                    double tp = (Bars.HighPrices[i] + Bars.LowPrices[i] + Bars.ClosePrices[i]) / 3.0;
                    double vol = Math.Max(1.0, Bars.TickVolumes[i]);

                    sumVolumePrice += tp * vol;
                    sumVolume += vol;
                }
            }

            return sumVolume > 0 ? sumVolumePrice / sumVolume : Bars.ClosePrices[count - 1];
        }

        private double CalculateAtr(int period)
        {
            int count = Bars.Count;
            if (count < period + 1) return Symbol.PipSize * 10;

            double sumTr = 0;
            for (int i = count - period; i < count; i++)
            {
                double high = Bars.HighPrices[i];
                double low = Bars.LowPrices[i];
                double prevClose = Bars.ClosePrices[i - 1];

                double tr = Math.Max(high - low,
                            Math.Max(Math.Abs(high - prevClose),
                                     Math.Abs(low - prevClose)));
                sumTr += tr;
            }

            return sumTr / period;
        }

        private void CloseAllPositions(string reason)
        {
            foreach (var pos in Positions)
            {
                ClosePosition(pos);
                Print($"❌ Position #{pos.Id} fermée de force. Raison: {reason}");
            }
        }
    }
}
