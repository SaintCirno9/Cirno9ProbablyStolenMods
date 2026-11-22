using System.Collections.Generic;
using UnityEngine;

namespace BatchOpenContainers;

/// <summary>Author: SaintCirno9。负责框选容器过滤、批量打开与窗口自动排列。</summary>
internal static class BatchOpenLogic
{
    private const float Margin = 8f;
    private const float TileGap = 6f;
    private const float CascadeStepX = 20f;
    private const float CascadeStepY = 20f;

    private static ItemMultiSelectHandler s_trackedHandler;
    private static List<GameItemElement> s_trackedSelectedItems;
    private static bool s_selectionReady;

    private static GameItem s_contextTargetItem;
    private static readonly List<GameItem> s_contextOpenableItems = new List<GameItem>();

    internal static void BindSelectionSource(ItemMultiSelectHandler handler, List<GameItemElement> selectedItems)
    {
        s_trackedHandler = handler;
        s_trackedSelectedItems = selectedItems;
    }

    internal static void SetSelectionReady(ItemMultiSelectHandler handler, bool ready)
    {
        if (handler == s_trackedHandler)
        {
            s_selectionReady = ready;
        }
    }

    internal static void ResetSession()
    {
        s_trackedHandler = null;
        s_trackedSelectedItems = null;
        s_selectionReady = false;
        ClearContextSnapshot();
    }

    internal static void ClearContextSnapshot()
    {
        s_contextTargetItem = null;
        s_contextOpenableItems.Clear();
    }

    internal static bool CanOpenContainer(GameItem item)
    {
        return item != null
            && !(item is GameItemElement element && element.IsDestroyed())
            && !GeneralHelper.IsDisplayOnly(item)
            && item.contentWindow != null
            && !item.contentWindow.isLocked
            && GeneralHelper.IsItemOwned(item);
    }

    /// <summary>在右键按下前处理框选批量打开或保存上下文菜单快照。</summary>
    internal static bool TryHandleSecondaryClick(GameItem hovered, Vector2 mousePosition)
    {
        ClearContextSnapshot();
        if (!BatchOpenMod.Active || hovered == null || GeneralHelper.IsDisplayOnly(hovered))
        {
            return false;
        }

        if (!TryGetActiveSelection(out List<GameItemElement> activeSelection))
        {
            return false;
        }

        if (!ContainsItem(activeSelection, hovered))
        {
            return false;
        }

        List<GameItem> openable = CollectOpenableContainers(activeSelection);
        if (openable.Count < 2)
        {
            return false;
        }

        bool shiftHeld = IsShiftHeld();
        if (BatchOpenMod.DirectRightClick.Value && !shiftHeld && CanOpenContainer(hovered) && HasAnyClosed(openable))
        {
            OpenContainers(openable, mousePosition);
            return true;
        }

        // 在菜单模式下按住 Shift 右键，保留单物品“打开”按钮以便只开单个容器。
        if (!BatchOpenMod.DirectRightClick.Value && shiftHeld)
        {
            return false;
        }

        // 记录快照，避免原版 ItemContextHandler.ResetAfter 在菜单显示前清空框选列表。
        s_contextTargetItem = hovered;
        s_contextOpenableItems.Clear();
        s_contextOpenableItems.AddRange(openable);
        return false;
    }

    internal static bool CanShowContextMenuOpenAll(GameItem item)
    {
        if (!BatchOpenMod.Active || item == null || !ReferenceEquals(item, s_contextTargetItem))
        {
            return false;
        }
        return CountValidOpenable(s_contextOpenableItems) >= 2;
    }

    internal static string GetContextMenuOpenAllLabel(GameItem item)
    {
        int count = CountValidOpenable(s_contextOpenableItems);
        return string.Format(BatchOpenMod.Text("ctx_open_all", "打开全部 ({0})"), count);
    }

    internal static void OnContextMenuOpenAll(GameItem item, Vector2 mousePosition)
    {
        if (!BatchOpenMod.Active || item == null || !ReferenceEquals(item, s_contextTargetItem))
        {
            ClearContextSnapshot();
            return;
        }

        var targets = new List<GameItem>(s_contextOpenableItems.Count);
        for (int i = 0; i < s_contextOpenableItems.Count; i++)
        {
            GameItem candidate = s_contextOpenableItems[i];
            if (CanOpenContainer(candidate))
            {
                targets.Add(candidate);
            }
        }
        ClearContextSnapshot();

        if (targets.Count > 0)
        {
            OpenContainers(targets, mousePosition);
        }
    }

    internal static void OpenContainers(List<GameItem> containers, Vector2 mousePosition)
    {
        ItemMouseDoubleClickHandler doubleClick = ItemMouseDoubleClickHandler.current;
        if (doubleClick == null || containers == null || containers.Count == 0)
        {
            return;
        }

        bool preserveSaved = BatchOpenMod.PreserveSavedPositions.Value;
        var toArrange = new List<GameItem>(containers.Count);

        for (int i = 0; i < containers.Count; i++)
        {
            GameItem item = containers[i];
            if (!CanOpenContainer(item))
            {
                continue;
            }

            PixelWindow window = item.contentWindow;
            bool wasVisible = window.IsVisible();
            bool hadSaved = HasSavedWindowPosition(item);

            doubleClick.OpenContentAction(item, mousePosition);

            if (!wasVisible && window.IsVisible() && (!preserveSaved || !hadSaved))
            {
                toArrange.Add(item);
            }
        }

        if (toArrange.Count > 1)
        {
            ArrangeWindows(toArrange, mousePosition);
        }
    }

    private static void ArrangeWindows(List<GameItem> items, Vector2 mousePosition)
    {
        int layoutMode = BatchOpenMod.LayoutMode.Value;
        if (layoutMode == 2)
        {
            return;
        }

        PixelWindow firstWindow = items[0].contentWindow;
        if (firstWindow == null || firstWindow.handler == null)
        {
            return;
        }

        RectTransform parentRect = firstWindow.rectTransform.parent as RectTransform;
        if (!TryGetCanvasBounds(firstWindow, parentRect, out Vector2 bounds))
        {
            return;
        }

        if (layoutMode == 1)
        {
            Vector2 translation = ItemMouseDoubleClickHandler.current != null
                ? ItemMouseDoubleClickHandler.current.openWindowTranslation
                : new Vector2(6f, -6f);
            Vector2 anchor = (Vector2)parentRect.InverseTransformPoint(mousePosition) + translation;

            float firstW = GetScaledWidth(firstWindow);
            float firstH = GetScaledHeight(firstWindow);
            float startX = Mathf.Clamp(anchor.x, Margin, Mathf.Max(Margin, bounds.x - firstW - Margin));
            float startY = Mathf.Clamp(anchor.y, -Mathf.Max(Margin, bounds.y - firstH - Margin), -Margin);
            ArrangeCascade(items, parentRect, bounds, startX, startY);
        }
        else
        {
            // 平铺模式固定从屏幕左上角开始铺排，避免右键下方容器时先占满底部再折返顶部。
            ArrangeTile(items, parentRect, bounds, Margin, -Margin);
        }
    }

    private static void ArrangeTile(List<GameItem> items, RectTransform sharedParent, Vector2 sharedBounds, float startX, float startY)
    {
        float curX = startX;
        float curY = startY;
        float rowMaxH = 0f;
        int wrapPass = 0;

        for (int i = 0; i < items.Count; i++)
        {
            PixelWindow window = items[i].contentWindow;
            if (window == null || window.handler == null || !window.IsVisible())
            {
                continue;
            }

            RectTransform parentRect = window.rectTransform.parent as RectTransform;
            Vector2 bounds = sharedBounds;
            if (parentRect != sharedParent && !TryGetCanvasBounds(window, parentRect, out bounds))
            {
                continue;
            }

            float winW = GetScaledWidth(window);
            float winH = GetScaledHeight(window);

            if (curX > Margin && curX + winW > bounds.x - Margin)
            {
                curX = Margin + (wrapPass % 4) * 16f;
                curY -= rowMaxH + TileGap;
                rowMaxH = 0f;
            }

            if (curY - winH < -bounds.y + Margin)
            {
                wrapPass++;
                float step = (wrapPass % 4) * 16f;
                curX = Margin + step;
                curY = -Margin - step;
                rowMaxH = 0f;
            }

            ApplyWindowPosition(window, new Vector2(curX, curY));
            curX += winW + TileGap;
            if (winH > rowMaxH)
            {
                rowMaxH = winH;
            }
        }
    }

    private static void ArrangeCascade(List<GameItem> items, RectTransform sharedParent, Vector2 sharedBounds, float startX, float startY)
    {
        float curX = startX;
        float curY = startY;
        int colPass = 0;

        for (int i = 0; i < items.Count; i++)
        {
            PixelWindow window = items[i].contentWindow;
            if (window == null || window.handler == null || !window.IsVisible())
            {
                continue;
            }

            RectTransform parentRect = window.rectTransform.parent as RectTransform;
            Vector2 bounds = sharedBounds;
            if (parentRect != sharedParent && !TryGetCanvasBounds(window, parentRect, out bounds))
            {
                continue;
            }

            float winW = GetScaledWidth(window);
            float winH = GetScaledHeight(window);

            if (curX + winW > bounds.x - Margin || curY - winH < -bounds.y + Margin)
            {
                colPass++;
                float maxShift = Mathf.Max(24f, bounds.x * 0.4f);
                curX = Margin + (colPass * 24f) % maxShift;
                curY = -Margin;
            }

            ApplyWindowPosition(window, new Vector2(curX, curY));
            curX += CascadeStepX;
            curY -= CascadeStepY;
        }
    }

    private static void ApplyWindowPosition(PixelWindow window, Vector2 anchoredPosition)
    {
        window.rectTransform.anchoredPosition = anchoredPosition;
        window.FixBounds();
        window.ToFront();
        Vector3 worldPos = window.rectTransform.position;
        window.rectTransform.position = new Vector3(Mathf.RoundToInt(worldPos.x), Mathf.RoundToInt(worldPos.y), worldPos.z);
    }

    private static bool TryGetCanvasBounds(PixelWindow window, RectTransform parentRect, out Vector2 bounds)
    {
        bounds = default;
        if (window == null || window.handler == null || parentRect == null)
        {
            return false;
        }
        Canvas rootCanvas = window.handler.GetComponentInParent<Canvas>(includeInactive: true);
        Canvas boundsCanvas = rootCanvas != null ? rootCanvas.rootCanvas : RenderHandler.current?.windowUICanvas;
        if (boundsCanvas == null)
        {
            return false;
        }
        Vector2 max = boundsCanvas.pixelRect.max;
        max.y = boundsCanvas.pixelRect.min.y;
        bounds = parentRect.InverseTransformPoint(max);
        bounds.y = -bounds.y;
        return bounds.x > 0f && bounds.y > 0f;
    }

    private static float GetScaledWidth(PixelWindow window)
    {
        float scale = Mathf.Abs(window.rectTransform.localScale.x);
        return Mathf.Max(1f, window.widthPixels * (scale > 0f ? scale : 1f));
    }

    private static float GetScaledHeight(PixelWindow window)
    {
        float scale = Mathf.Abs(window.rectTransform.localScale.y);
        return Mathf.Max(1f, window.heightPixels * (scale > 0f ? scale : 1f));
    }

    private static bool HasSavedWindowPosition(GameItem item)
    {
        Dictionary<int, Vector2> positions = PlayerStore.Instance?.contentWindowPositions;
        return positions != null && positions.ContainsKey(item.GetUniqueID());
    }

    private static bool TryGetActiveSelection(out List<GameItemElement> selection)
    {
        selection = null;
        ItemMultiSelectHandler current = ItemMultiSelectHandler.current;
        if (!s_selectionReady
            || current == null
            || !ReferenceEquals(current, s_trackedHandler)
            || !current.HasSelection
            || current.IsGroupDragging
            || Input.GetKey(current.key)
            || s_trackedSelectedItems == null
            || s_trackedSelectedItems.Count < 2)
        {
            return false;
        }
        selection = s_trackedSelectedItems;
        return true;
    }

    private static bool ContainsItem(List<GameItemElement> selection, GameItem target)
    {
        for (int i = 0; i < selection.Count; i++)
        {
            if (ReferenceEquals(selection[i], target))
            {
                return true;
            }
        }
        return false;
    }

    private static List<GameItem> CollectOpenableContainers(List<GameItemElement> selection)
    {
        var seen = new HashSet<GameItem>();
        var result = new List<GameItem>(selection.Count);
        for (int i = 0; i < selection.Count; i++)
        {
            GameItemElement element = selection[i];
            if (CanOpenContainer(element) && seen.Add(element))
            {
                result.Add(element);
            }
        }

        // 按所属库存与网格坐标（先上后下、先左后右）排序，让打开窗口顺序与框选视觉顺序一致。
        result.Sort(CompareItemsByGridPosition);
        return result;
    }

    private static int CompareItemsByGridPosition(GameItem a, GameItem b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }
        int invA = a.parentInventory != null ? a.parentInventory.GetHashCode() : 0;
        int invB = b.parentInventory != null ? b.parentInventory.GetHashCode() : 0;
        int invCmp = invA.CompareTo(invB);
        if (invCmp != 0)
        {
            return invCmp;
        }
        int yA = a.modifiedShape != null ? a.modifiedShape.minY : 0;
        int yB = b.modifiedShape != null ? b.modifiedShape.minY : 0;
        int yCmp = yA.CompareTo(yB);
        if (yCmp != 0)
        {
            return yCmp;
        }
        int xA = a.modifiedShape != null ? a.modifiedShape.minX : 0;
        int xB = b.modifiedShape != null ? b.modifiedShape.minX : 0;
        return xA.CompareTo(xB);
    }

    private static bool HasAnyClosed(List<GameItem> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            PixelWindow window = items[i].contentWindow;
            if (window != null && !window.IsVisible())
            {
                return true;
            }
        }
        return false;
    }

    private static int CountValidOpenable(List<GameItem> items)
    {
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (CanOpenContainer(items[i]))
            {
                count++;
            }
        }
        return count;
    }

    private static bool IsShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }
}
