using System;
using System.Collections.Generic;
using System.Linq;
using Cirno9.Storage;
using UnityEngine;

namespace CounterOverflowShelf;

/// <summary>
/// 溢出货架：真实原版格子存放柜台放不下的非己方货物，滚动组件仅负责显示。
/// 窗口默认贴在谈判柜台旁边，可拖动、可调整显示行数，本次交易出现后保留空货架直到清台。
/// 货架不在原版的存档物品清单里，货物只在本次交易期间存在，与原版“放不下就销毁”的结果一致。
/// Author: SaintCirno9
/// </summary>
internal static class OverflowShelf
{
    // 与原版谈判柜台同宽。总行数只是容量上限，窗口只显示若干行，其余靠滚动查看。
    private const int ShelfWidth = 18;

    private const int ShelfTotalRows = 200;

    private const int MinRows = 2;

    private const float ScrollOverflow = 0.1f;

    private const int WindowGap = 4;

    private const string NuclearWaste = "nuclear_waste";

    private static EmporiumEntry s_entry;

    private static ScrollableStorageWindow s_storage;

    private static ScrollableGridInventory s_inventory;

    private static RectTransform s_canvas;

    private static int s_titleCount = -1;

    private static int s_storedSinceLog;

    // 拿走最后一件货不代表成交；清台前保留空货架，让未购买的货物能放回来。
    private static bool s_keepEmptyVisible;

    private static bool s_atJackson;

    // 模组上次把窗口摆到的位置；窗口不在这里说明玩家拖动过。
    private static bool s_placed;

    private static Vector3 s_lastTarget;

    private static Vector3 s_lastPosition;

    private static Transform s_lastParent;

    internal static bool CanCaptureNow()
    {
        return OverflowShelfMod.Active && OverflowShelfMod.CaptureEnabled && EmporiumEntry.Instance != null && RenderHandler.current != null;
    }

    /// <summary>把尚未放进任何容器的非己方货物放上货架，返回是否全部放下。</summary>
    internal static bool TryStore(GameItem item)
    {
        if (item == null || !CanCaptureNow())
        {
            return false;
        }
        if (item.parentInventory != null || item.unitCount <= 0 || GeneralHelper.IsItemOwned(item))
        {
            return false;
        }
        if (item is GameItemElement element && element.IsDestroyed())
        {
            return false;
        }
        if (!EnsureWindow())
        {
            return false;
        }
        try
        {
            int remaining = GraphUtils.TryAcceptAll(s_inventory, item);
            if (item.parentInventory == null && remaining > 0)
            {
                OverflowShelfMod.Log.Warning($"溢出货架也放不下 '{item.identifier}' x{remaining}，按原版销毁。");
                return false;
            }
            s_keepEmptyVisible = true;
            s_storedSinceLog++;
            return true;
        }
        catch (Exception exception)
        {
            OverflowShelfMod.Log.Error($"把 '{item.identifier}' 放进溢出货架失败，按原版销毁：{exception}");
            return false;
        }
    }

    /// <summary>只接管货架与谈判区的未购物品；其他来源、已购物品及角色交易保持原版逻辑。</summary>
    internal static bool CanQuickTransfer(GameItem item)
    {
        EmporiumEntry entry = EmporiumEntry.Instance;
        if (!OverflowShelfMod.Active || entry == null || item == null || item.unitCount <= 0
            || GeneralHelper.IsItemOwned(item) || item.IsTag(GameCharacterItem.TAG_NOT_PURCHASED))
        {
            return false;
        }
        GameInventory parent = item.parentInventory;
        return parent != null && (parent == entry.backInvinvElement
            || (IsAlive() && s_entry == entry && parent == s_inventory));
    }

    /// <summary>货架送到谈判区；谈判区退回前柜台，前柜台放不下的剩余数量再进货架。</summary>
    internal static void QuickTransfer(GameItem item)
    {
        EmporiumEntry entry = EmporiumEntry.Instance;
        if (item.parentInventory == s_inventory)
        {
            GraphUtils.TryAcceptOnce(entry.backInvinvElement, item);
            return;
        }
        GameInventory source = item.parentInventory;
        int remaining = GraphUtils.TryAcceptAll(entry.frontInvinvElement, item);
        // 部分堆叠后原物品仍在来源里；只有这部分剩余数量可以继续转移，不能把已放上柜台的货再搬走。
        if (remaining <= 0 || item.parentInventory != source || GeneralHelper.IsItemOwned(item)
            || !CanCaptureNow() || !EnsureWindow())
        {
            return;
        }
        if (GraphUtils.TryAcceptAll(s_inventory, item, remaining) < remaining)
        {
            s_keepEmptyVisible = true;
        }
    }

    /// <summary>
    /// 清除货架：已归玩家的物品先送走，非己方货物按原版清台方式销毁。
    /// </summary>
    internal static void Clear(bool countNuclearWaste)
    {
        // 即使货架已经被拿空，原版清台时也要结束本次交易的空架保留。
        s_keepEmptyVisible = false;
        if (!IsAlive())
        {
            return;
        }
        if (s_inventory.childItems.Count == 0)
        {
            return;
        }
        EmporiumEntry entry = EmporiumEntry.Instance;
        try
        {
            entry?.DeselectDragItem(nonOwnedOnly: true);
        }
        catch (Exception exception)
        {
            OverflowShelfMod.Log.Warning("清除货架前取消拖动失败：" + exception.Message);
        }
        MoveOwnedOff();
        int destroyed = 0;
        foreach (GameItem item in s_inventory.childItems.ToList())
        {
            if (GeneralHelper.IsItemOwned(item))
            {
                continue;
            }
            try
            {
                item.CloseContentWindow(includeChildren: true);
                if (countNuclearWaste && item.identifier == NuclearWaste)
                {
                    PlayerStore.GetStoreClientData().nuclearWasteLeftOnCounter++;
                }
                GunHelper.ReturnPlayerParts(item);
                item.Destroy();
                destroyed++;
            }
            catch (Exception exception)
            {
                OverflowShelfMod.Log.Error($"清除溢出货架上的 '{item?.identifier}' 失败：{exception}");
            }
        }
        if (destroyed > 0)
        {
            OverflowShelfMod.Log.Log($"柜台已清空，溢出货架清除 {destroyed} 件货物。");
        }
    }

    /// <summary>判断传入顾客是否为当前活跃交易顾客，避免把货架货物误归到其他顾客名下。</summary>
    internal static bool IsCurrentClient(StoreClient storeClient)
    {
        return storeClient != null
            && IsAlive()
            && s_entry == EmporiumEntry.Instance
            && PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint() == storeClient;
    }

    /// <summary>货架上是否仍有未购（非己方）货物。</summary>
    internal static bool HasNonOwned()
    {
        if (!IsAlive() || s_entry != EmporiumEntry.Instance)
        {
            return false;
        }
        foreach (GameItem item in s_inventory.childItems)
        {
            if (!GeneralHelper.IsItemOwned(item))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>货架上当前未购（非己方）货物的件数。</summary>
    internal static int NonOwnedCount()
    {
        if (!IsAlive() || s_entry != EmporiumEntry.Instance)
        {
            return 0;
        }
        int count = 0;
        foreach (GameItem item in s_inventory.childItems)
        {
            if (!GeneralHelper.IsItemOwned(item))
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>把货架上的非己方货物追加到列表，用于批发计价与成交或举报检查。</summary>
    internal static void AppendNonOwned(List<GameItem> list)
    {
        if (list == null || !IsAlive() || s_entry != EmporiumEntry.Instance)
        {
            return;
        }
        foreach (GameItem item in s_inventory.childItems)
        {
            if (!GeneralHelper.IsItemOwned(item) && !list.Contains(item))
            {
                list.Add(item);
            }
        }
    }

    /// <summary>
    /// 货架不存档，已归玩家的物品不能留在上面：按原版离开杰克逊博士时送回己方物品的方式（EmporiumTryAdd，从后台开始找空位）移走。
    /// 店里所有容器都满时物品留在货架上并提示，原版此时会直接销毁。
    /// </summary>
    internal static void MoveOwnedOff()
    {
        if (!IsAlive())
        {
            return;
        }
        EmporiumEntry entry = EmporiumEntry.Instance;
        int moved = 0;
        int stuck = 0;
        foreach (GameItem item in s_inventory.childItems.ToList())
        {
            if (!GeneralHelper.IsItemOwned(item))
            {
                continue;
            }
            try
            {
                item.CloseContentWindow(includeChildren: true);
                // 物品仍在货架上时，全部容器放不下也不会被 DestroyIfUnplaced 销毁。
                GraphUtils.EmporiumTryAdd(entry?.backInvWindow, item);
                if (item.parentInventory == s_inventory)
                {
                    stuck++;
                }
                else
                {
                    moved++;
                }
            }
            catch (Exception exception)
            {
                OverflowShelfMod.Log.Error($"移走溢出货架上已购的 '{item?.identifier}' 失败：{exception}");
                stuck++;
            }
        }
        if (moved > 0)
        {
            OverflowShelfMod.Log.Log($"溢出货架上 {moved} 件已购物品已移进店内容器。");
        }
        if (stuck > 0)
        {
            OverflowShelfMod.Log.Warning($"店内容器都满了，{stuck} 件已购物品留在溢出货架上；货架不会存档，请尽快取走。");
        }
    }

    internal static void Tick()
    {
        ShelfLayout.SaveIfIdle();
        if (s_storage == null)
        {
            return;
        }
        if (!IsAlive() || s_entry != EmporiumEntry.Instance)
        {
            // 场景切换后旧窗口随场景销毁，只丢弃引用，下次溢出时重建。
            Forget();
            return;
        }
        int count = s_inventory.childItems.Count;
        if (count != s_titleCount)
        {
            s_storage.Window.SetTitle(OverflowShelfMod.Text("shelf_title", count));
            s_titleCount = count;
        }
        bool show = (count > 0 || s_keepEmptyVisible) && s_entry.frontInvWindow != null && s_entry.frontInvWindow.IsVisible();
        if (!show)
        {
            s_storage.Hide();
        }
        else
        {
            if (!s_storage.Window.IsVisible())
            {
                s_storage.Show();
                s_placed = false;
            }
            bool atJackson = IsAtJackson();
            if (atJackson != s_atJackson)
            {
                s_atJackson = atJackson;
                s_storage.SetVisibleRows(ShelfLayout.GetRows(atJackson));
                s_placed = false;
            }
            s_storage.Tick();
            FollowFrontCounter();
        }
        if (s_storedSinceLog > 0)
        {
            OverflowShelfMod.Log.Log($"柜台放不下，{s_storedSinceLog} 件货物已放进溢出货架（货架现有 {count} 件）。");
            s_storedSinceLog = 0;
        }
    }

    /// <summary>设置里的默认行数变化后，按当前场景重新取行数。</summary>
    internal static void ApplyRows()
    {
        if (IsAlive())
        {
            s_storage.SetVisibleRows(ShelfLayout.GetRows(s_atJackson));
        }
    }

    /// <summary>贴靠侧设置变化后回到默认位置。</summary>
    internal static void ResetPlacement()
    {
        s_placed = false;
    }

    internal static void Shutdown()
    {
        if (IsAlive())
        {
            Clear(countNuclearWaste: false);
            s_storage.Destroy();
        }
        Forget();
        ShelfLayout.Save();
    }

    private static bool EnsureWindow()
    {
        EmporiumEntry entry = EmporiumEntry.Instance;
        if (entry == null || entry.frontInvWindow == null)
        {
            return false;
        }
        if (IsAlive() && s_entry == entry)
        {
            return true;
        }
        Forget();
        try
        {
            s_atJackson = IsAtJackson();
            int initialRows = Mathf.Clamp(ShelfLayout.GetRows(s_atJackson), MinRows, ShelfTotalRows);
            s_inventory = new ScrollableGridInventory(ShelfWidth, initialRows, ShelfTotalRows, ScrollOverflow);
            s_inventory.mayInventoryAddItemFunc = MayShelfAccept;
            // 优先级低于原版所有柜台和仓库（4~8），快捷转移只在其他地方都放不下时才会用到货架。
            s_inventory.slotPriorityFunc = (GameItem _, GameInventory _, SlotMarker _) => 0;
            // 货架上的货随清台销毁，关掉窗口会让玩家看不到这些货，因此设为不可关闭并保留可见登记供智能排列识别。
            s_storage = new ScrollableStorageWindow(
                s_inventory,
                OverflowShelfMod.Text("shelf_title", 0),
                closable: false,
                minRows: MinRows,
                maxRows: ShelfTotalRows,
                objectName: "Node CounterOverflowShelfWindow",
                warn: msg => OverflowShelfMod.Log.Warning(msg),
                usePointerDrag: true);
            s_storage.OnResized += rows => ShelfLayout.SetRows(s_atJackson, rows);
            s_canvas = RenderHandler.current.windowUICanvasObject.GetComponent<RectTransform>();
            s_entry = entry;
            s_titleCount = 0;
            s_placed = false;
            return true;
        }
        catch (Exception exception)
        {
            OverflowShelfMod.Log.Error("创建溢出货架窗口失败：" + exception);
            Forget();
            return false;
        }
    }

    private static bool MayShelfAccept(GameItem item, GameInventory inventory)
    {
        if (item == null || GeneralHelper.IsItemOwned(item))
        {
            return false;
        }
        // 接收模组放入的溢出货物、货架内部的挪动，以及从谈判柜台或谈判区放回的未购货物。
        // 离开柜台和谈判区的原版处理挂在来源容器的移除事件上，放回货架时照常触发。
        GameInventory parent = item.parentInventory;
        if (parent != null && parent != s_inventory)
        {
            EmporiumEntry entry = EmporiumEntry.Instance;
            if (entry == null || (parent != entry.frontInvinvElement && parent != entry.backInvinvElement))
            {
                return false;
            }
        }
        // 与原版柜台一致：外出地点不放危险废料。
        return !GeneralHelper.IsHazardousWaste(item) || MapUIManager.Instance == null || !MapUIManager.Instance.IsOutside;
    }

    /// <summary>
    /// 窗口跟随谈判柜台：位置按相对柜台左上角的偏移保存，柜台移动或场景切换时一起移动。
    /// 用世界坐标换算，可移动窗口可能被原版挂到另一块画布（宽屏留边时的停靠画布）上。
    /// </summary>
    private static void FollowFrontCounter()
    {
        PixelWindow window = s_storage.Window;
        RectTransform front = s_entry.frontInvWindow.rectTransform;
        RectTransform shelf = window.rectTransform;
        float scale = front.lossyScale.x > 0f ? front.lossyScale.x : 1f;
        Vector3 frontPosition = front.position;
        Vector3 current = shelf.position;
        // 原版会把可移动窗口在浮动画布和原画布之间来回挂载，换父节点引起的位移不算玩家拖动。
        if (shelf.parent != s_lastParent)
        {
            s_placed = false;
            s_lastParent = shelf.parent;
        }
        if (s_placed && (current - s_lastPosition).sqrMagnitude > 0.01f)
        {
            ShelfLayout.SetOffset(s_atJackson, new Vector2((current.x - frontPosition.x) / scale, (current.y - frontPosition.y) / scale));
        }
        Vector2 offset = ShelfLayout.TryGetOffset(s_atJackson, out Vector2 saved) ? saved : DefaultOffset();
        Vector3 target = new Vector3(Mathf.Round(frontPosition.x + offset.x * scale), Mathf.Round(frontPosition.y + offset.y * scale), current.z);
        if (!s_placed || (target - s_lastTarget).sqrMagnitude > 0.01f)
        {
            shelf.position = target;
            window.FixBounds();
            s_lastTarget = target;
        }
        s_lastPosition = shelf.position;
        s_placed = true;
    }

    /// <summary>默认贴着柜台顶边，放在设置里的一侧；那一侧放不下时换另一侧。</summary>
    private static Vector2 DefaultOffset()
    {
        PixelWindow front = s_entry.frontInvWindow;
        float canvasWidth = s_canvas != null ? s_canvas.sizeDelta.x : float.MaxValue;
        float frontX = front.rectTransform.anchoredPosition.x;
        float width = s_storage.Window.widthPixels;
        float right = frontX + front.widthPixels + WindowGap;
        float left = frontX - width - WindowGap;
        bool rightFits = right + width <= canvasWidth;
        bool leftFits = left >= 0f;
        float x = OverflowShelfMod.PreferLeft
            ? (leftFits || !rightFits ? left : right)
            : (rightFits || !leftFits ? right : left);
        x = Mathf.Clamp(x, 0f, Mathf.Max(0f, canvasWidth - width));
        return new Vector2(x - frontX, 0f);
    }

    private static bool IsAtJackson()
    {
        PlayerStore store = PlayerStore.Instance;
        return store != null && store.playerLocation == PlayerStore.PlayerLocation.INVENTOR;
    }

    private static bool IsAlive()
    {
        return s_storage != null && s_storage.IsAlive;
    }

    private static void Forget()
    {
        s_storage = null;
        s_inventory = null;
        s_entry = null;
        s_canvas = null;
        s_titleCount = -1;
        s_keepEmptyVisible = false;
        s_placed = false;
        s_lastParent = null;
    }
}
