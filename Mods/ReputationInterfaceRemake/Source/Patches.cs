using System;
using HarmonyLib;

namespace ReputationInterfaceRemake;

[HarmonyPatch(typeof(ReputationUIManager), nameof(ReputationUIManager.OpenUI))]
internal static class OpenUIPatch
{
    // 排在可能跳过原版 OpenUI 的前缀补丁之前，确保旧行标记与原始 layout 快照必然执行。
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ReputationUIManager __instance)
    {
        try
        {
            BoardController.CaptureOriginalLayout(__instance);
            BoardData.MarkStale(__instance.layout != null ? __instance.layout.transform : null);
        }
        catch (Exception ex)
        {
            ReputationInterfaceRemakeMod.Log.Error("记录旧势力行失败：" + ex);
        }
    }

    // 排在其他模组之后，确保它们在 OpenUI 后追加的内容已就位。
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ReputationUIManager __instance)
    {
        try
        {
            // 灾变日在灯塔面板只在 OpenUI 之后延迟销毁原版行，同帧内还会被分页器重新激活，需标记其余兄弟为旧行。
            BoardData.MarkSiblingsStale(__instance.layout != null ? __instance.layout.transform : null, "Reputation_Lighthouse");
            BoardController.OnOpened(__instance);
        }
        catch (Exception ex)
        {
            ReputationInterfaceRemakeMod.Log.Error("挂载新版势力档案失败：" + ex);
        }
    }
}

[HarmonyPatch(typeof(ReputationElement), nameof(ReputationElement.Init))]
internal static class ReputationElementInitPatch
{
    private static void Postfix(ReputationElement __instance, StoreReputation storeReputation)
    {
        BoardData.Bind(__instance, storeReputation);
        BoardController.RequestRefreshAll();
    }
}
