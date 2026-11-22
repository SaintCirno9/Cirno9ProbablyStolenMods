using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ProjectorShowcase;

/// <summary>
/// 创建幻影投影仪时挂载展示区。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(MachineProjector), nameof(MachineProjector.Projector))]
internal static class ProjectorCreatePatch
{
    private static void Postfix(GameItem __result)
    {
        try
        {
            ProjectorStorage.Attach(__result, fromProjectorFactory: true);
        }
        catch (Exception ex)
        {
            ProjectorShowcaseMod.Log?.Error("挂载幻影投影仪展示区失败：\n" + ex);
        }
    }
}

/// <summary>
/// 打开投影仪窗口时刷新展示区标题与网格尺寸，避开加载期本地化未就绪的问题并响应语言切换。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Show), new Type[] { })]
internal static class ProjectorWindowShowPatch
{
    private static void Postfix(PixelWindow __instance)
    {
        try
        {
            ProjectorStorage.OnWindowShown(__instance);
        }
        catch (Exception ex)
        {
            ProjectorShowcaseMod.Log?.Error("刷新幻影投影仪展示窗口失败：\n" + ex);
        }
    }
}

[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Show), new[] { typeof(Vector2), typeof(Vector2), typeof(bool) })]
internal static class ProjectorWindowShowPositionedPatch
{
    private static void Postfix(PixelWindow __instance)
    {
        try
        {
            ProjectorStorage.OnWindowShown(__instance);
        }
        catch (Exception ex)
        {
            ProjectorShowcaseMod.Log?.Error("刷新幻影投影仪定位窗口失败：\n" + ex);
        }
    }
}

/// <summary>
/// 仅在展示柜业务查询作用域内将已生效的幻影投影仪的物品并入 <c>showcaseElement.childItems</c>，
/// 不影响物理网格整理、存档编码与全店估算价值统计。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(GameGridInventory), nameof(GameGridInventory.childItems), MethodType.Getter)]
internal static class ShowcaseChildItemsPatch
{
    [ThreadStatic]
    internal static int ScopeDepth;

    private static void Postfix(GameGridInventory __instance, List<GameItem> __result)
    {
        if (ScopeDepth <= 0 || !ProjectorShowcaseMod.Active)
        {
            return;
        }
        EmporiumEntry entry = EmporiumEntry.Instance;
        if (entry == null || __instance != entry.showcaseElement)
        {
            return;
        }
        ProjectorStorage.AppendActiveShowcaseItems(__result);
    }
}

/// <summary>
/// 覆盖原版展示柜吸引、客源生成、招牌、黑灯、鉴定证书、客源预测、展示柜违禁品检查与展示柜主库存统计入口；
/// 使用最高优先级前缀与最低优先级终结器包裹其他模组在同一方法上的前后缀与转译器。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch]
internal static class ShowcaseQueryScopePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.HasShowcaseSpecialItem));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.HasMerchandise));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetTypeCount));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetTypeTotalValue));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCasePrediction));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDiscerningShopperChance));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.HandleDisplayCase));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCaseType));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCaseCustomer));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateDisplayCaseAugBuyer));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetMostCommonWaterTypeInDisplay));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.CreateClientByStockType));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.IsShowcaseContainItemById));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetShowCaseContainItemCount));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.IsShowCaseContainSeriousContraband));
        yield return AccessTools.DeclaredMethod(typeof(ShowcaseHelper), nameof(ShowcaseHelper.IsShowCaseContainSeriousStolen));
        yield return AccessTools.DeclaredMethod(typeof(AppraisalRankHelper), nameof(AppraisalRankHelper.GetAttractionChance));
        yield return AccessTools.DeclaredMethod(typeof(AppraisalRankHelper), nameof(AppraisalRankHelper.GetDisplayedCertificate));
        yield return AccessTools.DeclaredMethod(typeof(BlackLampHelper), nameof(BlackLampHelper.GetLampCountForTarget));
        yield return AccessTools.DeclaredMethod(typeof(BlackLampHelper), nameof(BlackLampHelper.GetChanceForTarget));
        yield return AccessTools.DeclaredMethod(typeof(BlackLampHelper), nameof(BlackLampHelper.GetActiveInvitations));
        yield return AccessTools.DeclaredMethod(typeof(BlackLampHelper), nameof(BlackLampHelper.CreateBlackLampTooltip));
        yield return AccessTools.DeclaredMethod(typeof(BlackLampHelper), "IsInDisplayCase");
        yield return AccessTools.DeclaredMethod(typeof(ClienteleUIManager), nameof(ClienteleUIManager.RefreshUI));
        yield return AccessTools.DeclaredMethod(typeof(ContrabandHelper), nameof(ContrabandHelper.SecurityCheckDisplayCase));
        yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.CheckMostStockType));
        yield return AccessTools.DeclaredMethod(typeof(PlayerStore), nameof(PlayerStore.CheckMostStockTypeShowcase));
    }

    [HarmonyPriority(Priority.First)]
    private static void Prefix()
    {
        ShowcaseChildItemsPatch.ScopeDepth++;
    }

    [HarmonyPriority(Priority.Last)]
    private static void Finalizer()
    {
        if (ShowcaseChildItemsPatch.ScopeDepth > 0)
        {
            ShowcaseChildItemsPatch.ScopeDepth--;
        }
    }
}
