using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
            if (!VendingEnhancementMod.EnableCapacityExpansion)
            {
                prevOnLoaded?.Invoke(gameItem);
                VendingEnhancementCore.EnsureMachineTags(gameItem);
                MachineHelper.LoadUpgrade(gameItem);
                gameItem.contentWindow?.Validate();
                gameItem.Validate();
                VendingEnhancementCore.RecomputeModulesAndPower(gameItem, recomputeTempStats: true);
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
/// 提高售货机容量升级可判定上限至 20 次（VENDING_CAPACITY = 22）；关闭容量扩建开关时让位给原版或其他模组。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.CanUpgradedCapacity))]
internal static class VendingCanUpgradeCapacityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, ref bool __result)
    {
        if (!VendingEnhancementMod.Active || !VendingEnhancementMod.EnableCapacityExpansion)
        {
            return true;
        }
        __result = vendingMachine != null
            && vendingMachine.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt() < VendingEnhancementCore.MaxCapacityValue;
        return false;
    }
}

/// <summary>
/// 放宽售货机已升级次数的统计上限至 20 次；关闭容量扩建开关时让位给原版或其他模组。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.GetCapacityUpgradeCount))]
internal static class VendingGetCapacityUpgradeCountPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem vendingMachine, ref int __result)
    {
        if (!VendingEnhancementMod.Active || !VendingEnhancementMod.EnableCapacityExpansion)
        {
            return true;
        }
        __result = VendingEnhancementCore.GetCapacityUpgradeCount(vendingMachine);
        return false;
    }
}

/// <summary>
/// 接管售货机容量升级：第 1~10 次保持 8×2..8×12，第 11~20 次交替横向扩宽并扩高至 14×16；关闭开关时让位给原版或其他模组。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.UpgradeCapacity))]
internal static class VendingUpgradeCapacityPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GameItem core, GameItem item)
    {
        if (!VendingEnhancementMod.Active || !VendingEnhancementMod.EnableCapacityExpansion)
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
/// 售货机夜间售卖判定时仅替换吸引力读取为经性能模组加成后的有效吸引力，不短路其他模组的 Prefix/Postfix。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.CheckSoldItem))]
internal static class VendingCheckSoldItemPatch
{
    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo getTagReadonly = AccessTools.Method(typeof(GameItem), nameof(GameItem.GetTagReadonly), new[] { typeof(string) });
        MethodInfo getInt = AccessTools.Method(typeof(TagState), nameof(TagState.GetInt), Type.EmptyTypes);
        MethodInfo getEffective = AccessTools.Method(typeof(VendingEnhancementCore), nameof(VendingEnhancementCore.GetAttractivenessForSale), new[] { typeof(GameItem) });

        List<CodeInstruction> codes = instructions.ToList();
        if (getTagReadonly == null || getInt == null || getEffective == null)
        {
            VendingEnhancementMod.Log?.Warning("CheckSoldItem Transpiler 未能解析目标方法，已回退为原版吸引力读取。");
            return codes;
        }

        int replaced = 0;
        for (int i = 0; i + 2 < codes.Count; i++)
        {
            if (codes[i].opcode == OpCodes.Ldstr
                && codes[i].operand is string tagName
                && string.Equals(tagName, VendingMachineHelper.VENDING_ATTRACTIVENESS, StringComparison.Ordinal)
                && codes[i + 1].Calls(getTagReadonly)
                && codes[i + 2].Calls(getInt))
            {
                CodeInstruction callEffective = new CodeInstruction(OpCodes.Call, getEffective);
                callEffective.MoveLabelsFrom(codes[i]);
                callEffective.MoveBlocksFrom(codes[i]);
                codes[i] = callEffective;
                codes.RemoveRange(i + 1, 2);
                replaced++;
                break;
            }
        }

        if (replaced == 0)
        {
            VendingEnhancementMod.Log?.Warning("CheckSoldItem Transpiler 未匹配到 VENDING_ATTRACTIVENESS 读取序列，已回退为原版逻辑。");
        }
        return codes;
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
/// 更新自动售货机核心悬停提示：通过 Prefix 刷新模组耗电、Postfix 追加模组与扩建信息，不覆盖原版与其他模组的提示内容。
/// </summary>
[HarmonyPatch(typeof(VendingMachineHelper), nameof(VendingMachineHelper.CreateTooltip))]
internal static class VendingCoreTooltipPatch
{
    [HarmonyPrefix]
    public static void Prefix(GameItem item)
    {
        if (!VendingEnhancementMod.Active
            || item == null
            || !item.identifier.Equals(VendingEnhancementCore.VendingMachineCoreId, StringComparison.Ordinal))
        {
            return;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(item);
        if (parentItem != null)
        {
            VendingEnhancementCore.EnsureMachineTags(parentItem);
            VendingEnhancementCore.RecomputeModulesAndPower(parentItem, recomputeTempStats: false);
        }
    }

    [HarmonyPostfix]
    public static void Postfix(RichTextBuilder builder, GameItem item)
    {
        if (!VendingEnhancementMod.Active
            || builder == null
            || item == null
            || !item.identifier.Equals(VendingEnhancementCore.VendingMachineCoreId, StringComparison.Ordinal))
        {
            return;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(item);
        if (parentItem == null)
        {
            return;
        }

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

        if (VendingEnhancementMod.EnableCapacityExpansion)
        {
            int capUpgrades = VendingEnhancementCore.GetCapacityUpgradeCount(parentItem);
            int curCap = parentItem.GetTagReadonly(VendingMachineHelper.VENDING_CAPACITY).GetInt();
            (int width, int height) = VendingEnhancementCore.GetVendingGridSize(curCap);
            builder.AddLine(ModHelper.GetLocalized(
                VendingEnhancementMod.ModId,
                "tooltip_expanded_capacity",
                capUpgrades,
                VendingEnhancementCore.MaxCapacityUpgrades,
                width,
                height), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        }

        string kitName = LocHelper.GetLocalizedItem("item_module_bay_expansion_kit_name");
        int bayStage = parentItem.GetTagReadonly(MachineHelper.MODULE_UPGRADE_STAGE_INT).GetInt();
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_bay_upgrade", kitName, bayStage, VendingEnhancementCore.MaxModuleBayUpgrades), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_guide_vending"), ignoreEmpty: true, RenderHandler.ColorPalette.LightGray);
    }
}

/// <summary>
/// 更新自动售水机核心悬停提示：通过 Prefix 刷新模组耗电、Postfix 追加售水轮次、售价加成与模组仓进度。
/// </summary>
[HarmonyPatch(typeof(VendingFountainHelper), nameof(VendingFountainHelper.CreateTooltip))]
internal static class FountainCoreTooltipPatch
{
    [HarmonyPrefix]
    public static void Prefix(GameItem item)
    {
        if (!VendingEnhancementMod.Active
            || item == null
            || !item.identifier.Equals(VendingEnhancementCore.VendingFountainCoreId, StringComparison.Ordinal))
        {
            return;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(item);
        if (parentItem != null)
        {
            VendingEnhancementCore.EnsureMachineTags(parentItem);
            VendingEnhancementCore.RecomputeModulesAndPower(parentItem, recomputeTempStats: false);
        }
    }

    [HarmonyPostfix]
    public static void Postfix(RichTextBuilder builder, GameItem item)
    {
        if (!VendingEnhancementMod.Active
            || builder == null
            || item == null
            || !item.identifier.Equals(VendingEnhancementCore.VendingFountainCoreId, StringComparison.Ordinal))
        {
            return;
        }

        GameItem parentItem = GeneralHelper.GetParentItem(item);
        if (parentItem == null)
        {
            return;
        }

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

        string kitName = LocHelper.GetLocalizedItem("item_module_bay_expansion_kit_name");
        int bayStage = parentItem.GetTagReadonly(MachineHelper.MODULE_UPGRADE_STAGE_INT).GetInt();
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_bay_upgrade", kitName, bayStage, VendingEnhancementCore.MaxModuleBayUpgrades), ignoreEmpty: true, RenderHandler.ColorPalette.LightCyan);
        builder.AddLine(ModHelper.GetLocalized(VendingEnhancementMod.ModId, "tooltip_module_guide_fountain"), ignoreEmpty: true, RenderHandler.ColorPalette.LightGray);
    }
}

/// <summary>
/// 在窗口居中后恢复售货机/售水机已保存的位置，不拦截 StoreVendingMachine.OnClick，兼容任意多机或点击交互模组。
/// </summary>
[HarmonyPatch(typeof(PixelWindow), nameof(PixelWindow.Center))]
internal static class PixelWindowCenterPatch
{
    [HarmonyPostfix]
    public static void Postfix(PixelWindow __instance)
    {
        if (!VendingEnhancementMod.Active)
        {
            return;
        }
        VendingEnhancementCore.RestoreVendingWindowPositionIfSaved(__instance);
    }
}
