using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BatchOpenContainers;

[HarmonyPatch(typeof(ItemMultiSelectHandler), "Awake")]
internal static class MultiSelectAwakePatch
{
    private static void Postfix(ItemMultiSelectHandler __instance, List<GameItemElement> ___selectedItems)
    {
        BatchOpenLogic.BindSelectionSource(__instance, ___selectedItems);
        BatchOpenLogic.SetSelectionReady(__instance, false);
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventInit")]
internal static class MultiSelectInitPatch
{
    private static void Postfix(ItemMultiSelectHandler __instance, List<GameItemElement> ___selectedItems)
    {
        BatchOpenLogic.BindSelectionSource(__instance, ___selectedItems);
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "FinalizeBand")]
internal static class MultiSelectFinalizeBandPatch
{
    private static void Postfix(ItemMultiSelectHandler __instance, List<GameItemElement> ___selectedItems)
    {
        BatchOpenLogic.BindSelectionSource(__instance, ___selectedItems);
        BatchOpenLogic.SetSelectionReady(__instance, ___selectedItems != null && ___selectedItems.Count > 0);
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "StartGroupDrag")]
internal static class MultiSelectStartGroupDragPatch
{
    private static void Prefix(ItemMultiSelectHandler __instance)
    {
        BatchOpenLogic.SetSelectionReady(__instance, false);
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "OnEventReset")]
internal static class MultiSelectResetPatch
{
    private static void Postfix(ItemMultiSelectHandler __instance)
    {
        BatchOpenLogic.SetSelectionReady(__instance, false);
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "ClearSelection")]
internal static class MultiSelectClearSelectionPatch
{
    private static void Postfix(ItemMultiSelectHandler __instance)
    {
        BatchOpenLogic.SetSelectionReady(__instance, false);
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "PruneSelection")]
internal static class MultiSelectPrunePatch
{
    private static void Postfix(ItemMultiSelectHandler __instance)
    {
        if (!__instance.HasSelection)
        {
            BatchOpenLogic.SetSelectionReady(__instance, false);
        }
    }
}

[HarmonyPatch(typeof(ItemMultiSelectHandler), "CustomCancel")]
internal static class MultiSelectCustomCancelPatch
{
    private static void Postfix(ItemMultiSelectHandler __instance)
    {
        if (!__instance.HasSelection)
        {
            BatchOpenLogic.SetSelectionReady(__instance, false);
        }
    }
}

[HarmonyPatch(typeof(ItemContextHandler), "OnEventPress")]
internal static class ContextPressBatchOpenPatch
{
    private static bool Prefix(ItemContextHandler __instance, ISet<KeyCode> keysPressed)
    {
        if (!BatchOpenMod.Active || !keysPressed.Contains(__instance.triggerKey))
        {
            return true;
        }
        BatchOpenLogic.ClearContextSnapshot();
        if ((ItemSelectHandler.current != null && ItemSelectHandler.current.IsEquipped)
            || ItemPinHelper.BlocksClicks
            || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem))
        {
            return true;
        }

        Vector2 mousePosition = Input.mousePosition;
        GameItem hovered = RenderHandler.RaycastElement<GameItem>(mousePosition);
        if (BatchOpenLogic.TryHandleSecondaryClick(hovered, mousePosition))
        {
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(ItemContextHandler), "OnEventUpdate")]
internal static class ContextUpdateBatchOpenPatch
{
    private static void Postfix(bool __result, GameItem ___currentItem)
    {
        if (!__result && ___currentItem == null)
        {
            BatchOpenLogic.ClearContextSnapshot();
        }
    }
}

/// <summary>在保留原版及第三方右键菜单结构的前提下，将选中的“打开”按钮文案替换为“打开全部 (N)”。</summary>
[HarmonyPatch(typeof(ItemContextHandler), "SetButtonColor")]
internal static class ContextSetButtonColorPatch
{
    private static void Postfix(
        RichTextElement button,
        RichTextElement selectedButton,
        bool isEnabled,
        RichTextElement ___buttonOpen,
        GameItem ___currentItem,
        bool ___canOpen,
        RenderHandler.ColorPalette ___textColor,
        RenderHandler.ColorPalette ___textSelectColor)
    {
        if (!BatchOpenMod.Active || button == null || button != ___buttonOpen || !___canOpen || !isEnabled)
        {
            return;
        }
        if (!BatchOpenLogic.CanShowContextMenuOpenAll(___currentItem))
        {
            return;
        }

        RenderHandler.ColorPalette color = button == selectedButton ? ___textSelectColor : ___textColor;
        button.SetText(BatchOpenLogic.GetContextMenuOpenAllLabel(___currentItem), 10000, color);
    }
}

/// <summary>点击右键菜单中的“打开全部 (N)”时，一次性打开所有选中的可打开容器。</summary>
[HarmonyPatch(typeof(ItemContextHandler), "TriggerButton")]
internal static class ContextTriggerButtonPatch
{
    private static bool Prefix(
        ItemContextHandler __instance,
        RichTextElement button,
        RichTextElement ___buttonOpen,
        GameItem ___currentItem,
        bool ___canOpen)
    {
        if (!BatchOpenMod.Active || button == null || button != ___buttonOpen || !___canOpen)
        {
            return true;
        }
        if (!BatchOpenLogic.CanShowContextMenuOpenAll(___currentItem))
        {
            return true;
        }

        GameItem target = ___currentItem;
        Vector2 mousePosition = Input.mousePosition;
        __instance.Stop();
        BatchOpenLogic.OnContextMenuOpenAll(target, mousePosition);
        return false;
    }
}
