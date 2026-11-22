using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Cirno9.Storage;

/// <summary>物品由真实的原版格子持有；滚动组件仅提供遮罩、偏移和滚动条，不增加存档节点。</summary>
internal class ScrollableGridInventory : GameGridInventory, PixelElement
{
    private sealed class ScrollView : GameGridScrollableInventory
    {
        private readonly ScrollableGridInventory m_inventory;

        internal ScrollView(ScrollableGridInventory inventory, int width, int visibleRows, int rows, float overflow)
            : base(width, visibleRows, rows, overflow)
        {
            m_inventory = inventory;
        }

        // 原版依据物品最低行计算滚动范围；滚动组件自己不持有任何物品。
        public override List<GameItem> childItems => m_inventory?.childItems ?? base.childItems;
    }

    internal readonly GameGridScrollableInventory Viewport;

    int PixelElement.widthPixels => Viewport.widthPixels;
    int PixelElement.heightPixels => Viewport.heightPixels;
    TreeNodeRender PixelElement.handler => Viewport.handler;
    RectTransform PixelElement.rectTransform => Viewport.rectTransform;
    void PixelElement.ResizePixels(int width, int height) => Viewport.ResizePixels(width, height);
    int PixelElement.WidthPixelsClamp(int width) => Viewport.WidthPixelsClamp(width);
    int PixelElement.HeightPixelsClamp(int height) => Viewport.HeightPixelsClamp(height);

    internal ScrollableGridInventory(int width, int visibleRows, int rows, float overflow = 0.1f)
        : base(width, rows)
    {
        SetMachineryStyle();
        Viewport = new ScrollView(this, width, visibleRows, rows, overflow);
        // 可见格子的渲染节点与物品 parentInventory 指向同一个真实格子，不再暴露隐藏视图。
        handler.SetParent(Viewport.inner);
        handler.rectTransform.anchoredPosition = Vector2.zero;
        ConfigureViewport(Viewport);
        TreeNodeRender background = AccessTools.FieldRefAccess<GameGridInventory, TreeNodeRender>("background")(this);
        background.eventHandler.mouseScrollFunc.Add(data =>
        {
            Viewport.Scroll(data.scrollDelta);
            return false;
        });
        handler.InitEventHandler();
        handler.image.raycastTarget = false;
        handler.eventHandler.mouseScrollFunc.Add(data =>
        {
            Viewport.Scroll(data.scrollDelta);
            return false;
        });
        Validate();
    }

    internal static void ConfigureViewport(GameGridScrollableInventory viewport)
    {
        TreeNodeRender viewBackground =
            AccessTools.FieldRefAccess<GameGridScrollableInventory, TreeNodeRender>("background")(viewport);
        viewBackground.image.color = Color.clear;
        viewBackground.image.raycastTarget = false;
        AccessTools.FieldRefAccess<GameGridScrollableInventory, TreeNodeRender>("insideBorder")(viewport).image.enabled = false;
        // 旧滚动条贴图自带橙色边框，单改窗口颜色无效；替换为原版灰色九宫格资源，保留尺寸和事件。
        MachineryStyle.SetSliced(
            AccessTools.FieldRefAccess<GameGridScrollableInventory, TreeNodeRender>("scrollBar")(viewport),
            MachineryStyle.Intake);
        MachineryStyle.SetSliced(
            AccessTools.FieldRefAccess<GameGridScrollableInventory, TreeNodeRender>("scrollButton")(viewport),
            MachineryStyle.ButtonNormal);
    }

    public override void Validate()
    {
        base.Validate();
        // 基类构造时滚动组件尚未创建。
        if (Viewport == null)
        {
            return;
        }
        if (Viewport.width != inventoryShape.width || Viewport.height != inventoryShape.height)
        {
            Viewport.SetShape(inventoryShape.width, Viewport.GetVisibleHeight(), inventoryShape.height);
        }
        else
        {
            Viewport.Validate(validateItems: false);
        }
        RefreshClipping();
    }

    public override bool UncheckedAccept(GameItem item)
    {
        bool accepted = base.UncheckedAccept(item);
        if (accepted && Viewport != null)
        {
            Viewport.Validate(validateItems: false);
            if (item is PixelElement element)
            {
                RefreshClipping(element.handler);
            }
        }
        return accepted;
    }

    public override bool Expel(GameItem item)
    {
        bool removed = base.Expel(item);
        if (removed)
        {
            Viewport.Validate(validateItems: false);
        }
        return removed;
    }

    internal int GetVisibleHeight() => Viewport.GetVisibleHeight();

    internal void SetScroll(float value) => Viewport.SetScroll(value);

    internal void RefreshClipping() => RefreshClipping(handler);

    internal static void RefreshClipping(TreeNodeRender root)
    {
        foreach (MaskableGraphic graphic in root.GetComponentsInChildren<MaskableGraphic>(includeInactive: true))
        {
            graphic.maskable = true;
            graphic.RecalculateClipping();
        }
    }
}
