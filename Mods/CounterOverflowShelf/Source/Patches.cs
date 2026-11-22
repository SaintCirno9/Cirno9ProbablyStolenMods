using System.Collections.Generic;
using HarmonyLib;

namespace CounterOverflowShelf;

/// <summary>
/// 开启扩容货架时接管原版 1.0 货物翻页，让所有溢出的非己方货物统一进货架。
/// 关闭模组总开关后退回原版翻页逻辑。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(StockPageHelper), nameof(StockPageHelper.IsPagingFor))]
internal static class StockPageIsPagingForPatch
{
    private static bool Prefix(bool isOwned, ref bool __result)
    {
        if (!isOwned && OverflowShelf.CanCaptureNow())
        {
            __result = false;
            return false;
        }
        return true;
    }
}

/// <summary>
/// 防止玩家单件买空柜台与谈判区时，只卖货的顾客误判为货物已卖完而提前离开。
/// </summary>
[HarmonyPatch(typeof(StockPageHelper), nameof(StockPageHelper.HasHeldStock))]
internal static class StockPageHasHeldStockPatch
{
    private static void Postfix(StoreClient storeClient, ref bool __result)
    {
        if (!__result && OverflowShelf.IsCurrentClient(storeClient) && OverflowShelf.HasNonOwned())
        {
            __result = true;
        }
    }
}

/// <summary>
/// 让违禁品/赃物检查与举报奖金同步计入扩容货架上的未购货物。
/// </summary>
[HarmonyPatch(typeof(StockPageHelper), nameof(StockPageHelper.GetHeldItems))]
internal static class StockPageGetHeldItemsPatch
{
    private static void Postfix(StoreClient storeClient, List<GameItem> __result)
    {
        if (OverflowShelf.IsCurrentClient(storeClient))
        {
            OverflowShelf.AppendNonOwned(__result);
        }
    }
}

/// <summary>
/// 原版在柜台放不下货物时以 "front counter full" 上下文调用 DestroyIfUnplaced 销毁物品。
/// 前缀先把物品放进溢出货架；放进去后物品已有所属容器，原版方法会直接返回“已放置”而不销毁。
/// 同时也兜底直接调用 StockPageHelper.PlaceOrHold 导致的溢出上下文。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(GraphUtils), nameof(GraphUtils.DestroyIfUnplaced))]
internal static class DestroyIfUnplacedPatch
{
    internal const string FrontCounterFull = "front counter full";

    internal const string StockPageFull = "does not fit the front counter or a stock page";

    private static void Prefix(GameItem item, string context)
    {
        if (context == FrontCounterFull || context == StockPageFull)
        {
            OverflowShelf.TryStore(item);
        }
    }
}

/// <summary>
/// 按物品 ID 上货的重载在柜台已满时不创建物品，只记一笔 "offer skipped"。
/// 此时改走 GameItem 重载，让多出来的货物同样经过上面的销毁拦截进入货架。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.TryAddDirectSellingItemToTable), new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(int) })]
internal static class TryAddByIdentifierPatch
{
    private static bool Prefix(PlayerStore __instance, string identifier, bool isOwend, bool isStolen, bool ignorePerk, int forcedHeat, ref bool __result)
    {
        // 玩家自己的物品不进货架，保持原版行为。
        if (isOwend || !OverflowShelf.CanCaptureNow())
        {
            return true;
        }
        EmporiumEntry entry = EmporiumEntry.Instance;
        if (entry == null || entry.frontInvinvElement == null || entry.frontInvinvElement.CanTakeAnything())
        {
            return true;
        }
        __result = __instance.TryAddDirectSellingItemToTable(DirectoryMaster.Item(identifier), isOwend, isStolen, ignorePerk, forcedHeat);
        return false;
    }
}

/// <summary>顾客离开、拉下卷帘门等清除柜台上非己方货物时，一并清除货架。</summary>
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.RemoveNotOwnedItemFromTable))]
internal static class RemoveNotOwnedItemFromTablePatch
{
    private static void Postfix()
    {
        OverflowShelf.Clear(countNuclearWaste: false);
    }
}

/// <summary>离开杰克逊博士、日终等清空柜台时，一并清除货架。</summary>
[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.ClearFrontInv))]
internal static class ClearFrontInvPatch
{
    private static void Postfix()
    {
        OverflowShelf.Clear(countNuclearWaste: true);
    }
}

[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.ClearFrontInvNonOwned))]
internal static class ClearFrontInvNonOwnedPatch
{
    private static void Postfix()
    {
        OverflowShelf.Clear(countNuclearWaste: true);
    }
}

/// <summary>无顾客时的兜底清台入口（对齐原版清空 StockPageHelper 的时机）。</summary>
[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.RemoveNonOwnedItem))]
internal static class RemoveNonOwnedItemPatch
{
    private static void Postfix()
    {
        if (PlayerStore.Instance == null || PlayerStore.Instance.currentClientInstance == null)
        {
            OverflowShelf.Clear(countNuclearWaste: false);
        }
    }
}

/// <summary>
/// 补足未购物品总件数，确保只剩货架有货时批发按钮依然显示，且不误判交易结束。
/// GetAllNonOwendItemCount 内部调用 GetAllNonOwnedItem，仅在 WholesaleDepth == 0 时追加计数以避免重复。
/// </summary>
[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.GetAllNonOwendItemCount))]
internal static class GetAllNonOwendItemCountPatch
{
    private static void Postfix(ref int __result)
    {
        if (GetAllNonOwnedItemPatch.WholesaleDepth == 0)
        {
            __result += OverflowShelf.NonOwnedCount();
        }
    }
}

/// <summary>
/// 批发按“这位客人的全部货物”计价和买入，原版的 GetAllNonOwnedItem 只统计柜台与谈判区。
/// 在批发计价与批发成交期间把货架上的货一并算进去，否则只卖货的顾客批发后直接离开，货架上的货随清台销毁。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.GetAllNonOwnedItem))]
internal static class GetAllNonOwnedItemPatch
{
    internal static int WholesaleDepth;

    private static void Postfix(List<GameItem> __result)
    {
        if (WholesaleDepth > 0)
        {
            OverflowShelf.AppendNonOwned(__result);
        }
    }
}

/// <summary>谈判面板显示的批发总价与批发按钮是否可点都来自这里。</summary>
[HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.GetWholesalePrice))]
internal static class GetWholesalePricePatch
{
    private static void Prefix()
    {
        GetAllNonOwnedItemPatch.WholesaleDepth++;
    }

    private static void Finalizer()
    {
        GetAllNonOwnedItemPatch.WholesaleDepth--;
    }
}

[HarmonyPatch(typeof(NegociationUIManager), nameof(NegociationUIManager.OnWholesaleClick))]
internal static class OnWholesaleClickPatch
{
    private static void Prefix()
    {
        GetAllNonOwnedItemPatch.WholesaleDepth++;
    }

    /// <summary>批发买下的货已归玩家，不能留在不存档的货架上。</summary>
    private static void Postfix()
    {
        OverflowShelf.MoveOwnedOff();
    }

    private static void Finalizer()
    {
        GetAllNonOwnedItemPatch.WholesaleDepth--;
    }
}
