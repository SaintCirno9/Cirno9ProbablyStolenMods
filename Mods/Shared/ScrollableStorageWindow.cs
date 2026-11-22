using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cirno9.Storage;

/// <summary>
/// 带滚动格子库存的 Machinery 样式浮动窗口：封装不可关闭配置、装饰射线关闭、
/// 标题拖动、右下角按行缩放以及防止边框触发原版框选。
/// Author: SaintCirno9
/// </summary>
internal sealed class ScrollableStorageWindow
{
    private const float ResizeHandleSize = 10f;
    private const float CloseButtonHitSize = 20f;

    private enum Zone
    {
        None,
        Grid,
        Chrome,
        Resize
    }

    private enum DragMode
    {
        None,
        Move,
        Resize
    }

    private sealed class ChromeRaycastFilter : MonoBehaviour, ICanvasRaycastFilter
    {
        internal ScrollableStorageWindow Storage { get; set; }

        public bool IsRaycastLocationValid(Vector2 screenPosition, Camera eventCamera)
        {
            return Storage != null && Storage.IsOverChrome(screenPosition);
        }
    }

    private static readonly ConditionalWeakTable<PixelWindow, ScrollableStorageWindow> s_windows =
        new ConditionalWeakTable<PixelWindow, ScrollableStorageWindow>();

    private static readonly List<WeakReference<ScrollableStorageWindow>> s_instances =
        new List<WeakReference<ScrollableStorageWindow>>();

    private readonly bool m_closable;
    private readonly int m_minRows;
    private readonly int m_maxRows;
    private readonly Action<string> m_warn;
    private readonly TreeNodeRender m_pointerDragSurface;

    private bool m_wasVisible;
    private DragMode m_dragMode;
    private Vector2 m_pressMouse;
    private Vector3 m_pressPosition;
    private int m_pressRows;

    internal PixelWindow Window { get; }
    internal ScrollableGridInventory Inventory { get; }
    internal bool IsAlive => Window != null && Window.handler != null && Inventory != null;
    internal int VisibleRows => Inventory.GetVisibleHeight();

    /// <summary>玩家通过交互拖动调整显示行数后触发。</summary>
    internal event Action<int> OnResized;

    /// <summary>拖动移动或调整行数结束（含中途隐藏中断），且位置或行数相对按下时发生变化时触发。</summary>
    internal event Action OnDragCommitted;

    internal ScrollableStorageWindow(
        ScrollableGridInventory inventory,
        string title,
        bool closable = true,
        int minRows = 2,
        int maxRows = 30,
        string objectName = null,
        Action<string> warn = null,
        bool usePointerDrag = false)
    {
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        m_closable = closable;
        m_minRows = Math.Max(1, minRows);
        m_maxRows = Math.Max(m_minRows, maxRows);
        m_warn = warn;

        // 非可关闭窗口设为 canMove=false 与 isImportant=true，不占用原版 Esc 关闭队列与浮窗上限，
        // 同时保持在 visibleWindows 中供智能排列等原版工具识别。
        Window = new PixelWindow(canMove: closable, title, WindowStyle.Machinery);
        Window.isImportant = !closable;
        s_windows.Add(Window, this);
        s_instances.Add(new WeakReference<ScrollableStorageWindow>(this));
        Window.Attach(Inventory);
        Window.Validate();
        if (!string.IsNullOrEmpty(objectName))
        {
            Window.handler.gameObject.name = objectName;
        }
        ConfigureChrome();
        if (usePointerDrag)
        {
            m_pointerDragSurface = CreatePointerDragSurface();
            ResizePointerDragSurface();
        }
        Window.Hide();
    }

    internal void Show()
    {
        if (!IsAlive)
        {
            return;
        }
        if (!Window.IsVisible())
        {
            Window.Show();
            // canMove=false 的窗口创建时在底层且 Show 不会置前，显示时恢复到前排；显示期间不再抢占顺位。
            if (!m_closable)
            {
                Window.ToFront(force: true);
            }
            Inventory.RefreshClipping();
            m_wasVisible = true;
        }
    }

    internal void PositionBeside(PixelWindow anchor, int gap = 4)
    {
        if (!IsAlive || anchor == null || anchor.handler == null)
        {
            return;
        }
        float scale = Math.Max(0.1f, anchor.rectTransform.lossyScale.x);
        Vector3 position = anchor.rectTransform.position;
        float right = position.x + (anchor.widthPixels + gap) * scale;
        float x = right + Window.widthPixels * scale <= Screen.width
            ? right
            : position.x - (Window.widthPixels + gap) * scale;
        Window.rectTransform.position = new Vector3(x, position.y, Window.rectTransform.position.z);
        Window.FixBounds();
    }

    internal void Hide()
    {
        EndDrag();
        m_wasVisible = false;
        if (IsAlive && Window.IsVisible())
        {
            Window.Hide();
        }
    }

    internal void Destroy()
    {
        EndDrag();
        m_wasVisible = false;
        RemoveInstance(this);
        if (IsAlive)
        {
            Window.Destroy();
        }
    }

    internal bool SetVisibleRows(int rows, bool clampToScreen = false)
    {
        return SetVisibleRowsInternal(rows, clampToScreen, fromInteraction: false);
    }

    internal void Tick()
    {
        if (!IsAlive || !Window.IsVisible())
        {
            m_wasVisible = false;
            EndDrag();
            return;
        }
        if (!m_wasVisible)
        {
            // 处理外部直接调用 Window.Show()（例如纸箱物品内容窗口打开）的情况。
            Inventory.RefreshClipping();
            if (!m_closable)
            {
                Window.ToFront(force: true);
            }
            m_wasVisible = true;
        }
        if (Inventory.GetVisibleHeight() < m_minRows)
        {
            SetVisibleRowsInternal(m_minRows, clampToScreen: false, fromInteraction: true);
        }
        if (m_dragMode != DragMode.None)
        {
            if (!Input.GetMouseButton(0))
            {
                EndDrag();
                return;
            }
            Vector2 mouse = Input.mousePosition;
            if (m_dragMode == DragMode.Move)
            {
                Vector2 delta = mouse - m_pressMouse;
                Window.rectTransform.position = new Vector3(
                    Mathf.Round(m_pressPosition.x + delta.x),
                    Mathf.Round(m_pressPosition.y + delta.y),
                    m_pressPosition.z);
                Window.FixBounds();
            }
            else
            {
                // canMove=true 时原版背景拖动可能同时修改窗口位置，缩放期间固定在按下位置。
                if (m_closable)
                {
                    Window.rectTransform.position = m_pressPosition;
                }
                float scale = Math.Max(0.1f, Window.rectTransform.lossyScale.y);
                int rows = m_pressRows + Mathf.RoundToInt((m_pressMouse.y - mouse.y) / (UISettings.current.gridSize * scale));
                rows = Mathf.Min(rows, MaxRowsOnScreen(fullScreenHeight: false));
                SetVisibleRowsInternal(rows, clampToScreen: false, fromInteraction: true);
            }
            return;
        }
        if (m_pointerDragSurface != null || !Input.GetMouseButtonDown(0))
        {
            return;
        }
        Vector2 press = Input.mousePosition;
        Zone zone = HitZone(press);
        if (zone != Zone.Chrome && zone != Zone.Resize)
        {
            return;
        }
        if (IsOccluded(RenderHandler.RaycastFindParentElement<PixelWindow>(press)))
        {
            return;
        }
        BeginDrag(press, zone);
    }

    private void BeginDrag(Vector2 press, Zone zone)
    {
        Window.ToFront(force: true);
        m_dragMode = zone == Zone.Resize ? DragMode.Resize : DragMode.Move;
        m_pressMouse = press;
        m_pressPosition = Window.rectTransform.position;
        m_pressRows = Inventory.GetVisibleHeight();
    }

    internal bool IsOverChrome(Vector2 screenPosition)
    {
        if (!IsAlive || !Window.IsVisible())
        {
            return false;
        }
        Zone zone = HitZone(screenPosition);
        return zone == Zone.Chrome || zone == Zone.Resize;
    }

    internal static bool TryGetManaged(PixelWindow window, out ScrollableStorageWindow storage)
    {
        return s_windows.TryGetValue(window, out storage);
    }

    internal static bool IsPointerOverManagedChrome(Vector2 screenPosition)
    {
        PixelWindow top = RenderHandler.RaycastFindParentElement<PixelWindow>(screenPosition);
        ScrollableStorageWindow direct = null;
        if (top != null && s_windows.TryGetValue(top, out direct)
            && direct.m_pointerDragSurface == null && direct.IsOverChrome(screenPosition))
        {
            return true;
        }
        for (int i = s_instances.Count - 1; i >= 0; i--)
        {
            if (!s_instances[i].TryGetTarget(out ScrollableStorageWindow window) || !window.IsAlive)
            {
                s_instances.RemoveAt(i);
                continue;
            }
            // 事件式拖动表面已由原版按最上层射线命中识别，无须猜测是否被遮住。
            if (window == direct || window.m_pointerDragSurface != null)
            {
                continue;
            }
            if (window.IsOverChrome(screenPosition) && !window.IsOccluded(top))
            {
                return true;
            }
        }
        return false;
    }

    private bool SetVisibleRowsInternal(int rows, bool clampToScreen, bool fromInteraction)
    {
        if (!IsAlive)
        {
            return false;
        }
        int maxAllowed = clampToScreen
            ? Math.Min(m_maxRows, MaxRowsOnScreen(fullScreenHeight: true))
            : m_maxRows;
        int clamped = Mathf.Clamp(rows, m_minRows, Math.Max(m_minRows, maxAllowed));
        int before = Inventory.GetVisibleHeight();
        if (clamped == before)
        {
            return false;
        }
        // 窗口 Validate 会按窗口自身高度重设格子高度，改窗口高度以调整可见行数。
        int chrome = Window.heightPixels - Inventory.Viewport.heightPixels;
        Window.ResizePixels(Window.widthPixels, clamped * UISettings.current.gridSize + chrome);
        int after = Inventory.GetVisibleHeight();
        if (after != before)
        {
            if (fromInteraction)
            {
                OnResized?.Invoke(after);
            }
            return true;
        }
        return false;
    }

    private int MaxRowsOnScreen(bool fullScreenHeight)
    {
        float scale = Math.Max(0.1f, Window.rectTransform.lossyScale.y);
        int chrome = Window.heightPixels - Inventory.Viewport.heightPixels;
        float available = fullScreenHeight ? Screen.height / scale : Window.rectTransform.position.y / scale;
        return Mathf.Max(m_minRows, (int)((available - chrome) / UISettings.current.gridSize));
    }

    private void EndDrag()
    {
        bool committed = m_dragMode != DragMode.None && IsAlive
            && (Window.rectTransform.position != m_pressPosition || Inventory.GetVisibleHeight() != m_pressRows);
        m_dragMode = DragMode.None;
        if (committed)
        {
            OnDragCommitted?.Invoke();
        }
    }

    private TreeNodeRender CreatePointerDragSurface()
    {
        // 保留原版节点归属，既挡住下面的平板，也不让游戏命中下面的物品。
        TreeNodeRender surface = TreeNodeRender.Instantiate().SetParent(Window.handler).InitEventHandler();
        surface.image.sprite = null;
        surface.image.color = Color.clear;
        surface.image.enabled = true;
        RectTransform rect = surface.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = Vector2.zero;
        surface.gameObject.AddComponent<ChromeRaycastFilter>().Storage = this;
        surface.eventHandler.mousePotentialDragFunc.Add(data =>
        {
            if (data.button == PointerEventData.InputButton.Left)
            {
                data.useDragThreshold = false;
            }
            return true;
        });
        surface.eventHandler.mouseBeginDragFunc.Add(data =>
        {
            if (data.button != PointerEventData.InputButton.Left || m_dragMode != DragMode.None)
            {
                return false;
            }
            Zone zone = HitZone(data.pressPosition);
            if (zone != Zone.Chrome && zone != Zone.Resize)
            {
                return false;
            }
            BeginDrag(data.pressPosition, zone);
            return true;
        });
        return surface;
    }

    internal void ResizePointerDragSurface()
    {
        if (m_pointerDragSurface != null)
        {
            m_pointerDragSurface.rectTransform.sizeDelta = new Vector2(Window.widthPixels, Window.heightPixels);
        }
    }

    private void ConfigureChrome()
    {
        // 字段名保留字面量，避免触发加载器的反射权限告警。
        if (!m_closable)
        {
            try
            {
                AccessTools.FieldRefAccess<PixelWindow, TreeNodeRender>("topCloseButton")(Window)?.gameObject.SetActive(false);
            }
            catch (Exception exception)
            {
                Warn("隐藏滚动窗口关闭按钮失败：" + exception.Message);
            }
        }
        try
        {
            DisableRaycast(AccessTools.FieldRefAccess<PixelWindow, TreeNodeRender>("titleText")(Window));
            DisableRaycast(AccessTools.FieldRefAccess<PixelWindow, TreeNodeRender>("topTitleBackground")(Window));
            DisableRaycast(AccessTools.FieldRefAccess<PixelWindow, TreeNodeRender>("topDragBorder")(Window));
            DisableRaycast(AccessTools.FieldRefAccess<PixelWindow, TreeNodeRender>("outsideBorder")(Window));
        }
        catch (Exception exception)
        {
            Warn("关闭滚动窗口装饰节点射线检测失败：" + exception.Message);
        }
    }

    private Zone HitZone(Vector2 screenPosition)
    {
        Vector2 local = Window.rectTransform.InverseTransformPoint(screenPosition);
        if (local.x < 0f || local.x > Window.widthPixels || local.y > 0f || local.y < -Window.heightPixels)
        {
            return Zone.None;
        }
        if (m_closable && local.x > Window.widthPixels - CloseButtonHitSize && local.y > -CloseButtonHitSize)
        {
            return Zone.None;
        }
        if (local.x >= Window.widthPixels - ResizeHandleSize && local.y <= -Window.heightPixels + ResizeHandleSize)
        {
            return Zone.Resize;
        }
        Vector2 grid = Inventory.Viewport.rectTransform.InverseTransformPoint(screenPosition);
        if (grid.x >= 0f && grid.x <= Inventory.Viewport.widthPixels && grid.y <= 0f && grid.y >= -Inventory.Viewport.heightPixels)
        {
            return Zone.Grid;
        }
        return Zone.Chrome;
    }

    private bool IsOccluded(PixelWindow top)
    {
        if (top == null || top == Window)
        {
            return false;
        }
        // 同画布内且绘制顺位在当前窗口之下时，说明只是标题装饰关闭射线后穿透到底部窗口。
        if (top.rectTransform.parent == Window.rectTransform.parent)
        {
            return top.rectTransform.GetSiblingIndex() > Window.rectTransform.GetSiblingIndex();
        }
        return true;
    }

    private void Warn(string message)
    {
        if (m_warn != null)
        {
            m_warn(message);
        }
        else
        {
            Debug.LogWarning("[Cirno9.Storage] " + message);
        }
    }

    private static void DisableRaycast(TreeNodeRender node)
    {
        if (node == null)
        {
            return;
        }
        foreach (Graphic graphic in node.GetComponents<Graphic>())
        {
            graphic.raycastTarget = false;
        }
    }

    private static void RemoveInstance(ScrollableStorageWindow target)
    {
        for (int i = s_instances.Count - 1; i >= 0; i--)
        {
            if (!s_instances[i].TryGetTarget(out ScrollableStorageWindow item) || item == target)
            {
                s_instances.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// 原版 Machinery 窗口默认隐藏调整手柄，只为共享滚动储藏窗口恢复右下角手柄并保持对齐。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Validate))]
internal static class ScrollableWindowResizePatch
{
    private static bool s_warned;

    private static void Postfix(PixelWindow __instance)
    {
        if (!ScrollableStorageWindow.TryGetManaged(__instance, out ScrollableStorageWindow storage))
        {
            return;
        }
        storage.ResizePointerDragSurface();
        try
        {
            TreeNodeRender handle = AccessTools.FieldRefAccess<PixelWindow, TreeNodeRender>("bottomResizeButton")(__instance);
            if (handle == null)
            {
                return;
            }
            handle.image.enabled = true;
            handle.image.raycastTarget = false;
            handle.rectTransform.anchoredPosition = new Vector2(
                __instance.widthPixels - handle.rectTransform.sizeDelta.x,
                -__instance.heightPixels + handle.rectTransform.sizeDelta.y);
        }
        catch (Exception exception)
        {
            if (!s_warned)
            {
                s_warned = true;
                Debug.LogWarning("[Cirno9.Storage] 恢复滚动窗口调整手柄失败：" + exception.Message);
            }
        }
    }
}

/// <summary>
/// 原版框选只认带拖动事件回调的元素；将共享滚动储藏窗口的标题栏、边框和调整手柄视为可拖动表面。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(ItemMultiSelectHandler), "IsPointerOverDragSurface")]
internal static class ScrollableWindowDragSurfacePatch
{
    private static void Postfix(Vector2 screenPosition, ref bool __result)
    {
        if (!__result && ScrollableStorageWindow.IsPointerOverManagedChrome(screenPosition))
        {
            __result = true;
        }
    }
}
