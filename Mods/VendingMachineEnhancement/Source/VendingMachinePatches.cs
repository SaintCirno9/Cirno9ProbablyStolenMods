using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace VendingMachineEnhancement;

/// <summary>
/// 售货机创建补丁：挂载模组仓，并在读档与夜间结算时同步容量尺寸、模组加成与耗电。
/// Author: SaintCirno9
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.VendingMachine))]
internal static class VendingMachineCreatePatch
{
    [HarmonyPostfix]
    public static void Postfix(GameItem __result)
    {
        if (!VendingEnhancementMod.Active || __result == null)
        {
            return;
        }

        VendingEnhancementCore.AttachModuleBay(__result, isFountain: false);
        VendingEnhancementCore.RecomputeModulesAndPower(__result, recomputeTempStats: false);

        Action<GameItem> prevOnLoaded = __result.onLoaded;
        __result.onLoaded = delegate(GameItem gameItem)
        {
            if (!VendingEnhancementMod.Active)
            {
                prevOnLoaded?.Invoke(gameItem);
                return;
            }
            VendingEnhancementCore.EnsureMachineTags(gameItem);
            MachineHelper.LoadUpgrade(gameItem);
            VendingEnhancementCore.ApplyVendingCapacityShape(gameItem);
            gameItem.contentWindow?.Validate();
            gameItem.Validate();
            VendingEnhancementCore.TrySafeUpdateVendingSprite(gameItem);
            VendingEnhancementCore.RecomputeModulesAndPower(gameItem, recomputeTempStats: true);
        };

        Action<GameItem, GameInventory, SlotMarker> prevCycle = __result.onCycleEndSlotItemFunc;
        __result.onCycleEndSlotItemFunc = delegate(GameItem gameItem, GameInventory parentInv, SlotMarker slot)
        {
            if (!VendingEnhancementMod.Active)
            {
                prevCycle?.Invoke(gameItem, parentInv, slot);
                return;
            }
            VendingEnhancementCore.VendingNightRan = false;
            try
            {
                prevCycle?.Invoke(gameItem, parentInv, slot);
            }
            finally
            {
                if (VendingEnhancementCore.VendingNightRan)
                {
                    VendingEnhancementCore.VendingNightRan = false;
                    VendingEnhancementCore.OnVendingNightActioned(__result);
                }
            }
        };
    }
}

/// <summary>
/// 自动售水机创建补丁：挂载模组仓，并在读档与夜间结算时同步模组加成与耗电。
/// </summary>
[HarmonyPatch(typeof(VendingFountainHelper), nameof(VendingFountainHelper.VendingFountain))]
internal static class VendingFountainCreatePatch
{
    [HarmonyPostfix]
    public static void Postfix(GameItem __result)
    {
        if (!VendingEnhancementMod.Active || __result == null)
        {
            return;
        }

        VendingEnhancementCore.AttachModuleBay(__result, isFountain: true);
        VendingEnhancementCore.RecomputeModulesAndPower(__result, recomputeTempStats: false);

        Action<GameItem> prevOnLoaded = __result.onLoaded;
        __result.onLoaded = delegate(GameItem gameItem)
        {
            prevOnLoaded?.Invoke(gameItem);
            if (!VendingEnhancementMod.Active)
            {
                return;
            }
            VendingEnhancementCore.EnsureMachineTags(gameItem);
            MachineHelper.LoadUpgrade(gameItem);
            VendingEnhancementCore.RecomputeModulesAndPower(gameItem, recomputeTempStats: true);
            gameItem.contentWindow?.Validate();
            gameItem.Validate();
        };

        Action<GameItem, GameInventory, SlotMarker> prevCycle = __result.onCycleEndLateSlotItemFunc;
        __result.onCycleEndLateSlotItemFunc = delegate(GameItem gameItem, GameInventory parentInv, SlotMarker slot)
        {
            if (!VendingEnhancementMod.Active)
            {
                prevCycle?.Invoke(gameItem, parentInv, slot);
                return;
            }
            VendingEnhancementCore.FountainNightRan = false;
            try
            {
                prevCycle?.Invoke(gameItem, parentInv, slot);
            }
            finally
            {
                if (VendingEnhancementCore.FountainNightRan)
                {
                    VendingEnhancementCore.FountainNightRan = false;
                    VendingEnhancementCore.OnVendingNightActioned(__result);
                }
            }
        };
    }
}

/// <summary>
/// 使两种售货机在 MachineHelper.GetModuleInv 中指向末尾挂载的模组仓，保持旧存档槽位序号不变。
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.GetModuleInv))]
internal static class MachineHelperGetModuleInvPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem machine, ref GameGridInventory __result)
    {
        if (!VendingEnhancementMod.Active || !VendingEnhancementCore.IsTargetVending(machine))
        {
            return true;
        }
        __result = VendingEnhancementCore.TryGetModuleBay(machine);
        return false;
    }
}

/// <summary>
/// 使两种售货机在 MachineHelper.GetModules 中从末尾模组仓读取已安装模组。
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.GetModules))]
internal static class MachineHelperGetModulesPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem machine, ref List<GameItem> __result)
    {
        if (!VendingEnhancementMod.Active || !VendingEnhancementCore.IsTargetVending(machine))
        {
            return true;
        }
        GameGridInventory grid = VendingEnhancementCore.TryGetModuleBay(machine);
        __result = grid != null ? new List<GameItem>(grid.childItems) : new List<GameItem>();
        return false;
    }
}

/// <summary>
/// 正确识别售货机与饮水机的末尾模组仓，避免把第 1 号货架或普通水槽误判为模组仓。
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.IsModuleBay))]
internal static class MachineHelperIsModuleBayPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameInventory inventory, ref bool __result)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        GameItem parent = inventory?.GetParentItem();
        if (!VendingEnhancementCore.IsTargetVending(parent))
        {
            return true;
        }
        __result = inventory == VendingEnhancementCore.TryGetModuleBay(parent);
        return false;
    }
}

/// <summary>
/// 读档恢复模组仓升级状态前，补齐旧存档售货机缺少的 STANDARD_MACHINE_TAG。
/// </summary>
[HarmonyPatch(typeof(MachineHelper), nameof(MachineHelper.LoadUpgrade))]
internal static class MachineHelperLoadUpgradePatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem item)
    {
        if (VendingEnhancementMod.Active && VendingEnhancementCore.IsTargetVending(item))
        {
            VendingEnhancementCore.EnsureMachineTags(item);
        }
        return true;
    }
}

/// <summary>
/// 支持将模组仓扩展工具包直接拖到售货机/售水机核心上扩展模组仓。
/// </summary>
[HarmonyPatch(typeof(DirectoryMaster), nameof(DirectoryMaster.Item), typeof(string), typeof(bool))]
internal static class ModuleBayExpansionKitPatch
{
    [HarmonyPostfix]
    public static void Postfix(string identifier, GameItem __result)
    {
        if (!VendingEnhancementMod.Active
            || __result == null
            || !string.Equals(identifier, VendingEnhancementCore.ModuleBayExpansionKitId, StringComparison.Ordinal))
        {
            return;
        }

        Func<GameItem, GameItem, bool> coreCheck = (GameItem source, GameItem target) =>
            VendingEnhancementMod.Active && VendingEnhancementCore.CanExpandFromCore(source, target);

        __result.mayItemTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(__result.mayItemTargetItemFunc, coreCheck);
        __result.canItemTargetItemFunc = (Func<GameItem, GameItem, bool>)Delegate.Combine(__result.canItemTargetItemFunc, coreCheck);

        // 必须包装替换原委托：命中核心时提前返回，防止原委托对核心调用 ExpandModule(null, core) 触发空引用。
        Action<GameItem, GameItem> prevOnTarget = __result.onItemTargetItemFunc;
        __result.onItemTargetItemFunc = delegate(GameItem source, GameItem target)
        {
            if (VendingEnhancementMod.Active && VendingEnhancementCore.TryExpandFromCore(source, target))
            {
                return;
            }
            prevOnTarget?.Invoke(source, target);
        };
    }
}

/// <summary>
/// 提高售货机容量升级可判定上限至 20 次（VENDING_CAPACITY = 22）。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.CanUpgradedCapacity))]
internal static class VendingCanUpgradeCapacityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, ref bool __result)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        __result = vendingMachine != null
            && vendingMachine.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt() < VendingEnhancementCore.MaxCapacityValue;
        return false;
    }
}

/// <summary>
/// 放宽售货机已升级次数的统计上限至 20 次。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.GetCapacityUpgradeCount))]
internal static class VendingGetCapacityUpgradeCountPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, ref int __result)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        __result = VendingEnhancementCore.GetCapacityUpgradeCount(vendingMachine);
        return false;
    }
}

/// <summary>
/// 接管售货机容量升级：第 1~10 次保持 8×2..8×12，第 11~20 次交替横向扩宽并扩高至 14×16。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.UpgradeCapacity))]
internal static class VendingUpgradeCapacityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem core, GameItem item)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(core);
        if (VendingMachineHelper.CanUpgradedCapacity(parentItem) && VendingEnhancementCore.IsVendingMachine(parentItem))
        {
            int current = parentItem.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt();
            int newCapacity = Mathf.Min(current + 1, VendingEnhancementCore.MaxCapacityValue);
            parentItem.ModifyTag(VendingMachineHelper.VENDING_CAPACITY, delegate(TagState state)
            {
                state.SetInt(newCapacity);
            });
            StartingPerkContent.ConsumeUpgradeMaterial(item);
            VendingMachineHelper.UpdatePowerUsage(parentItem);

            GameGridInventory inventory = VendingMachineHelper.GetVendingInventory(parentItem);
            if (inventory != null)
            {
                (int width, int height) = VendingEnhancementCore.GetVendingGridSize(newCapacity);
                inventory.SetShape(width, height);
            }

            EmporiumAudioPlayer.Instance?.ratchet?.Play();
            parentItem.contentWindow?.Validate();
            parentItem.contentWindow?.FixBounds();
            VendingEnhancementCore.TrySafeUpdateVendingSprite(parentItem);
            if (parentItem.contentWindow != null && parentItem.contentWindow.IsVisible())
            {
                parentItem.RememberContentWindowPositionIfMine(parentItem.contentWindow);
            }
        }
        return false;
    }
}

/// <summary>
/// 接管售货机耗电刷新，确保升级增量记入基础耗电后再应用效率模组减免。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.UpdatePowerUsage))]
internal static class VendingUpdatePowerUsagePatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        VendingEnhancementCore.RecomputeModulesAndPower(vendingMachine, recomputeTempStats: false);
        return false;
    }
}

/// <summary>
/// 接管自动售水机耗电刷新，确保吸引力升级增量记入基础耗电后再应用效率模组减免。
/// </summary>
[HarmonyPatch(typeof(VendingFountainHelper), nameof(VendingFountainHelper.UpdatePowerUsage))]
internal static class FountainUpdatePowerUsagePatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem fountain)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        VendingEnhancementCore.RecomputeModulesAndPower(fountain, recomputeTempStats: false);
        return false;
    }
}

/// <summary>
/// 支持吸引力超过 100 时的保底售出件数递增。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.GetGuaranteedSaleCount))]
internal static class VendingGuaranteedSaleCountPatch
{
    [HarmonyPrefix]
    public static bool Prefix(int attractiveness, ref int __result)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        __result = VendingEnhancementCore.GetGuaranteedSaleCount(attractiveness);
        return false;
    }
}

/// <summary>
/// 售货机夜间售卖判定时使用经性能模组加成后的有效吸引力。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.CheckSoldItem))]
internal static class VendingCheckSoldItemPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, List<GameItem> eligibleItems, ref List<GameItem> __result)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }

        int effectiveAttractiveness = VendingEnhancementCore.GetEffectiveVendingAttractiveness(vendingMachine);
        List<GameItem> soldItems = new List<GameItem>();
        Dictionary<GameItem, int> prices = new Dictionary<GameItem, int>();

        foreach (GameItem eligibleItem in eligibleItems)
        {
            int vendingPrice = VendingMachineHelper.GetVendingPrice(eligibleItem);
            prices[eligibleItem] = vendingPrice;
            if (UnityEngine.Random.value * 100f < VendingMachineHelper.GetSaleChance(effectiveAttractiveness, eligibleItem, vendingPrice))
            {
                soldItems.Add(eligibleItem);
            }
        }

        int guaranteedSaleCount = VendingMachineHelper.GetGuaranteedSaleCount(effectiveAttractiveness);
        if (soldItems.Count < guaranteedSaleCount)
        {
            foreach (GameItem item in eligibleItems
                .Where((GameItem item) => !soldItems.Contains(item)
                    && !item.IsGameItemType("LUXURY_ITEM")
                    && prices[item] <= VendingMachineHelper.GUARANTEED_SALE_MAX_VALUE)
                .OrderByDescending(IsVendingConsumable)
                .ToList())
            {
                if (soldItems.Count >= guaranteedSaleCount)
                {
                    break;
                }
                soldItems.Add(item);
            }
        }

        __result = soldItems;
        return false;
    }

    private static bool IsVendingConsumable(GameItem item)
    {
        return item.IsGameItemType("FOOD")
            || item.IsGameItemType("WATER_RATION")
            || item.IsGameItemType("BREVAGE")
            || item.IsGameItemType("PROCESSED_FOOD")
            || item.IsGameItemType("TREAT");
    }
}

/// <summary>
/// 记录售货机当晚结算上下文，供价格加成与模组使用后效果触发。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.HandleNight))]
internal static class VendingHandleNightPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, out GameItem __state)
    {
        __state = VendingEnhancementCore.ActiveSellingMachine;
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        VendingEnhancementCore.VendingNightRan = true;
        VendingEnhancementCore.ActiveSellingMachine = vendingMachine;
        return true;
    }

    [HarmonyFinalizer]
    public static void Finalizer(GameItem __state)
    {
        VendingEnhancementCore.ActiveSellingMachine = __state;
    }
}

/// <summary>
/// 饮水机夜间售水按性能模组百分比增加或减少售水轮次。
/// </summary>
[HarmonyPatch(typeof(VendingFountainHelper), nameof(VendingFountainHelper.HandleNight))]
internal static class FountainHandleNightPatch
{
    internal struct FountainNightState
    {
        public bool IsOuter;
        public int ExtraRounds;
    }

    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, out FountainNightState __state)
    {
        __state = default;
        if (!VendingEnhancementMod.Active || VendingEnhancementCore.InFountainExtraRound)
        {
            return true;
        }

        VendingEnhancementCore.FountainNightRan = true;
        int rounds = VendingEnhancementCore.RollFountainSaleRounds(vendingMachine);
        if (rounds <= 0)
        {
            return false;
        }

        __state = new FountainNightState
        {
            IsOuter = true,
            ExtraRounds = rounds - 1,
        };
        return true;
    }

    [HarmonyPostfix]
    public static void Postfix(
        GameItem vendingMachine,
        GameSlotInventory baseWaterInv,
        GameSlotInventory highQualityWaterInv,
        GameSlotInventory pureWaterInv,
        FountainNightState __state)
    {
        if (!VendingEnhancementMod.Active || !__state.IsOuter || __state.ExtraRounds <= 0)
        {
            return;
        }

        VendingEnhancementCore.InFountainExtraRound = true;
        try
        {
            for (int i = 0; i < __state.ExtraRounds; i++)
            {
                VendingFountainHelper.HandleNight(vendingMachine, baseWaterInv, highQualityWaterInv, pureWaterInv);
            }
        }
        finally
        {
            VendingEnhancementCore.InFountainExtraRound = false;
        }
    }

    [HarmonyFinalizer]
    public static void Finalizer(FountainNightState __state)
    {
        if (__state.IsOuter)
        {
            VendingEnhancementCore.InFountainExtraRound = false;
        }
    }
}

/// <summary>
/// 在饮水机单次售水计算价值时注入当前饮水机上下文，使质量加成实时生效。
/// </summary>
[HarmonyPatch(typeof(VendingFountainHelper), nameof(VendingFountainHelper.SimulateWaterSale))]
internal static class FountainSimulateWaterSalePatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, out GameItem __state)
    {
        __state = VendingEnhancementCore.ActiveSellingMachine;
        if (VendingEnhancementMod.Active)
        {
            VendingEnhancementCore.ActiveSellingMachine = vendingMachine;
        }
        return true;
    }

    [HarmonyFinalizer]
    public static void Finalizer(GameItem __state)
    {
        VendingEnhancementCore.ActiveSellingMachine = __state;
    }
}

/// <summary>
/// 在原版 +10% 售价加成上叠加按设置折算的质量加成。
/// </summary>
[HarmonyPatch(typeof(ItemFeatureList), nameof(ItemFeatureList.VendingMachineBonus))]
internal static class VendingBonusFeaturePatch
{
    [HarmonyPostfix]
    public static void Postfix(ItemFeature __result)
    {
        if (!VendingEnhancementMod.Active || __result == null || VendingEnhancementCore.ActiveSellingMachine == null)
        {
            return;
        }
        int totalBonus = VendingEnhancementCore.GetTotalPriceBonusPercent(VendingEnhancementCore.ActiveSellingMachine);
        __result.SetPreExposedValueMod(totalBonus);
    }
}

/// <summary>
/// 更新自动售货机核心悬停提示：展示 20 级容量与网格尺寸、模组仓扩展进度及三维加成状态。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.CreateTooltip))]
internal static class VendingCoreTooltipPatch
{
    [HarmonyPrefix]
    public static bool Prefix(RichTextBuilder builder, GameItem item)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        if (item == null || !item.identifier.Equals(VendingEnhancementCore.VendingMachineCoreId, StringComparison.Ordinal))
        {
            return true;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(item);
        if (parentItem == null)
        {
            return false;
        }

        VendingEnhancementCore.RecomputeModulesAndPower(parentItem, recomputeTempStats: false);

        builder.AddLine(LocHelper.GetLocalizedMechanic("mech_vending_tooltip_markup"), ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);
        builder.AddLine(LocHelper.GetLocalizedMechanic("mech_vending_tooltip_popular"), ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);
        builder.AddLine(LocHelper.GetLocalizedMechanic("mech_vending_tooltip_attractiveness", parentItem.GetTagReadonly(VendingMachineHelper.VENDING_ATTRACTIVENESS).GetInt()), ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);

        int perfBonus = VendingEnhancementCore.GetEffectivePerformanceBonus(parentItem);
        if (perfBonus != 0)
        {
            int effectiveAttr = VendingEnhancementCore.GetEffectiveVendingAttractiveness(parentItem);
            int guaranteed = VendingEnhancementCore.GetGuaranteedSaleCount(effectiveAttr);
            RenderHandler.ColorPalette color = perfBonus >= 0 ? RenderHandler.ColorPalette.LightGreen : RenderHandler.ColorPalette.LightOrange;
            builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_effective_attractiveness", effectiveAttr, guaranteed), ignoreEmpty: true, color);
        }

        int priceBonus = VendingEnhancementCore.GetTotalPriceBonusPercent(parentItem);
        if (priceBonus != VendingEnhancementCore.BaseVendingPriceBonus)
        {
            RenderHandler.ColorPalette color = priceBonus > VendingEnhancementCore.BaseVendingPriceBonus
                ? RenderHandler.ColorPalette.LightGreen
                : RenderHandler.ColorPalette.LightOrange;
            builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_price_bonus", priceBonus), ignoreEmpty: true, color);
        }

        string ingotName = LocHelper.GetLocalizedItem("item_metal_ingot_name");
        int capUpgrades = VendingEnhancementCore.GetCapacityUpgradeCount(parentItem);
        int curCap = parentItem.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt();
        (int width, int height) = VendingEnhancementCore.GetVendingGridSize(curCap);
        string storageLine = LocHelper.GetLocalizedMechanic("mech_fridge_tooltip_upgrade_storage", ingotName, capUpgrades, VendingEnhancementCore.MaxCapacityUpgrades);

        VendingEnhancementCore.EnsureMachineTags(parentItem);
        string kitName = LocHelper.GetLocalizedItem("item_module_bay_expansion_kit_name");
        int bayStage = parentItem.GetTagReadonly(MachineHelper.MODULE_UPGRADE_STAGE_INT).GetInt();

        VendingMachineHelper.AddUpgradeHeader(builder);
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_storage_with_size", storageLine, width, height), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        VendingMachineHelper.AddAttractivenessUpgradeLine(builder, parentItem);
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_bay_upgrade", kitName, bayStage, VendingEnhancementCore.MaxModuleBayUpgrades), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        builder.AddLine(LocHelper.GetLocalizedMechanicOrDefault("mech_vending_tooltip_upgrade_power", "Each {0} upgrade raises the nightly energy use by {1}.", ingotName, 1), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);

        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_guide_vending"), ignoreEmpty: true, RenderHandler.ColorPalette.LightGray);
        MachineryHelper.CreateMachineryTooltip(builder, parentItem);
        return false;
    }
}

/// <summary>
/// 更新自动售水机核心悬停提示：展示售水轮次、质量售价加成及模组仓扩展进度。
/// </summary>
[HarmonyPatch(typeof(VendingFountainHelper), nameof(VendingFountainHelper.CreateTooltip))]
internal static class FountainCoreTooltipPatch
{
    [HarmonyPrefix]
    public static bool Prefix(RichTextBuilder builder, GameItem item)
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        if (item == null || !item.identifier.Equals(VendingEnhancementCore.VendingFountainCoreId, StringComparison.Ordinal))
        {
            return true;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(item);
        if (parentItem == null)
        {
            return false;
        }

        VendingEnhancementCore.RecomputeModulesAndPower(parentItem, recomputeTempStats: false);

        builder.AddLine(LocHelper.GetLocalizedMechanic("mech_vending_tooltip_markup"), ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);
        builder.AddLine(LocHelper.GetLocalizedMechanic("mech_vending_tooltip_attractiveness", parentItem.GetTagReadonly(VendingMachineHelper.VENDING_ATTRACTIVENESS).GetInt()), ignoreEmpty: true, RenderHandler.ColorPalette.LightTurquoise);

        int perfBonus = VendingEnhancementCore.GetEffectivePerformanceBonus(parentItem);
        if (perfBonus != 0)
        {
            RenderHandler.ColorPalette color = perfBonus >= 0 ? RenderHandler.ColorPalette.LightGreen : RenderHandler.ColorPalette.LightOrange;
            builder.AddLine(VendingEnhancementCore.GetFountainRoundsTooltipText(perfBonus), ignoreEmpty: true, color);
        }

        int priceBonus = VendingEnhancementCore.GetTotalPriceBonusPercent(parentItem);
        if (priceBonus != VendingEnhancementCore.BaseVendingPriceBonus)
        {
            RenderHandler.ColorPalette color = priceBonus > VendingEnhancementCore.BaseVendingPriceBonus
                ? RenderHandler.ColorPalette.LightGreen
                : RenderHandler.ColorPalette.LightOrange;
            builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_price_bonus", priceBonus), ignoreEmpty: true, color);
        }

        VendingEnhancementCore.EnsureMachineTags(parentItem);
        string kitName = LocHelper.GetLocalizedItem("item_module_bay_expansion_kit_name");
        int bayStage = parentItem.GetTagReadonly(MachineHelper.MODULE_UPGRADE_STAGE_INT).GetInt();

        VendingMachineHelper.AddUpgradeHeader(builder);
        VendingMachineHelper.AddAttractivenessUpgradeLine(builder, parentItem);
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_bay_upgrade", kitName, bayStage, VendingEnhancementCore.MaxModuleBayUpgrades), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        builder.AddLine(LocHelper.GetLocalizedMechanicOrDefault("mech_fountain_tooltip_upgrade_power", "Every {1} {0} upgrades raise the nightly energy use by {2}.", LocHelper.GetLocalizedItem("item_common_electronic_name"), 2, 1), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);

        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_guide_fountain"), ignoreEmpty: true, RenderHandler.ColorPalette.LightGray);
        MachineryHelper.CreateMachineryTooltip(builder, parentItem);
        return false;
    }
}

/// <summary>
/// 修复商店中点击自动售货机每次强制居中的问题，恢复并记住玩家拖动后的窗口位置。
/// </summary>
[HarmonyPatch(typeof(StoreVendingMachine), nameof(StoreVendingMachine.OnClick))]
internal static class StoreVendingMachineOnClickPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        VendingEnhancementCore.ToggleOrOpenVendingWindow(EmporiumEntry.Instance?.vendingMachineElement?.childItem);
        return false;
    }
}

/// <summary>
/// 修复商店中点击自动售水机每次强制居中的问题，恢复并记住玩家拖动后的窗口位置。
/// </summary>
[HarmonyPatch(typeof(StoreVendingFountain), nameof(StoreVendingFountain.OnClick))]
internal static class StoreVendingFountainOnClickPatch
{
    [HarmonyPrefix]
    public static bool Prefix()
    {
        if (!VendingEnhancementMod.Active)
        {
            return true;
        }
        VendingEnhancementCore.ToggleOrOpenVendingWindow(EmporiumEntry.Instance?.vendingFountainElement?.childItem);
        return false;
    }
}
