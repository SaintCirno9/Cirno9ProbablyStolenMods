using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CounterOverflowShelf;

/// <summary>复用原版按下状态、改键与鼠标命中校验，只补充货架相关的 Alt/Ctrl 转移。</summary>
internal static class ShelfQuickTransfer
{
    internal static bool OnRelease(InputActionHandler handler, KeyCode clickKey, KeyCode transferKey,
        ISet<KeyCode> keysPressed, ref GameItem currentItem, ref bool isQuickTransfer)
    {
        if (!isQuickTransfer || !keysPressed.Contains(clickKey)
            || !(currentItem is GameItemElement item) || !OverflowShelf.CanQuickTransfer(item))
        {
            return true;
        }
        if (RenderHandler.RaycastElement<GameItemElement>(Input.mousePosition) != item || !item.MayRemove())
        {
            return true;
        }
        AudioManager.Instance.TryPlayOne(item.soundDragStart, item.soundDragEnd);
        OverflowShelf.QuickTransfer(item);
        // 已处理的点击不再交给原版，否则可能将刚转移的货物再次移走。
        InputActionManager.current.ResetAfter(handler);
        currentItem = null;
        if (keysPressed.Contains(transferKey))
        {
            isQuickTransfer = false;
        }
        return false;
    }
}

[HarmonyPatch(typeof(ItemQuickTransferHandler), "OnEventRelease")]
internal static class ShelfInventoryQuickTransferPatch
{
    private static bool Prefix(ItemQuickTransferHandler __instance, ISet<KeyCode> keysPressed,
        ref GameItem ___currentItem, ref bool ___isQuickTransfer)
    {
        return ShelfQuickTransfer.OnRelease(__instance, __instance.key, __instance.quickTransferKey,
            keysPressed, ref ___currentItem, ref ___isQuickTransfer);
    }
}

[HarmonyPatch(typeof(ItemQuickTransferAltHandler1), "OnEventRelease")]
internal static class ShelfStorageQuickTransferPatch
{
    private static bool Prefix(ItemQuickTransferAltHandler1 __instance, ISet<KeyCode> keysPressed,
        ref GameItem ___currentItem, ref bool ___isQuickTransfer)
    {
        return ShelfQuickTransfer.OnRelease(__instance, __instance.key, __instance.quickTransferKey,
            keysPressed, ref ___currentItem, ref ___isQuickTransfer);
    }
}
