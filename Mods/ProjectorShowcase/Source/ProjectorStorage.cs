using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectorShowcase;

/// <summary>
/// 管理幻影投影仪上的展示区面板、尺寸调整、生效状态判断与展示柜联动。
/// 子容器挂载在原版电池(0)、改装模组(1)、便签(2)之后的索引 3，再通过显示顺序排在便签左侧，保留旧存档与机器槽位兼容。
/// Author: SaintCirno9
/// </summary>
internal static class ProjectorStorage
{
    internal const string ProjectorItemId = "mirage_projector";
    internal const string StoragePanelRole = "projector_showcase";
    internal const int DefaultWidth = 7;
    internal const int DefaultHeight = 5;

    private sealed class Entry
    {
        internal GameItem Projector;
        internal MachineryPanel Panel;
        internal GameGridInventory Storage;
        internal bool WasActiveInStore;
    }

    private static readonly Dictionary<GameItem, Entry> EntriesByProjector = new Dictionary<GameItem, Entry>();
    private static readonly HashSet<GameInventory> StorageInventories = new HashSet<GameInventory>();
    private static readonly List<GameItem> DeadKeysBuffer = new List<GameItem>();

    internal static int ConfiguredWidth => ProjectorShowcaseMod.StorageWidth != null
        ? Mathf.Clamp(ProjectorShowcaseMod.StorageWidth.Value, ProjectorShowcaseMod.MinWidth, ProjectorShowcaseMod.MaxWidth)
        : DefaultWidth;

    internal static int ConfiguredHeight => ProjectorShowcaseMod.StorageHeight != null
        ? Mathf.Clamp(ProjectorShowcaseMod.StorageHeight.Value, ProjectorShowcaseMod.MinHeight, ProjectorShowcaseMod.MaxHeight)
        : DefaultHeight;

    internal static bool IsProjectorStorage(GameInventory inventory)
    {
        return inventory != null && StorageInventories.Contains(inventory);
    }

    /// <summary>
    /// 挂载展示区。注意 <see cref="Directory{T}.Create(string)"/> 在工厂方法返回后才给 <c>identifier</c> 赋值，
    /// 因此从 <see cref="MachineProjector.Projector"/> 补丁进入时传 <paramref name="fromProjectorFactory"/> 为 true，确保读档解码前槽位已就绪。
    /// </summary>
    internal static void Attach(GameItem projector, bool fromProjectorFactory = false)
    {
        if (projector == null || (!fromProjectorFactory && projector.identifier != ProjectorItemId))
        {
            return;
        }
        if (EntriesByProjector.ContainsKey(projector))
        {
            return;
        }
        if (!(projector.contentWindow?.childElement is MachineryPanelRow panelRow))
        {
            ProjectorShowcaseMod.Log?.Warning("幻影投影仪内容窗口不是 MachineryPanelRow，跳过挂载展示区。");
            return;
        }
        if (panelRow.GetPanel(StoragePanelRole) != null)
        {
            return;
        }

        projector.EnableTag(ContainerHelper.CONTAINER_TAG);

        var storage = new GameGridInventory(ConfiguredWidth, ConfiguredHeight);
        storage.mayInventoryAddItemFunc = (GameItem item, GameInventory _) =>
            item != null
            && item != projector
            && item.identifier != ProjectorItemId
            && GeneralHelper.IsItemOwned(item);

        MachineryPanel panel = MachineHelper.Panel(GetPanelLabel(), storage, StoragePanelRole);
        panelRow.Add(panel);
        panelRow.SetDisplayOrder(0, 1, 3, 2);
        projector.contentWindow.Validate();

        var entry = new Entry
        {
            Projector = projector,
            Panel = panel,
            Storage = storage,
            WasActiveInStore = false,
        };
        EntriesByProjector[projector] = entry;
        StorageInventories.Add(storage);

        storage.onSlotAddItemLateFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(
            storage.onSlotAddItemLateFunc,
            (Action<GameItem, GameInventory, SlotMarker>)delegate(GameItem addedItem, GameInventory _, SlotMarker __)
            {
                if (SaveManager.IsDecoding || addedItem == null)
                {
                    return;
                }
                if (IsActiveStoreProjector(projector) && PlayerStore.HasInstance)
                {
                    PlayerStore.Instance.AddedItemToDisplayCase(addedItem);
                }
            });

        storage.onSlotRemoveItemEndFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(
            storage.onSlotRemoveItemEndFunc,
            (Action<GameItem, GameInventory, SlotMarker>)delegate(GameItem removedItem, GameInventory _, SlotMarker __)
            {
                if (SaveManager.IsDecoding)
                {
                    return;
                }
                ApplySize(entry);
                if (removedItem != null && PlayerStore.HasInstance)
                {
                    PlayerStore.Instance.RemovedItemToDisplayCase(removedItem);
                }
            });

        // 夜间结算后原版才会改写生效标记，接在原版回调之后刷新黑灯外观与客源预测。
        projector.onCycleEndSlotItemFunc = (Action<GameItem, GameInventory, SlotMarker>)Delegate.Combine(
            projector.onCycleEndSlotItemFunc,
            (Action<GameItem, GameInventory, SlotMarker>)delegate
            {
                OnProjectorActiveStateChanged(projector);
            });

        projector.onLoaded = (Action<GameItem>)Delegate.Combine(
            projector.onLoaded,
            (Action<GameItem>)delegate
            {
                ApplySize(entry);
                entry.WasActiveInStore = IsActiveStoreProjector(projector);
                RefreshItemsVisuals(entry.Storage);
            });
    }

    /// <summary>投影仪生效状态在夜间结算后变化时，刷新内部黑灯外观与客源预测面板。</summary>
    internal static void OnProjectorActiveStateChanged(GameItem projector)
    {
        if (SaveManager.IsDecoding || projector == null || !EntriesByProjector.TryGetValue(projector, out Entry entry))
        {
            return;
        }
        bool activeNow = IsActiveStoreProjector(projector);
        if (activeNow == entry.WasActiveInStore)
        {
            return;
        }
        entry.WasActiveInStore = activeNow;
        RefreshItemsVisuals(entry.Storage);
        ClienteleUIManager.Instance?.RefreshUI();
    }

    internal static void OnWindowShown(PixelWindow window)
    {
        if (!ProjectorShowcaseMod.Active || window == null || EntriesByProjector.Count == 0)
        {
            return;
        }
        PruneDestroyed();
        foreach (Entry entry in EntriesByProjector.Values)
        {
            if (entry.Projector.contentWindow == window)
            {
                entry.Panel.SetLabel(GetPanelLabel());
                ApplySize(entry);
                break;
            }
        }
    }

    internal static void RefreshAllSizes()
    {
        if (EntriesByProjector.Count == 0)
        {
            return;
        }
        PruneDestroyed();
        foreach (Entry entry in EntriesByProjector.Values)
        {
            ApplySize(entry);
        }
    }

    internal static void RefreshAllProjectors()
    {
        if (!ProjectorShowcaseMod.Active)
        {
            return;
        }
        if (EmporiumEntry.Instance != null)
        {
            foreach (GameItem item in EmporiumEntry.Instance.GetAllItems())
            {
                if (item != null && item.identifier == ProjectorItemId && !EntriesByProjector.ContainsKey(item))
                {
                    Attach(item);
                }
            }
        }
        PruneDestroyed();
        foreach (Entry entry in EntriesByProjector.Values)
        {
            ApplySize(entry);
            entry.WasActiveInStore = IsActiveStoreProjector(entry.Projector);
            RefreshItemsVisuals(entry.Storage);
        }
        ClienteleUIManager.Instance?.RefreshUI();
    }

    internal static void AppendActiveShowcaseItems(List<GameItem> target)
    {
        if (target == null || EntriesByProjector.Count == 0 || !ProjectorShowcaseMod.Active)
        {
            return;
        }
        PruneDestroyed();
        HashSet<GameItem> existing = null;
        foreach (Entry entry in EntriesByProjector.Values)
        {
            if (!IsActiveStoreProjector(entry.Projector))
            {
                continue;
            }
            List<GameItem> items = entry.Storage.childItems;
            if (items.Count == 0)
            {
                continue;
            }
            if (existing == null)
            {
                existing = new HashSet<GameItem>(target);
            }
            foreach (GameItem item in items)
            {
                if (item != null && !IsDestroyed(item) && GeneralHelper.IsItemOwned(item) && existing.Add(item))
                {
                    target.Add(item);
                }
            }
        }
    }

    internal static void ClearTracking()
    {
        EntriesByProjector.Clear();
        StorageInventories.Clear();
        DeadKeysBuffer.Clear();
    }

    private static void ApplySize(Entry entry)
    {
        if (entry?.Storage?.inventoryShape == null)
        {
            return;
        }
        int targetWidth = ConfiguredWidth;
        int targetHeight = ConfiguredHeight;
        foreach (GameItem item in entry.Storage.childItems)
        {
            if (item?.modifiedShape == null || IsDestroyed(item))
            {
                continue;
            }
            targetWidth = Math.Max(targetWidth, item.modifiedShape.maxX + 1);
            targetHeight = Math.Max(targetHeight, item.modifiedShape.maxY + 1);
        }

        GridShape current = entry.Storage.inventoryShape;
        if (current.width == targetWidth && current.height == targetHeight)
        {
            return;
        }
        entry.Storage.SetShape(targetWidth, targetHeight);
        entry.Panel?.Validate();
        entry.Projector?.contentWindow?.Validate();
    }

    private static void RefreshItemsVisuals(GameGridInventory storage)
    {
        if (storage == null)
        {
            return;
        }
        foreach (GameItem child in storage.childItems)
        {
            if (child != null && !IsDestroyed(child))
            {
                BlackLampHelper.RefreshLampSprite(child);
            }
        }
    }

    /// <summary>
    /// 与原版投影仪的生效判定一致：玩家自己的、未损坏的投影仪，且处于原版的生效状态
    /// （每晚结算从电池扣电成功后置 IS_PROJECTOR_ACTIVE）。原版结算对店内各处物品一视同仁，因此不再限定摆放位置。
    /// </summary>
    private static bool IsActiveStoreProjector(GameItem projector)
    {
        if (!ProjectorShowcaseMod.Active || projector == null || projector.identifier != ProjectorItemId || IsDestroyed(projector))
        {
            return false;
        }
        if (!GeneralHelper.IsItemOwned(projector))
        {
            return false;
        }
        return !projector.IsTag("BROKEN_MACHINE_TAG") && projector.IsTag(MachineProjector.IS_PROJECTOR_ACTIVE);
    }

    private static void PruneDestroyed()
    {
        DeadKeysBuffer.Clear();
        foreach (KeyValuePair<GameItem, Entry> pair in EntriesByProjector)
        {
            if (pair.Key == null || IsDestroyed(pair.Key))
            {
                DeadKeysBuffer.Add(pair.Key);
                if (pair.Value?.Storage != null)
                {
                    StorageInventories.Remove(pair.Value.Storage);
                }
            }
        }
        for (int i = 0; i < DeadKeysBuffer.Count; i++)
        {
            EntriesByProjector.Remove(DeadKeysBuffer[i]);
        }
        DeadKeysBuffer.Clear();
    }

    private static bool IsDestroyed(GameItem item)
    {
        return item is GameItemElement element && element.IsDestroyed();
    }

    private static string GetPanelLabel()
    {
        try
        {
            return ProjectorShowcaseMod.Text("panel_showcase", "Showcase");
        }
        catch (Exception ex)
        {
            ProjectorShowcaseMod.Log?.Warning("读取展示区标题失败，先使用默认标题：" + ex.Message);
            return "Showcase";
        }
    }
}
