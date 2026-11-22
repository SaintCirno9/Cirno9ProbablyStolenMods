using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ModKeybindHub;

/// <summary>
/// 打开选项窗口前整理分区。原版 OpenUI 第一步就是 EnsureVanilla，这里提前调用，
/// 保证原版条目和挂在 EnsureVanilla 上补注册的模组条目都已就位。
/// 优先级最低，尽量排在其他模组往选项窗口补条目的前缀之后。
/// </summary>
[HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OpenUI))]
internal static class OpenUIPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix()
    {
        if (!KeybindHub.Active)
        {
            return;
        }
        try
        {
            GameOptionRegistry.EnsureVanilla();
            KeybindHub.Organize();
        }
        catch (Exception e)
        {
            KeybindHubMod.Log.Error("整理模组快捷键分区失败，选项窗口保持原样：" + e);
        }
    }
}

/// <summary>原版每次刷新联动规则（建窗、改键、应用）后重写按键条目名称追加冲突标记，并给按键按钮挂右键清空。</summary>
[HarmonyPatch(typeof(GameOptionUIManager), "RefreshRules")]
internal static class RefreshRulesPatch
{
    private static void Postfix(GameOptionUIManager __instance, CustomUIWindow ___window, Dictionary<string, KeyCode> ___pendingKeys)
    {
        if (!KeybindHub.Active || ___window == null || !__instance.IsOpen)
        {
            return;
        }
        try
        {
            KeybindHub.DecorateLabels(__instance, ___window);
            KeybindHub.AttachRightClick(___window, ___pendingKeys);
        }
        catch (Exception e)
        {
            KeybindHubMod.Log.Error("刷新按键冲突标记或挂载右键清空失败：" + e);
        }
    }
}
