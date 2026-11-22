using System;
using System.Collections.Generic;
using UnityEngine;

namespace BatchOpenContainers;

/// <summary>Author: SaintCirno9。框选多个容器后右键批量打开全部选中容器。</summary>
public sealed class BatchOpenMod : IMod
{
    internal const string ModId = "cirno9.batch_open_containers";
    private const string SectionTitle = "框选批量打开 / Batch Open";
    private const string SectionId = ModId + ".options";

    internal static ModLog Log { get; private set; }
    internal static bool Active { get; private set; }
    internal static ConfigEntry<bool> DirectRightClick { get; private set; }
    internal static ConfigEntry<int> LayoutMode { get; private set; }
    internal static ConfigEntry<bool> PreserveSavedPositions { get; private set; }

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
        ModConfig config = ModConfig.For(ModId);

        DirectRightClick = config.Define(
            "direct_right_click",
            false,
            SectionTitle,
            () => Text("cfg_direct_right_click", "框选后右键直接打开全部容器"),
            tooltip: () => Text("cfg_direct_right_click_tip", "开启后，框选 2 个及以上可打开容器且至少 1 个未打开时，右键其中任一容器会直接全部打开（按住 Shift 右键仍可打开右键菜单）。关闭后（默认）始终弹出右键菜单，并将“打开”显示为“打开全部 (N)”。"));

        LayoutMode = config.Define(
            "layout_mode",
            0,
            SectionTitle,
            () => Text("cfg_layout_mode", "批量打开窗口排列方式"),
            0,
            2,
            tooltip: () => Text("cfg_layout_mode_tip", "批量打开多个容器时，对未记忆位置的新窗口的摆放方式：平铺尽量不重叠排列；层叠按固定偏移错开；原版位置则按鼠标位置或原版逻辑打开。"));

        PreserveSavedPositions = config.Define(
            "preserve_saved_positions",
            true,
            SectionTitle,
            () => Text("cfg_preserve_saved_positions", "保留已记忆的窗口位置"),
            tooltip: () => Text("cfg_preserve_saved_positions_tip", "开启后（默认），以前记录过窗口位置的容器仍会在原记忆位置打开，仅对没有记录的新窗口应用排列；关闭时会对本次批量新打开的窗口统一排列，避免旧位置重叠。"));
    }

    public void OnEnable()
    {
        Active = true;
        ModHook.OnGameLoadedLate += OnGameLoadedLate;

        GameOptionRegistry.GetOrAddSection(SectionId, () => SectionTitle, ModId);

        GameOptionRegistry.Entry directEntry = GameOptionRegistry.Toggle(
            ModId + ".direct_right_click",
            DirectRightClick.Label,
            () => DirectRightClick.Value,
            value => DirectRightClick.Set(value),
            DirectRightClick.Tooltip);
        directEntry.source = ModId;
        GameOptionRegistry.Add(SectionId, directEntry);

        GameOptionRegistry.Entry layoutEntry = GameOptionRegistry.Dropdown(
            ModId + ".layout_mode",
            LayoutMode.Label,
            GetLayoutOptionNames,
            () => Mathf.Clamp(LayoutMode.Value, 0, 2),
            value => LayoutMode.Set(Mathf.Clamp(value, 0, 2)),
            LayoutMode.Tooltip);
        layoutEntry.source = ModId;
        GameOptionRegistry.Add(SectionId, layoutEntry);

        GameOptionRegistry.Entry preserveEntry = GameOptionRegistry.Toggle(
            ModId + ".preserve_saved_positions",
            PreserveSavedPositions.Label,
            () => PreserveSavedPositions.Value,
            value => PreserveSavedPositions.Set(value),
            PreserveSavedPositions.Tooltip);
        preserveEntry.source = ModId;
        GameOptionRegistry.Add(SectionId, preserveEntry);

        Log.Log("已启用 1.1.0：框选多个可打开容器后，右键会正常弹出菜单并将“打开”显示为“打开全部 (N)”，默认保留已记忆窗口位置并排列新窗口。");
    }

    public void OnDisable()
    {
        Active = false;
        ModHook.OnGameLoadedLate -= OnGameLoadedLate;
        GameOptionRegistry.RemoveBySource(ModId);
        BatchOpenLogic.ResetSession();
    }

    private static void OnGameLoadedLate()
    {
        BatchOpenLogic.ResetSession();
    }

    private static IList<string> GetLayoutOptionNames()
    {
        return new[]
        {
            Text("cfg_layout_tile", "平铺（不重叠网格）"),
            Text("cfg_layout_cascade", "层叠（错开叠放）"),
            Text("cfg_layout_vanilla", "原版位置")
        };
    }

    internal static string Text(string key, string fallback)
    {
        string value = ModHelper.GetLocalized(ModId, key);
        return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }
}
