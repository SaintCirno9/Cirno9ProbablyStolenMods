using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace TooltipAutoScale
{
    /// <summary>
    /// 提示框自适应排版与缩放核心逻辑：先在屏幕允许范围内适度加宽折行，再按需等比缩放与边界钳位。
    /// </summary>
    internal static class TooltipLayout
    {
        private const float DefaultScreenWidth = 960f;
        private const float DefaultScreenHeight = 540f;
        private const float ChromePadPx = 24f;
        private const float EdgeMarginPx = 4f;
        private const int MaxCandidateBuilderWidth = 116;
        private const int CandidateWidthStep = 12;

        // 标记被本模组适度加宽过的 RichText，便于 ShowTooltip 放宽对应的 maxCharWidth 裁剪上限
        private static readonly ConditionalWeakTable<RichText, object> WidenedTooltips = new ConditionalWeakTable<RichText, object>();
        private static readonly object WidenedMarker = new object();

        // 悬停期间同内容每帧重复构建的轻量单槽缓存，避免每帧多次试探 Build
        private static RichText _cachedInitial;
        private static int _cachedOrigMax;
        private static int _cachedComfortableLines;
        private static float _cachedMaxWidthPx;
        private static int _cachedTruncateLength;
        private static int _cachedChosenMax;

        // 重入保护：防止我们在 MaybeWiden 内再次调用 Build 时递归触发 Postfix
        internal static bool IsRebuilding { get; private set; }

        /// <summary>清空缓存状态。</summary>
        public static void Reset()
        {
            _cachedInitial = null;
            _cachedOrigMax = 0;
            _cachedComfortableLines = 0;
            _cachedMaxWidthPx = 0f;
            _cachedTruncateLength = -1;
            _cachedChosenMax = 0;
        }

        /// <summary>判断给定 RichText 是否由本模组适度加宽过。</summary>
        public static bool WasWidened(RichText richText)
        {
            return richText != null && WidenedTooltips.TryGetValue(richText, out _);
        }

        /// <summary>
        /// 第一阶段：当悬停提示框行数超过屏幕舒适高度上限时，在屏幕宽度允许范围内分步试探更宽的折行宽度，
        /// 找到刚好满足高度要求或尽可能降低行数的最小宽度。
        /// </summary>
        public static RichText MaybeWiden(RichTextBuilder builder, int truncateLength, RichText initial)
        {
            if (builder == null || initial == null || !TooltipAutoScaleMod.Active || !TooltipAutoScaleMod.AdaptiveWidenEnabled)
            {
                return initial;
            }

            GetScreenBudget(out float availW, out float availH, out float topReserve);
            float maxRatio = TooltipAutoScaleMod.MaxScreenRatio;
            int fontWidth = UISettings.current != null ? Math.Max(4, UISettings.current.fontWidth) : 8;
            int fontHeight = UISettings.current != null ? Math.Max(8, UISettings.current.fontHeight) : 16;
            float effectiveFontWidth = UISettings.current != null ? Mathf.Max(4f, UISettings.current.effectiveFontWidth) : fontWidth;

            float heightBudgetPx = Mathf.Min(availH * maxRatio, Mathf.Max(64f, availH - topReserve - EdgeMarginPx * 2f));
            int comfortableLines = Mathf.Max(6, Mathf.FloorToInt((heightBudgetPx - ChromePadPx) / fontHeight));
            if (initial.recommendedHeight <= comfortableLines)
            {
                return initial;
            }

            float widthLimitPx = Mathf.Max(200f, availW * maxRatio - ChromePadPx);
            int initialVisualPx = EstimateMaxLinePixels(initial.nakedText, fontWidth) + 20;
            float initialClampPx = initial.recommendedWidth * effectiveFontWidth + 20f;
            float maxAllowedVisualPx = Mathf.Max(widthLimitPx, initialVisualPx);
            float maxAllowedClampPx = Mathf.Max(widthLimitPx, initialClampPx);

            int origMax = builder.maxCharWidth;
            if (origMax >= MaxCandidateBuilderWidth)
            {
                return initial;
            }

            if (_cachedInitial != null
                && initial.QuickEquals(_cachedInitial)
                && _cachedOrigMax == origMax
                && _cachedComfortableLines == comfortableLines
                && Mathf.Approximately(_cachedMaxWidthPx, maxAllowedVisualPx)
                && _cachedTruncateLength == truncateLength)
            {
                if (_cachedChosenMax <= origMax)
                {
                    return initial;
                }

                RichText cachedBuild = RebuildAtWidth(builder, _cachedChosenMax, origMax, truncateLength);
                if (cachedBuild != null)
                {
                    MarkWidened(cachedBuild);
                    return cachedBuild;
                }
                return initial;
            }

            RichText best = initial;
            int bestWidth = origMax;

            IsRebuilding = true;
            try
            {
                for (int candidateMax = origMax + CandidateWidthStep;
                     candidateMax <= MaxCandidateBuilderWidth;
                     candidateMax += CandidateWidthStep)
                {
                    builder.maxCharWidth = candidateMax;
                    RichText candidate = builder.Build(truncateLength);
                    if (candidate == null)
                    {
                        break;
                    }

                    int candidateVisualPx = EstimateMaxLinePixels(candidate.nakedText, fontWidth) + 20;
                    float candidateClampPx = candidate.recommendedWidth * effectiveFontWidth + 20f;
                    if (candidateVisualPx > maxAllowedVisualPx || candidateClampPx > maxAllowedClampPx)
                    {
                        break;
                    }

                    // 只有真正减少了行数才采用更宽布局，避免对硬换行短行列表增加无效留白
                    if (candidate.recommendedHeight < best.recommendedHeight)
                    {
                        best = candidate;
                        bestWidth = candidateMax;
                        if (best.recommendedHeight <= comfortableLines)
                        {
                            break;
                        }
                    }
                }
            }
            finally
            {
                builder.maxCharWidth = origMax;
                IsRebuilding = false;
            }

            _cachedInitial = initial;
            _cachedOrigMax = origMax;
            _cachedComfortableLines = comfortableLines;
            _cachedMaxWidthPx = maxAllowedVisualPx;
            _cachedTruncateLength = truncateLength;
            _cachedChosenMax = bestWidth;

            if (!ReferenceEquals(best, initial) && bestWidth > origMax)
            {
                MarkWidened(best);
                return best;
            }

            return initial;
        }

        /// <summary>
        /// 第二阶段：当提示框加宽后仍然超出最大占屏比例时，对 tooltipWindow 整体等比缩小，
        /// 并基于缩放后的实际尺寸与当前生效的定位模式（原版或 3834796164 浮窗位置优化）重算光标贴靠与屏幕边界钳位。
        /// </summary>
        public static void ApplyScaleAndBounds(
            PixelWindow window,
            Vector2 mousePosition,
            Vector2 worldTranslation,
            bool hasMousePosition)
        {
            if (window == null || window.rectTransform == null)
            {
                return;
            }

            RectTransform rt = window.rectTransform;
            RectTransform parentRect = rt.parent as RectTransform;
            if (parentRect == null)
            {
                rt.localScale = Vector3.one;
                return;
            }

            Canvas boundsCanvas = ResolveBoundsCanvas(window);
            GetCanvasBounds(parentRect, boundsCanvas, out float minX, out float maxX, out float minY, out float maxY);
            float availW = Mathf.Max(64f, maxX - minX);
            float availH = Mathf.Max(64f, maxY - minY);

            int contentW = window.childElement != null ? window.childElement.widthPixels : window.widthPixels;
            int contentH = window.childElement != null ? window.childElement.heightPixels : window.heightPixels;
            float unscaledW = window.widthPixels > 0 ? window.widthPixels : Mathf.Max(1f, window.xPadding * 2 + contentW);
            float unscaledH = window.heightPixels > 0 ? window.heightPixels : Mathf.Max(1f, window.yPadding * 2 + contentH);

            float maxRatio = TooltipAutoScaleMod.MaxScreenRatio;
            float minScale = TooltipAutoScaleMod.MinScaleRatio;
            float leftLimit = minX + EdgeMarginPx;
            float rightLimit = maxX - EdgeMarginPx;
            float bottomLimit = minY + EdgeMarginPx;
            float topReserveMax = GetTopExtraReserve(parentRect, 1f);
            float safeAvailHMax = Mathf.Max(64f, maxY - EdgeMarginPx - topReserveMax - bottomLimit);
            float limitW = Mathf.Max(64f, availW * maxRatio - EdgeMarginPx * 2f);
            float limitH = Mathf.Max(64f, Mathf.Min(availH * maxRatio - EdgeMarginPx * 2f, safeAvailHMax));

            float scale = 1f;
            if (unscaledW > limitW || unscaledH > limitH)
            {
                scale = Mathf.Clamp(Mathf.Min(limitW / unscaledW, limitH / unscaledH), minScale, 1f);
            }

            rt.localScale = new Vector3(scale, scale, 1f);

            float scaledW = unscaledW * scale;
            float scaledH = unscaledH * scale;
            float topReserve = GetTopExtraReserve(parentRect, scale);
            float topLimit = maxY - EdgeMarginPx - topReserve;
            if (topLimit - bottomLimit < 64f)
            {
                topLimit = Mathf.Max(bottomLimit + 64f, maxY - EdgeMarginPx);
            }

            Camera cam = boundsCanvas != null && boundsCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? boundsCanvas.worldCamera
                : null;
            Vector2 mouseLocal = Vector2.zero;
            bool haveMouse = hasMousePosition
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, mousePosition, cam, out mouseLocal);
            if (!haveMouse)
            {
                // 仅从 parentRect 本地位置回退推算光标；worldTranslation 本身已是本地偏移，不能再做 InverseTransformVector
                Vector3 currentLocal = rt.localPosition;
                mouseLocal = new Vector2(currentLocal.x - worldTranslation.x, currentLocal.y - worldTranslation.y);
            }

            GetStableTooltipSideFlags(out bool stableH, out bool stableV);

            float finalX;
            if (stableH && haveMouse && boundsCanvas != null)
            {
                float offsetX = Mathf.Max(0f, worldTranslation.x);
                bool placeRight = mousePosition.x < boundsCanvas.pixelRect.center.x;
                float desiredX = placeRight ? mouseLocal.x + offsetX : mouseLocal.x - offsetX - scaledW;
                finalX = Mathf.Clamp(desiredX, leftLimit, Mathf.Max(leftLimit, rightLimit - scaledW));
            }
            else
            {
                finalX = ResolveHorizontalPosition(
                    mouseLocal.x + worldTranslation.x, mouseLocal.x, worldTranslation.x, scaledW, leftLimit, rightLimit);
            }

            float finalY;
            if (scaledH > topLimit - bottomLimit)
            {
                // 即使触及最小缩放下限仍高于整屏时，优先贴齐屏幕上沿以保住标题和前段说明
                finalY = topLimit;
            }
            else if (stableV && haveMouse && boundsCanvas != null)
            {
                float offsetY = Mathf.Max(0f, worldTranslation.y);
                bool placeBelow = mousePosition.y > boundsCanvas.pixelRect.center.y;
                float desiredY = placeBelow ? mouseLocal.y + offsetY : mouseLocal.y + scaledH + offsetY;
                finalY = Mathf.Clamp(desiredY, bottomLimit + scaledH, topLimit);
            }
            else
            {
                bool horizClearsCursor = finalX >= mouseLocal.x + 1f || finalX + scaledW <= mouseLocal.x - 1f;
                finalY = ResolveVerticalPosition(
                    mouseLocal.y + worldTranslation.y, mouseLocal.y, worldTranslation.y, scaledH, bottomLimit, topLimit, horizClearsCursor);
            }

            Vector3 localPos = rt.localPosition;
            rt.localPosition = new Vector3(finalX, finalY, localPos.z);

            if (boundsCanvas == null || boundsCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                Vector3 pos = rt.position;
                rt.position = new Vector3(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y), pos.z);
            }
        }

        private static bool IsExternalModRunning(string modId)
        {
            if (string.IsNullOrEmpty(modId) || !ModLoader.IsModEnabled(modId))
            {
                return false;
            }

            foreach (LoadedMod mod in ModLoader.Mods)
            {
                if (mod != null && mod.IsEnabled && mod.Manifest != null && mod.Manifest.ID == modId)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsItemDetailsTypeBadgeActive()
        {
            if (!IsExternalModRunning("item_details"))
            {
                return false;
            }

            ModConfig cfg = ModConfig.Find("item_details");
            if (cfg == null)
            {
                return true;
            }

            ConfigEntry infoEnhanceEntry = cfg.Get("show_info_enhance");
            if (infoEnhanceEntry?.BoxedValue is bool infoEnhance && !infoEnhance)
            {
                return false;
            }

            ConfigEntry typeBadgeEntry = cfg.Get("show_type_badge");
            return typeBadgeEntry?.BoxedValue is bool showBadge ? showBadge : true;
        }

        /// <summary>
        /// 为贴附在提示框顶部外侧的类型标签（如 ItemDetails 的 ItemDetailsTypeBadge）预留顶部安全高度，避免顶边贴屏时标签被裁出屏幕。
        /// </summary>
        private static float GetTopExtraReserve(RectTransform parentRect, float tooltipScale)
        {
            if (!IsItemDetailsTypeBadgeActive())
            {
                return 0f;
            }

            int fontHeight = UISettings.current != null ? Math.Max(12, UISettings.current.fontHeight) : 16;
            float badgeHeight;
            Transform existingBadge = parentRect != null ? parentRect.Find("ItemDetailsTypeBadge") : null;
            if (existingBadge is RectTransform badgeRect && badgeRect.sizeDelta.y > 0f)
            {
                badgeHeight = Mathf.Max(badgeRect.sizeDelta.y, fontHeight + 8f);
            }
            else
            {
                badgeHeight = Mathf.Max(28f, fontHeight + 12f);
            }

            // ItemDetails.TypeBadge 将未缩放标签中心摆放在 tooltipTop + (4 + badgeH/2) * scale，再加上上半高 badgeH/2 与 2px 余量
            return 4f * tooltipScale + badgeHeight * 0.5f * (1f + tooltipScale) + 2f;
        }

        private static void GetStableTooltipSideFlags(out bool horizontal, out bool vertical)
        {
            horizontal = false;
            vertical = false;

            if (!IsExternalModRunning("stable_tooltip_side"))
            {
                return;
            }

            ModConfig cfg = ModConfig.Find("stable_tooltip_side");
            ConfigEntry hEntry = cfg?.Get("horizontal_alignment");
            ConfigEntry vEntry = cfg?.Get("vertical_alignment");
            horizontal = hEntry?.BoxedValue is bool hb ? hb : true;
            vertical = vEntry?.BoxedValue is bool vb ? vb : true;
        }

        private static float ResolveHorizontalPosition(
            float primaryX, float cursorX, float offsetX, float scaledW, float leftLimit, float rightLimit)
        {
            float availSafeW = rightLimit - leftLimit;
            if (scaledW >= availSafeW)
            {
                return leftLimit;
            }

            if (primaryX >= leftLimit && primaryX + scaledW <= rightLimit)
            {
                return primaryX;
            }

            // 右侧溢出时对称翻转到光标另一侧，保持与光标相同的本地间距
            float flippedX = offsetX >= 0f
                ? cursorX - offsetX - scaledW
                : cursorX - offsetX;
            if (flippedX >= leftLimit && flippedX + scaledW <= rightLimit)
            {
                return flippedX;
            }

            if (primaryX < leftLimit)
            {
                return leftLimit;
            }

            // 两侧都无法保留完整间距时，优先放在剩余空间更大的一侧以避让或最小化遮挡光标
            float spaceRight = rightLimit - cursorX;
            float spaceLeft = cursorX - leftLimit;
            return spaceRight >= spaceLeft ? rightLimit - scaledW : leftLimit;
        }

        private static float ResolveVerticalPosition(
            float primaryY, float cursorY, float offsetY, float scaledH, float bottomLimit, float topLimit, bool horizClearsCursor)
        {
            float availSafeH = topLimit - bottomLimit;
            if (scaledH > availSafeH)
            {
                // 即使触及最小缩放下限仍高于整屏时，优先贴齐屏幕上沿以保住标题和前段说明
                return topLimit;
            }

            if (primaryY - scaledH < bottomLimit)
            {
                if (!horizClearsCursor)
                {
                    // 仅当水平方向无法避开光标时，优先整体翻到光标上方以免遮挡鼠标目标
                    float gapY = offsetY < -1f ? -offsetY : 12f;
                    float flippedY = cursorY + gapY + scaledH;
                    if (flippedY <= topLimit)
                    {
                        return flippedY;
                    }
                }

                // 水平已错开光标或上方空间不足时，仅上移最小幅度贴齐屏幕底缘，避免长框被甩离悬停目标
                return Mathf.Clamp(primaryY, bottomLimit + scaledH, topLimit);
            }

            return Mathf.Min(primaryY, topLimit);
        }

        private static RichText RebuildAtWidth(RichTextBuilder builder, int targetMaxWidth, int origMax, int truncateLength)
        {
            IsRebuilding = true;
            try
            {
                builder.maxCharWidth = targetMaxWidth;
                return builder.Build(truncateLength);
            }
            finally
            {
                builder.maxCharWidth = origMax;
                IsRebuilding = false;
            }
        }

        private static void MarkWidened(RichText richText)
        {
            if (richText == null)
            {
                return;
            }

            WidenedTooltips.Remove(richText);
            WidenedTooltips.Add(richText, WidenedMarker);
        }

        private static void GetScreenBudget(out float availW, out float availH, out float topReserve)
        {
            PixelWindow tooltipWindow = OverlayHandler.current != null
                ? OverlayHandler.current.GetTooltipWindow()
                : null;
            RectTransform parentRect = tooltipWindow != null && tooltipWindow.rectTransform != null
                ? tooltipWindow.rectTransform.parent as RectTransform
                : null;

            topReserve = GetTopExtraReserve(parentRect, 1f);

            if (tooltipWindow != null && parentRect != null)
            {
                Canvas boundsCanvas = ResolveBoundsCanvas(tooltipWindow);
                GetCanvasBounds(parentRect, boundsCanvas, out float minX, out float maxX, out float minY, out float maxY);
                availW = Mathf.Max(64f, maxX - minX);
                availH = Mathf.Max(64f, maxY - minY);
                return;
            }

            Canvas canvas = RenderHandler.current != null
                ? (RenderHandler.current.OverlayCanvas ?? RenderHandler.current.windowUICanvas)
                : null;
            if (canvas != null && canvas.scaleFactor > 0.01f)
            {
                Rect px = canvas.pixelRect;
                availW = Mathf.Max(200f, px.width / canvas.scaleFactor);
                availH = Mathf.Max(150f, px.height / canvas.scaleFactor);
                return;
            }

            availW = DefaultScreenWidth;
            availH = DefaultScreenHeight;
        }

        private static Canvas ResolveBoundsCanvas(PixelWindow window)
        {
            Canvas boundsCanvas = window != null && window.rectTransform != null
                ? window.rectTransform.GetComponentInParent<Canvas>(true)?.rootCanvas
                : null;
            if (boundsCanvas == null && RenderHandler.current != null)
            {
                boundsCanvas = RenderHandler.current.OverlayCanvas ?? RenderHandler.current.windowUICanvas;
            }
            return boundsCanvas;
        }

        private static void GetCanvasBounds(
            RectTransform parentRect,
            Canvas boundsCanvas,
            out float minX,
            out float maxX,
            out float minY,
            out float maxY)
        {
            if (boundsCanvas != null && parentRect != null)
            {
                Rect pixelRect = boundsCanvas.pixelRect;
                Camera cam = boundsCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : boundsCanvas.worldCamera;

                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parentRect, new Vector2(pixelRect.xMin, pixelRect.yMax), cam, out Vector2 topLeft)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        parentRect, new Vector2(pixelRect.xMax, pixelRect.yMin), cam, out Vector2 bottomRight))
                {
                    float lx0 = Mathf.Min(topLeft.x, bottomRight.x);
                    float lx1 = Mathf.Max(topLeft.x, bottomRight.x);
                    float ly0 = Mathf.Min(topLeft.y, bottomRight.y);
                    float ly1 = Mathf.Max(topLeft.y, bottomRight.y);
                    if (lx1 - lx0 > 32f && ly1 - ly0 > 32f)
                    {
                        minX = lx0;
                        maxX = lx1;
                        minY = ly0;
                        maxY = ly1;
                        return;
                    }
                }
            }

            minX = 0f;
            maxX = DefaultScreenWidth;
            minY = 0f;
            maxY = DefaultScreenHeight;
        }

        private static int EstimateMaxLinePixels(string nakedText, int fontWidth)
        {
            if (string.IsNullOrEmpty(nakedText))
            {
                return 0;
            }

            int maxPx = 0;
            int linePx = 0;
            for (int i = 0; i < nakedText.Length; i++)
            {
                char c = nakedText[i];
                if (c == '\n' || c == '\r')
                {
                    if (linePx > maxPx)
                    {
                        maxPx = linePx;
                    }
                    linePx = 0;
                    continue;
                }

                if (char.IsControl(c))
                {
                    continue;
                }

                // 全角/中日韩字符按双倍基准字符宽估算，与游戏双倍宽高字形一致
                linePx += c > '\u00FF' ? fontWidth * 2 : fontWidth;
            }

            if (linePx > maxPx)
            {
                maxPx = linePx;
            }

            return maxPx;
        }
    }
}
