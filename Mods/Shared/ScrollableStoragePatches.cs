using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Cirno9.Storage;

/// <summary>原版鼠标滚轮只认滚动库存；只在两个滚轮处理入口将真实格子映射到显示组件。</summary>
[HarmonyPatch]
internal static class StorageScrollInputPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.DeclaredMethod(typeof(ItemMouseHandler), "OnEventMouseScroll",
            new[] { typeof(UnityEngine.Vector2) });
        yield return AccessTools.DeclaredMethod(typeof(ItemMouseDragHandler), "OnEventMouseScroll",
            new[] { typeof(UnityEngine.Vector2) });
    }

    private static GameInventory ScrollView(GameInventory inventory) =>
        inventory is ScrollableGridInventory grid ? grid.Viewport : inventory;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int extended = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Isinst && Equals(instruction.operand, typeof(GameGridScrollableInventory)))
            {
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(StorageScrollInputPatch), nameof(ScrollView)))
                    .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                extended++;
            }
            yield return instruction;
        }
        if (extended == 0)
        {
            throw new InvalidOperationException("原版鼠标滚轮的库存检测接口已变化，无法接入滚动储藏区。");
        }
    }
}

[HarmonyPatch(typeof(InventorySortHelper), nameof(InventorySortHelper.Sort))]
internal static class StorageSortScrollPatch
{
    private static void Postfix(GameGridInventory inventory, bool fromEnd, bool __result)
    {
        if (__result && inventory is ScrollableGridInventory grid)
        {
            grid.SetScroll(fromEnd ? 1f : 0f);
        }
    }
}

/// <summary>智能排列自身没有给附加行更新悬停颜色，只为本模组库存补齐原版按钮反馈。</summary>
[HarmonyPatch(typeof(ItemContextHandler), "UpdateSortSelectButtonColor")]
internal static class SmartSortHoverPatch
{
    private static FieldInfo s_button;
    private static FieldInfo s_preserveButton;
    private static bool s_warned;

    private static void Postfix(RichTextElement selectedButton, GameGridInventory ___currentInventory,
        bool ___canSort, RenderHandler.ColorPalette ___textColor, RenderHandler.ColorPalette ___textSelectColor,
        RenderHandler.ColorPalette ___textDisabledColor, RenderHandler.ColorPalette ___textSelectDisabledColor)
    {
        if (!(___currentInventory is ScrollableGridInventory))
        {
            return;
        }
        try
        {
            // 只访问已核对的可选模组类型和两个固定字段；不改算法、归属或可用性校验。
            Type menu = AccessTools.TypeByName("SmartInventoryArrange.NativeSmartMenu");
            if (menu == null)
            {
                return;
            }
            s_button ??= AccessTools.Field(menu, "Button");
            s_preserveButton ??= AccessTools.Field(menu, "PreserveButton");
            if (s_button == null || s_preserveButton == null)
            {
                throw new MissingFieldException("SmartInventoryArrange.NativeSmartMenu", "Button / PreserveButton");
            }
            Paint(s_button.GetValue(null) as RichTextElement);
            Paint(s_preserveButton.GetValue(null) as RichTextElement);
        }
        catch (Exception exception)
        {
            if (!s_warned)
            {
                s_warned = true;
                UnityEngine.Debug.LogWarning("[Cirno9 Storage] 智能排列按钮悬停兼容失败：" + exception);
            }
        }

        void Paint(RichTextElement button)
        {
            if (button == null)
            {
                return;
            }
            bool hovered = button == selectedButton;
            RenderHandler.ColorPalette color = ___canSort
                ? (hovered ? ___textSelectColor : ___textColor)
                : (hovered ? ___textSelectDisabledColor : ___textDisabledColor);
            button.GetText().text.color = RenderHandler.GetColorFromPalette(color);
        }
    }
}
