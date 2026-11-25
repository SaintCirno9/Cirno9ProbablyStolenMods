using System;
using System.Collections.Generic;
using UnityEngine;

namespace VendingMachineEnhancement;

/// <summary>
/// 自动售货机与自动售水机增强逻辑：模组仓挂载、三维加成、容量扩展与窗口位置管理。
/// Author: SaintCirno9
/// </summary>
internal static class VendingEnhancementCore
{
    internal const string VendingMachineId = "vending_machine";
    internal const string VendingFountainId = "vending_fountain";
    internal const string VendingMachineCoreId = "vending_machine_core";
    internal const string VendingFountainCoreId = "vending_fountain_core";
    internal const string ModuleBayExpansionKitId = "module_bay_expansion_kit";
    internal const string ModulePanelRole = "module";

    internal const int MaxCapacityUpgrades = VendingMachineHelper.MAX_CAPACITY_UPGRADES * 2;
    internal const int MaxCapacityValue = VendingMachineHelper.STARTING_HEIGHT + MaxCapacityUpgrades;
    internal const int BaseVendingPriceBonus = 10;
    internal const int MaxModuleBayUpgrades = 2;

    // 对应 VENDING_CAPACITY = 13..22（第 11~20 次容量升级）的宽高映射，单调递增至 14×16（224 格）。
    private static readonly (int Width, int Height)[] ExtraCapacityShapes =
    {
        (9, 12),
        (9, 13),
        (10, 13),
        (10, 14),
        (11, 14),
        (11, 15),
        (12, 15),
        (12, 16),
        (13, 16),
        (14, 16),
    };

    // 夜间售货与售水执行上下文，用于在补丁间传递当前工作的机器并避免递归重入。
    internal static GameItem ActiveSellingMachine;
    internal static bool VendingNightRan;
    internal static bool FountainNightRan;
    internal static bool InFountainExtraRound;

    internal static void ResetTransientState()
    {
        ActiveSellingMachine = null;
        VendingNightRan = false;
        FountainNightRan = false;
        InFountainExtraRound = false;
    }

    internal static bool IsVendingMachine(GameItem item)
    {
        return item != null && string.Equals(item.identifier, VendingMachineId, StringComparison.Ordinal);
    }

    internal static bool IsVendingFountain(GameItem item)
    {
        return item != null && string.Equals(item.identifier, VendingFountainId, StringComparison.Ordinal);
    }

    internal static bool IsTargetVending(GameItem item)
    {
        return IsVendingMachine(item) || IsVendingFountain(item);
    }

    internal static bool IsTargetVendingCore(GameItem item)
    {
        if (item == null)
        {
            return false;
        }
        return string.Equals(item.identifier, VendingMachineCoreId, StringComparison.Ordinal)
            || string.Equals(item.identifier, VendingFountainCoreId, StringComparison.Ordinal);
    }

    internal static int GetCapacityUpgradeCount(GameItem machine)
    {
        if (machine == null)
        {
            return 0;
        }
        int capacity = machine.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt();
        return Mathf.Clamp(capacity - VendingMachineHelper.STARTING_HEIGHT, 0, MaxCapacityUpgrades);
    }

    internal static (int Width, int Height) GetVendingGridSize(int capacity)
    {
        if (capacity <= VendingMachineHelper.MAX_CAPACITY)
        {
            int height = Mathf.Clamp(capacity, VendingMachineHelper.STARTING_HEIGHT, VendingMachineHelper.MAX_CAPACITY);
            return (VendingMachineHelper.STARTING_WIDTH, height);
        }

        int index = Mathf.Clamp(capacity - VendingMachineHelper.MAX_CAPACITY - 1, 0, ExtraCapacityShapes.Length - 1);
        return ExtraCapacityShapes[index];
    }

    internal static void ApplyVendingCapacityShape(GameItem machine)
    {
        if (!IsVendingMachine(machine))
        {
            return;
        }
        GameGridInventory inventory = VendingMachineHelper.GetVendingInventory(machine);
        if (inventory == null)
        {
            return;
        }
        int capacity = machine.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt();
        (int width, int height) = GetVendingGridSize(capacity);
        inventory.SetShape(width, height);
        inventory.Validate();
    }

    internal static void TrySafeUpdateVendingSprite(GameItem machine)
    {
        if (!IsVendingMachine(machine))
        {
            return;
        }
        GameGridInventory inventory = VendingMachineHelper.GetVendingInventory(machine);
        if (inventory != null)
        {
            int percent = SpriteHelper.GetInventoryCapacity(inventory);
            VendingMachineHelper.SetSpriteFromCapacity(machine, "vending", percent);
        }
        if (StoreVendingMachine.Instance != null
            && EmporiumEntry.Instance?.vendingMachineElement?.childItem == machine)
        {
            StoreVendingMachine.Instance.UpdateSprite();
        }
    }

    internal static GameGridInventory TryGetModuleBay(GameItem machine)
    {
        if (machine?.contentWindow?.childElement is not MachineryPanelRow row)
        {
            return null;
        }
        MachineryPanel panel = row.GetPanel(ModulePanelRole);
        if (panel == null)
        {
            return null;
        }
        List<GraphNodeStorage> storages = new List<GraphNodeStorage>();
        panel.CollectStorage(storages);
        for (int i = 0; i < storages.Count; i++)
        {
            if (storages[i] is GameGridInventory grid)
            {
                return grid;
            }
        }
        return null;
    }

    internal static void EnsureMachineTags(GameItem machine)
    {
        if (!IsTargetVending(machine))
        {
            return;
        }
        machine.EnableTag(MachineHelper.STANDARD_MACHINE_TAG, skipValidate: true);
        // 旧存档可能保留固定质量除数；用实时结果替代原版提示中的固定倍率。
        if (machine.IsTag(MachineHelper.MACHIME_QUALITY_RATIO_INT))
        {
            machine.DisableTag(MachineHelper.MACHIME_QUALITY_RATIO_INT, skipValidate: true);
        }
    }

    /// <summary>
    /// 在面板列表末尾追加模组仓并重排视觉显示顺序，避免改变 SaveManager 对原有容器槽位的 BFS 编号。
    /// </summary>
    internal static void AttachModuleBay(GameItem machine, bool isFountain)
    {
        if (machine == null || TryGetModuleBay(machine) != null)
        {
            return;
        }
        if (machine.contentWindow?.childElement is not MachineryPanelRow row)
        {
            VendingEnhancementMod.Log?.Warning($"无法挂载模组仓：机器 {machine.identifier} 缺少 MachineryPanelRow。");
            return;
        }

        GameGridInventory moduleGrid = new GameGridInventory(MachineHelper.DEFAULT_MODULE_BAY_CONFIG, MachineHelper.DEFAULT_MODULE_BAY_WIDTH);
        moduleGrid.slotPriorityFunc = (GameItem _, GameInventory _, SlotMarker _) => 2;

        MachineryPanel modulePanel = MachineHelper.Panel(
            LocHelper.GetLocalizedMechanic("mech_purifier_label_module"),
            moduleGrid,
            ModulePanelRole);
        row.Add(modulePanel);

        if (isFountain)
        {
            row.SetDisplayOrder(0, 6, 1, 2, 3, 4, 5);
        }
        else
        {
            row.SetDisplayOrder(0, 4, 1, 2, 3);
        }

        EnsureMachineTags(machine);
        MachineHelper.SetupModuleBay(moduleGrid, machine, delegate
        {
            OnModuleBayChanged(machine);
        });
        machine.contentWindow.Validate();
    }

    private static void OnModuleBayChanged(GameItem machine)
    {
        if (SaveManager.IsDecoding || machine == null)
        {
            return;
        }
        RecomputeModulesAndPower(machine, recomputeTempStats: false);
        machine.Validate();
    }

    internal static int GetBasePowerUsage(GameItem machine)
    {
        if (IsVendingMachine(machine))
        {
            return VendingMachineHelper.POWER_USE + VendingMachineHelper.GetCapacityUpgradeCount(machine);
        }
        if (IsVendingFountain(machine))
        {
            int attractUpgrades = VendingMachineHelper.GetAttractivenessUpgradeCount(machine);
            return 2 + attractUpgrades / VendingFountainHelper.PARTS_PER_POWER_STEP;
        }
        return machine?.GetTagReadonly(MachineryHelper.MACHINERY_DRAWN_POWER_BASE).GetInt() ?? 0;
    }

    /// <summary>
    /// 先修正基础耗电字段（原版将其硬编码为 2），再统一重算模组临时属性、汇总加成与最终耗电。
    /// </summary>
    internal static void RecomputeModulesAndPower(GameItem machine, bool recomputeTempStats)
    {
        if (!IsTargetVending(machine))
        {
            return;
        }
        EnsureMachineTags(machine);

        int basePower = GetBasePowerUsage(machine);
        machine.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE, delegate(TagState state)
        {
            state.Enable().SetInt(basePower);
        }, skipValidate: true);

        GameGridInventory moduleGrid = TryGetModuleBay(machine);
        if (moduleGrid != null)
        {
            if (recomputeTempStats)
            {
                ModuleEffectHelper.ComputeAllModuleTempStat(moduleGrid.childItems);
            }
            ModuleHelper.ComputeModuleEffect(moduleGrid, null, machine);
            ModuleHelper.ApplyBasicModuleEffect(moduleGrid, null, machine);
            return;
        }

        machine.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER, delegate(TagState state)
        {
            state.Enable().SetInt(basePower);
        }, skipValidate: true);
    }

    internal static void OnVendingNightActioned(GameItem machine)
    {
        GameGridInventory moduleGrid = TryGetModuleBay(machine);
        if (moduleGrid == null)
        {
            return;
        }
        EnsureMachineTags(machine);
        int basePower = GetBasePowerUsage(machine);
        machine.ModifyTag(MachineryHelper.MACHINERY_DRAWN_POWER_BASE, delegate(TagState state)
        {
            state.Enable().SetInt(basePower);
        }, skipValidate: true);

        MachineHelper.OnMachineActioned(machine, moduleGrid);
        RecomputeModulesAndPower(machine, recomputeTempStats: false);
    }

    internal static int GetAttractivenessForSale(GameItem machine)
    {
        if (machine == null)
        {
            return 0;
        }
        if (!VendingEnhancementMod.Active)
        {
            return machine.GetTagReadonly(VendingMachineHelper.VENDING_ATTRACTIVENESS).GetInt();
        }
        return GetEffectiveVendingAttractiveness(machine);
    }

    internal static int GetEffectiveVendingAttractiveness(GameItem machine)
    {
        if (machine == null)
        {
            return 0;
        }
        int baseAttr = machine.GetTagReadonly(VendingMachineHelper.VENDING_ATTRACTIVENESS).GetInt();
        int perfBonus = GetEffectivePerformanceBonus(machine);
        return (int)Math.Clamp(baseAttr + (long)baseAttr * perfBonus / 100, 0, int.MaxValue);
    }

    internal static int GetEffectivePerformanceBonus(GameItem machine)
    {
        int perfBonus = MachineryHelper.GetCurrentPerformanceBonus(machine);
        return (int)Math.Clamp((long)perfBonus * VendingEnhancementMod.PerformanceCoefficient / 100, int.MinValue, int.MaxValue);
    }

    internal static int GetGuaranteedSaleCount(int attractiveness)
    {
        if (attractiveness < 25)
        {
            return 1;
        }
        if (attractiveness < 50)
        {
            return 2;
        }
        int step = VendingEnhancementMod.GuaranteedStep;
        if (attractiveness <= 100 || step <= 0)
        {
            return 3;
        }
        return 3 + (attractiveness - 100) / step;
    }

    internal static int GetTotalPriceBonusPercent(GameItem machine)
    {
        if (machine == null)
        {
            return BaseVendingPriceBonus;
        }
        int qualityBonus = MachineryHelper.GetCurrentQualityBonus(machine);
        long priceBonus = BaseVendingPriceBonus + (long)qualityBonus * VendingEnhancementMod.QualityCoefficient / 100;
        return (int)Math.Clamp(priceBonus, 0, int.MaxValue);
    }

    internal static void AddVendingItemTooltip(RichTextBuilder builder, GameItem item)
    {
        if (item.parentInventory is not GameGridInventory inventory)
        {
            return;
        }

        GameItem machine = inventory.GetParentItem();
        if (!IsVendingMachine(machine) || inventory != VendingMachineHelper.GetVendingInventory(machine))
        {
            return;
        }

        if (HopperHelper.IsHopper(item) || VendingMachineHelper.GetVendingPrice(item) <= 0)
        {
            builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_vending_item_not_for_sale"),
                ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);
            return;
        }

        int price = GetVendingPayout(item, machine);
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_vending_item_payout", price),
            ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);
    }

    private static int GetVendingPayout(GameItem item, GameItem machine)
    {
        // 与夜间结算使用相同的临时特征，保留原版对子物品价值的加成传递。
        ItemFeature bonus = ItemFeatureList.VendingMachineBonus().SetPreExposedValueMod(GetTotalPriceBonusPercent(machine));
        try
        {
            item.AddItemFeature(bonus);
            return (int)item.GetCurrentValue(useRetailMarkup: true, withChild: true, includeEvents: false, forceMarkup: true, 0L);
        }
        finally
        {
            item.RemoveItemFeature(bonus);
        }
    }

    internal static int RollFountainSaleRounds(GameItem fountain)
    {
        if (fountain == null)
        {
            return 1;
        }
        int perfBonus = GetEffectivePerformanceBonus(fountain);
        if (perfBonus == 0)
        {
            return 1;
        }
        if (perfBonus > 0)
        {
            int guaranteed = 1 + perfBonus / 100;
            int remainder = perfBonus % 100;
            if (remainder > 0 && UnityEngine.Random.value * 100f < remainder)
            {
                guaranteed++;
            }
            return guaranteed;
        }
        if (perfBonus <= -100)
        {
            return 0;
        }
        int chance = 100 + perfBonus;
        return UnityEngine.Random.value * 100f < chance ? 1 : 0;
    }

    internal static bool CanExpandFromCore(GameItem source, GameItem target)
    {
        if (source == null || !IsTargetVendingCore(target))
        {
            return false;
        }
        if (!GeneralHelper.IsItemOwned(source) || !GeneralHelper.IsItemOwned(target))
        {
            return false;
        }
        GameItem parent = GeneralHelper.GetParentItem(target);
        return IsTargetVending(parent) && TryGetModuleBay(parent) != null && MachineHelper.CanExpand(parent);
    }

    internal static bool TryExpandFromCore(GameItem source, GameItem target)
    {
        if (!CanExpandFromCore(source, target))
        {
            return false;
        }
        GameItem parent = GeneralHelper.GetParentItem(target);
        GameGridInventory moduleGrid = TryGetModuleBay(parent);
        if (moduleGrid == null)
        {
            return false;
        }

        EnsureMachineTags(parent);
        MachineHelper.ExpandModule(moduleGrid, parent);
        parent.unitValue += 100L;
        StartingPerkContent.ConsumeUpgradeMaterial(source);
        parent.contentWindow?.FixBounds();
        if (parent.contentWindow != null && parent.contentWindow.IsVisible())
        {
            parent.RememberContentWindowPositionIfMine(parent.contentWindow);
        }
        return true;
    }

    /// <summary>
    /// 在 PixelWindow.Center() 之后恢复售货机/售水机已记忆的窗口位置，避免拦截 StoreVendingMachine.OnClick 与其他模组冲突。
    /// </summary>
    internal static void RestoreVendingWindowPositionIfSaved(PixelWindow window)
    {
        if (window == null || window.parentItems == null)
        {
            return;
        }

        for (int i = 0; i < window.parentItems.Count; i++)
        {
            GameItem parent = window.parentItems[i];
            if (!IsTargetVending(parent) || parent.contentWindow != window)
            {
                continue;
            }

            Dictionary<int, Vector2> savedPositions = PlayerStore.Instance?.contentWindowPositions;
            if (savedPositions != null && savedPositions.ContainsKey(parent.GetUniqueID()))
            {
                parent.RestoreContentWindowPosition();
            }
            else
            {
                window.FixBounds();
            }
            return;
        }
    }

    internal static string GetFountainRoundsTooltipText(int perfBonus)
    {
        if (perfBonus > 0)
        {
            int guaranteed = 1 + perfBonus / 100;
            int remainder = perfBonus % 100;
            return remainder == 0
                ? ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_fountain_rounds_fixed", guaranteed)
                : ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_fountain_rounds_chance", guaranteed, remainder);
        }
        int chance = Mathf.Max(0, 100 + perfBonus);
        return ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_fountain_rounds_reduced", chance);
    }
}
