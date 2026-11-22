using System;
using HarmonyLib;
using UnityEngine;

namespace TooltipAutoScale
{
    /// <summary>
    /// 限制“先加宽折行”仅作用于物品/机器悬停提示框生成阶段，避免影响对话或书籍等非提示框文本。
    /// </summary>
    internal static class TooltipWidenScope
    {
        private static int _depth;

        public static bool IsActive => _depth > 0;

        public static void Enter()
        {
            _depth++;
        }

        public static Exception Exit(Exception __exception)
        {
            if (_depth > 0)
            {
                _depth--;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ItemTooltipHandler), "OnEventUpdate")]
    internal static class ItemTooltipUpdateScopePatch
    {
        private static void Prefix() => TooltipWidenScope.Enter();
        private static Exception Finalizer(Exception __exception) => TooltipWidenScope.Exit(__exception);
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.GetTooltip))]
    internal static class GameItemTooltipScopePatch
    {
        private static void Prefix() => TooltipWidenScope.Enter();
        private static Exception Finalizer(Exception __exception) => TooltipWidenScope.Exit(__exception);
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.GetTooltipAdvanced))]
    internal static class GameItemAdvancedTooltipScopePatch
    {
        private static void Prefix() => TooltipWidenScope.Enter();
        private static Exception Finalizer(Exception __exception) => TooltipWidenScope.Exit(__exception);
    }

    [HarmonyPatch(typeof(RichTextBuilder), nameof(RichTextBuilder.Build), typeof(int))]
    internal static class RichTextBuilderWidenPatch
    {
        private static void Postfix(RichTextBuilder __instance, int truncateLength, ref RichText __result)
        {
            if (TooltipLayout.IsRebuilding || !TooltipWidenScope.IsActive)
            {
                return;
            }

            __result = TooltipLayout.MaybeWiden(__instance, truncateLength, __result);
        }
    }

    [HarmonyPatch(typeof(OverlayHandler), nameof(OverlayHandler.ShowTooltip), typeof(Vector2), typeof(RichText), typeof(int))]
    internal static class OverlayShowTooltipShortPatch
    {
        private static void Prefix(RichText richText, ref int maxCharWidth)
        {
            if (TooltipAutoScaleMod.Active && TooltipLayout.WasWidened(richText) && richText.recommendedWidth > maxCharWidth)
            {
                maxCharWidth = richText.recommendedWidth;
            }
        }
    }

    [HarmonyPatch(typeof(OverlayHandler), nameof(OverlayHandler.ShowTooltip), typeof(Vector2), typeof(Vector2), typeof(RichText), typeof(int))]
    internal static class OverlayShowTooltipFullPatch
    {
        private static void Prefix(RichText richText, ref int maxCharWidth)
        {
            if (TooltipAutoScaleMod.Active && TooltipLayout.WasWidened(richText) && richText.recommendedWidth > maxCharWidth)
            {
                maxCharWidth = richText.recommendedWidth;
            }
        }
    }

    [HarmonyPatch(typeof(OverlayHandler), nameof(OverlayHandler.ShowTooltip2), typeof(Vector2), typeof(Vector2), typeof(RichText), typeof(int))]
    internal static class OverlayShowTooltip2Patch
    {
        private static void Prefix(RichText richText, ref int maxCharWidth)
        {
            if (TooltipAutoScaleMod.Active && TooltipLayout.WasWidened(richText) && richText.recommendedWidth > maxCharWidth)
            {
                maxCharWidth = richText.recommendedWidth;
            }
        }
    }

    [HarmonyPatch(typeof(PixelWindow), "FixTooltipBounds", typeof(Vector2))]
    internal static class PixelWindowTooltipBoundsPatch
    {
        private static bool Prefix(PixelWindow __instance, Vector2 worldTranslation)
        {
            if (!IsTooltipWindow(__instance))
            {
                return true;
            }

            if (!TooltipAutoScaleMod.Active)
            {
                ResetTooltipScale(__instance);
                return true;
            }

            // 经由 PixelWindow.Show 调用时先跳过原版宽高截断与定位，等待 Show 末尾低优先级 Postfix 在兼容模组之后统一按缩放尺寸收口
            if (PixelWindowShowBoundsPatch.IsActive)
            {
                return false;
            }

            TooltipLayout.ApplyScaleAndBounds(__instance, Vector2.zero, worldTranslation, hasMousePosition: false);
            return false;
        }

        internal static bool IsTooltipWindow(PixelWindow window)
        {
            PixelWindow tooltipWindow = OverlayHandler.current != null
                ? OverlayHandler.current.GetTooltipWindow()
                : null;
            return window != null && window == tooltipWindow;
        }

        internal static void ResetTooltipScale(PixelWindow window)
        {
            if (window != null && window.rectTransform != null && window.rectTransform.localScale != Vector3.one)
            {
                window.rectTransform.localScale = Vector3.one;
            }
        }
    }

    [HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Show), typeof(Vector2), typeof(Vector2), typeof(bool))]
    internal static class PixelWindowShowBoundsPatch
    {
        private static int _depth;

        internal static bool IsActive => _depth > 0;

        private static void Prefix(PixelWindow __instance, bool fixTooltipBounds, out bool __state)
        {
            __state = fixTooltipBounds && TooltipAutoScaleMod.Active && PixelWindowTooltipBoundsPatch.IsTooltipWindow(__instance);
            if (__state)
            {
                _depth++;
            }
        }

        // 在其他浮窗位置模组（如 3834796164 StableTooltipSide）之后执行，使用准确光标与缩放后尺寸重新落点
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("mod.stable_tooltip_side")]
        private static void Postfix(PixelWindow __instance, Vector2 mousePosition, Vector2 worldTranslation, bool fixTooltipBounds)
        {
            if (!fixTooltipBounds || !PixelWindowTooltipBoundsPatch.IsTooltipWindow(__instance))
            {
                return;
            }

            if (!TooltipAutoScaleMod.Active)
            {
                PixelWindowTooltipBoundsPatch.ResetTooltipScale(__instance);
                return;
            }

            TooltipLayout.ApplyScaleAndBounds(__instance, mousePosition, worldTranslation, hasMousePosition: true);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && _depth > 0)
            {
                _depth--;
            }
            return __exception;
        }
    }
}
