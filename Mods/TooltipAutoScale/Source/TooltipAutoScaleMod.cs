using System;
using UnityEngine;

namespace TooltipAutoScale
{
    /// <summary>
    /// 提示框自适应缩放模组入口：超长时先适度加宽折行，仍超出屏幕比例时自动等比缩小并修正边界位置。
    /// Harmony 特性补丁由游戏模组加载器自动安装与卸载，本类仅管理开关状态、设置项与清理还原。
    /// </summary>
    public sealed class TooltipAutoScaleMod : IMod
    {
        public const string ModId = "cirno9.tooltip_auto_scale";
        private const string SectionId = "TooltipAutoScale";
        private const string ConfigSectionName = "Tooltip Auto Scale";

        private static ConfigEntry<bool> _adaptiveWiden;
        private static ConfigEntry<int> _maxScreenPercent;
        private static ConfigEntry<int> _minScalePercent;

        /// <summary>模组当前是否处于启用状态。</summary>
        public static bool Active { get; private set; }

        /// <summary>超长提示框是否先尝试分步加宽折行。</summary>
        public static bool AdaptiveWidenEnabled => _adaptiveWiden == null || _adaptiveWiden.Value;

        /// <summary>提示框宽或高允许占用的最大屏幕比例（0.50~0.95）。</summary>
        public static float MaxScreenRatio =>
            Mathf.Clamp((_maxScreenPercent != null ? _maxScreenPercent.Value : 85) / 100f, 0.50f, 0.95f);

        /// <summary>自动缩小时允许的最小可读缩放比例（0.35~1.00）。</summary>
        public static float MinScaleRatio =>
            Mathf.Clamp((_minScalePercent != null ? _minScalePercent.Value : 50) / 100f, 0.35f, 1.00f);

        /// <summary>初始化配置定义，禁止在此阶段读取本地化结果。</summary>
        public void Init(ModManifest manifest)
        {
            ModConfig config = ModConfig.For(ModId);
            _adaptiveWiden = config.Define(
                "adaptive_widen",
                true,
                ConfigSectionName,
                () => Text("opt.adaptive_widen.label", "超长先适度加宽折行"),
                () => Text("opt.adaptive_widen.tip", "提示框超出最大占屏比例时，先在屏幕宽度允许范围内分步加宽折行，仍超出时再缩小"));

            _maxScreenPercent = config.Define(
                "max_screen_percent",
                85,
                ConfigSectionName,
                () => Text("opt.max_screen_percent.label", "提示框最大占屏比例 (%)"),
                50,
                95,
                () => Text("opt.max_screen_percent.tip", "悬停提示框宽或高允许占用的最大屏幕比例；加宽折行与自动缩小均以此为上限"));

            _minScalePercent = config.Define(
                "min_scale_percent",
                50,
                ConfigSectionName,
                () => Text("opt.min_scale_percent.label", "最小自动缩放比例 (%)"),
                35,
                100,
                () => Text("opt.min_scale_percent.tip", "自动缩小时允许的下限比例，避免极端超长文本缩到难以辨认"));
        }

        /// <summary>注册选项菜单条目并启用自适应布局。</summary>
        public void OnEnable()
        {
            Active = true;
            RegisterOptions();
        }

        /// <summary>注销选项、清理缓存并还原提示框原始缩放。</summary>
        public void OnDisable()
        {
            Active = false;
            GameOptionRegistry.RemoveBySource(ModId);
            TooltipLayout.Reset();

            try
            {
                PixelWindow tooltipWindow = OverlayHandler.current != null
                    ? OverlayHandler.current.GetTooltipWindow()
                    : null;
                if (tooltipWindow != null && tooltipWindow.rectTransform != null)
                {
                    tooltipWindow.rectTransform.localScale = Vector3.one;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[TooltipAutoScale] 还原提示框缩放失败: " + ex);
            }
        }

        private static void RegisterOptions()
        {
            GameOptionRegistry.RemoveBySource(ModId);
            GameOptionRegistry.GetOrAddSection(
                SectionId,
                () => Text("section.title", "提示框自适应缩放"),
                ModId);

            GameOptionRegistry.Entry widenEntry = GameOptionRegistry.Toggle(
                ModId + ".adaptive_widen",
                () => Text("opt.adaptive_widen.label", "超长先适度加宽折行"),
                () => _adaptiveWiden == null || _adaptiveWiden.Value,
                v => _adaptiveWiden?.Set(v),
                () => Text("opt.adaptive_widen.tip", "提示框超出最大占屏比例时，先在屏幕宽度允许范围内分步加宽折行，仍超出时再缩小"));
            widenEntry.source = ModId;
            GameOptionRegistry.Add(SectionId, widenEntry);

            GameOptionRegistry.Entry maxRatioEntry = GameOptionRegistry.Slider(
                ModId + ".max_screen_percent",
                () => Text("opt.max_screen_percent.label", "提示框最大占屏比例 (%)"),
                () => _maxScreenPercent != null ? _maxScreenPercent.Value : 85,
                v => _maxScreenPercent?.Set(Mathf.Clamp(Mathf.RoundToInt(v), 50, 95)),
                50,
                95,
                true,
                () => Text("opt.max_screen_percent.tip", "悬停提示框宽或高允许占用的最大屏幕比例；加宽折行与自动缩小均以此为上限"));
            maxRatioEntry.source = ModId;
            GameOptionRegistry.Add(SectionId, maxRatioEntry);

            GameOptionRegistry.Entry minScaleEntry = GameOptionRegistry.Slider(
                ModId + ".min_scale_percent",
                () => Text("opt.min_scale_percent.label", "最小自动缩放比例 (%)"),
                () => _minScalePercent != null ? _minScalePercent.Value : 50,
                v => _minScalePercent?.Set(Mathf.Clamp(Mathf.RoundToInt(v), 35, 100)),
                35,
                100,
                true,
                () => Text("opt.min_scale_percent.tip", "自动缩小时允许的下限比例，避免极端超长文本缩到难以辨认"));
            minScaleEntry.source = ModId;
            GameOptionRegistry.Add(SectionId, minScaleEntry);
        }

        private static string Text(string key, string fallback)
        {
            string value = ModHelper.GetLocalized(ModId, key);
            return string.IsNullOrEmpty(value) || value == key ? fallback : value;
        }
    }
}
