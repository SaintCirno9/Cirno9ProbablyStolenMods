using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FastQuit;

/// <summary>
/// 退出时加载器对每个模组先调用 OnDisable 再 <c>UnpatchAll("mod.&lt;ID&gt;")</c>，不少模组还在 OnDisable 里用自己的 owner 撤销补丁。
/// Harmony 2.4 的 UnpatchAll 要遍历全部已补丁方法，且每撤销一个补丁就重建一次目标方法；补丁多时累计几十秒。
/// 退出流程确认开始后的极短窗口内跳过所有撤销；进程随即结束，补丁无需撤销。
/// Author: SaintCirno9
/// </summary>
internal static class QuitUnpatchGuard
{
    /// <summary>
    /// wantsToQuit 与加载器的 OnApplicationQuit 之间只隔极少帧，且 OnApplicationQuit 内逐个停用模组是同步执行、帧号不变。
    /// 窗口外（如退出被其他处理器取消后，在模组界面停用模组）照常撤销补丁。
    /// </summary>
    private const int QuitFrameWindow = 5;

    private const string LoaderOwnerPrefix = "mod.";

    /// <summary>加载器自身的补丁 owner，扫描器禁止模组撤销，这里也不干预。</summary>
    private const string GuardOwnerPrefix = "dse.";

    /// <summary>超过该耗时的 OnDisable 写入日志。</summary>
    private const long SlowDisableMs = 100;

    private static int s_quitFrame = -1;

    /// <summary>加载器先调用模组 OnDisable 再立即 UnpatchAll，相邻两次加载器调用的间隔即该模组 OnDisable 的耗时。</summary>
    private static readonly Stopwatch s_watch = new Stopwatch();

    private static long s_lastMs;

    private static int s_loaderCalls;

    private static int s_otherCalls;

    internal static bool OnWantsToQuit()
    {
        s_quitFrame = Time.frameCount;
        s_watch.Restart();
        s_lastMs = 0;
        s_loaderCalls = 0;
        s_otherCalls = 0;
        return true;
    }

    /// <summary>返回 true 表示放行原方法。</summary>
    internal static bool Allow(string ownerId)
    {
        if (s_quitFrame < 0 || Time.frameCount - s_quitFrame > QuitFrameWindow
            || ownerId == null || ownerId.StartsWith(GuardOwnerPrefix, System.StringComparison.Ordinal))
        {
            return true;
        }
        if (!ownerId.StartsWith(LoaderOwnerPrefix, System.StringComparison.Ordinal))
        {
            s_otherCalls++;
            return false;
        }
        long now = s_watch.ElapsedMilliseconds;
        long elapsed = now - s_lastMs;
        s_lastMs = now;
        s_loaderCalls++;
        if (s_loaderCalls == 1)
        {
            // 第一个模组的计时起点是退出请求，包含到 OnApplicationQuit 之间的其他退出处理。
            FastQuitMod.Log.Log($"正在退出游戏：跳过 Harmony 补丁撤销，OnDisable 照常执行。首个模组 {ownerId} 距退出请求 {elapsed} ms。");
        }
        else if (elapsed >= SlowDisableMs)
        {
            FastQuitMod.Log.Log($"OnDisable 较慢：{ownerId} 耗时 {elapsed} ms（第 {s_loaderCalls} 个，退出后累计 {now} ms，已跳过模组自行撤销 {s_otherCalls} 次）。");
        }
        return false;
    }
}

[HarmonyPatch(typeof(Harmony), nameof(Harmony.UnpatchAll))]
internal static class UnpatchAllPatch
{
    private static bool Prefix(string harmonyID)
    {
        return QuitUnpatchGuard.Allow(harmonyID);
    }
}

[HarmonyPatch(typeof(Harmony), nameof(Harmony.Unpatch), typeof(MethodBase), typeof(HarmonyPatchType), typeof(string))]
internal static class UnpatchByTypePatch
{
    private static bool Prefix(string harmonyID)
    {
        return QuitUnpatchGuard.Allow(harmonyID);
    }
}

[HarmonyPatch(typeof(Harmony), nameof(Harmony.Unpatch), typeof(MethodBase), typeof(MethodInfo))]
internal static class UnpatchByMethodPatch
{
    private static bool Prefix(Harmony __instance)
    {
        return QuitUnpatchGuard.Allow(__instance.Id);
    }
}
