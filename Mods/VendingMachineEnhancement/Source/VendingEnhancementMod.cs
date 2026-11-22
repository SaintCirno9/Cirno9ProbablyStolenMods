namespace VendingMachineEnhancement;

/// <summary>
/// 模组入口：为自动售货机与自动售水机接入改装模组支持、翻倍容量上限并修复窗口位置保存。
/// Author: SaintCirno9
/// </summary>
public sealed class VendingEnhancementMod : IMod
{
    internal const string ModId = "cirno9.vending_machine_enhancement";
    private const string EffectSection = "模组效果 / Module Effects";
    private const int DefaultQualityCoefficient = 10;
    private const int DefaultPerformanceCoefficient = 100;
    private const int DefaultGuaranteedStep = 50;

    private static ConfigEntry<int> s_qualityCoefficient;
    private static ConfigEntry<int> s_performanceCoefficient;
    private static ConfigEntry<int> s_guaranteedStep;

    internal static int QualityCoefficient => s_qualityCoefficient?.Value ?? DefaultQualityCoefficient;
    internal static int PerformanceCoefficient => s_performanceCoefficient?.Value ?? DefaultPerformanceCoefficient;
    internal static int GuaranteedStep => s_guaranteedStep?.Value ?? DefaultGuaranteedStep;

    internal static ModLog Log { get; private set; }

    /// <summary>加载器调用过 OnEnable 且尚未 OnDisable。</summary>
    internal static bool Active { get; private set; }

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);

        ModConfig config = ModConfig.For(ModId);
        s_qualityCoefficient ??= config.Define("quality_coefficient", DefaultQualityCoefficient, EffectSection,
            () => ModHelper.GetLocalized(ModId, "config_quality_coefficient"), min: 0, max: 100,
            tooltip: () => ModHelper.GetLocalized(ModId, "config_quality_coefficient_help", DefaultQualityCoefficient));
        s_performanceCoefficient ??= config.Define("performance_coefficient", DefaultPerformanceCoefficient, EffectSection,
            () => ModHelper.GetLocalized(ModId, "config_performance_coefficient"), min: 0, max: 300,
            tooltip: () => ModHelper.GetLocalized(ModId, "config_performance_coefficient_help", DefaultPerformanceCoefficient));
        s_guaranteedStep ??= config.Define("guaranteed_attractiveness_step", DefaultGuaranteedStep, EffectSection,
            () => ModHelper.GetLocalized(ModId, "config_guaranteed_step"), min: 0, max: 500,
            tooltip: () => ModHelper.GetLocalized(ModId, "config_guaranteed_step_help", DefaultGuaranteedStep));
    }

    public void OnEnable()
    {
        VendingEnhancementCore.ResetTransientState();
        Active = true;
        ModHook.OnCreateTooltipEarly += VendingEnhancementCore.AddVendingItemTooltip;
        Log?.Log("已启用：两种自动售货机现已支持改装模组、容量升级上限翻倍至 20 次（最大 14×16）以及窗口位置保存。");
    }

    public void OnDisable()
    {
        ModHook.OnCreateTooltipEarly -= VendingEnhancementCore.AddVendingItemTooltip;
        Active = false;
        VendingEnhancementCore.ResetTransientState();
    }
}
