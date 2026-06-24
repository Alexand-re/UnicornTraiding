using System.Collections.Generic;

namespace cAlgo.Robots
{
    public class CrashCatcherMstpConfiguration
    {
        public int UniverseSize { get; set; } = 5;
        public double InitialDropPct { get; set; } = 0.02935121389541366;
        public double MaxCorrelationThreshold { get; set; } = 0.8189537877538027;
        public int CorrelationLookback { get; set; } = 10;
        public double SystemicCrashThreshold { get; set; } = 0.87;
        public double SystemicMomentumFactor { get; set; } = 0.47066031073716486;
        public int MomentumLookback { get; set; } = 5;
        public int WindowSize { get; set; } = 28;
        public double GridSpacingPct { get; set; } = 0.011499979506013905;
        public double ScalingRatio { get; set; } = 4.055311586267925;
        public bool UseNormalizedLeverage { get; set; } = true;
        public int MaxTranchesPerSymbol { get; set; } = 2;
        public int MinDaysBetweenTranches { get; set; } = 2;
        public bool SlBasedOnFirstTranche { get; set; } = true;
        public List<double>? TrancheWeights { get; set; } = null;
        public double SymbolStopLossPct { get; set; } = 0.5062874225928855;
        public double TrailingStopPct { get; set; } = 0.2672963328507246;
        public double AtrTrailingStopMultiplier { get; set; } = 8.961016233526642;
        public double TargetProfitFinalPct { get; set; } = 0.09015617075849146;
        public List<TakeProfitStage> Stages { get; set; } = new()
        {
            new TakeProfitStage {ProfitThresholdPct = 0.014067672548939883, SellRatio = 0.28937622255821693},
            new TakeProfitStage {ProfitThresholdPct = 0.04800945602473259, SellRatio = 0.2885118512001836},
            new TakeProfitStage {ProfitThresholdPct = 0.07262607711817924, SellRatio = 0.1533763735140414}
        };
        public TrailingStopMode TsMode { get; set; } = TrailingStopMode.None;
        public bool ExitTrancheAtStageThreshold { get; set; } = true;
        public double LeverageMultiplier { get; set; } = 4.0;
        public double MaxLeverageLimit { get; set; } = 4.7;
        public double RecoveryLeverageMultiplier { get; set; } = 0.26615483312222865;
        public int RecoveryDurationDays { get; set; } = 22;
        public double CircuitBreakerPct { get; set; } = 0.9;
        public double TrailingEquityStopPct { get; set; } = 0.04511898068018211;
        public double VolatilityTarget { get; set; } = 0.0114;
        public double VolatilityMinMultiplier { get; set; } = 1.0;
        public bool UseRegimeFilter { get; set; } = true;
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
}
