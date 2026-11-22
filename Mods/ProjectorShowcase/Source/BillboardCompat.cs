using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ProjectorShowcase;

/// <summary>
/// 可选兼容“更合理的广告牌/灯牌”（PawnshopBillboards，工坊 3830874694）：
/// 让投影仪展示区的物品变动同样触发其 <c>HangingAdvertising</c> 展示缓存失效。
/// Author: SaintCirno9
/// </summary>
internal static class BillboardCompat
{
    private const string TargetAssemblyName = "PawnshopBillboards.Mod";
    private const string HangingAdvertisingTypeName = "PawnshopBillboards.HangingAdvertising";

    private static string s_owner;
    private static Harmony s_harmony;
    private static bool s_installed;
    private static string s_lastFailure;

    internal static void Init(string owner)
    {
        s_owner = owner;
    }

    internal static void Start()
    {
        s_harmony = new Harmony(s_owner);
        AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        ModHook.OnModAssetsLoaded += OnModAssetsLoaded;
        TryInstallAllLoaded();
    }

    internal static void Stop()
    {
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
        ModHook.OnModAssetsLoaded -= OnModAssetsLoaded;
        s_harmony = null;
    }

    private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
    {
        TryInstall(args.LoadedAssembly);
    }

    private static void OnModAssetsLoaded(LoadedMod _)
    {
        TryInstallAllLoaded();
    }

    private static void TryInstallAllLoaded()
    {
        if (s_installed)
        {
            return;
        }
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            TryInstall(assembly);
            if (s_installed)
            {
                break;
            }
        }
    }

    private static void TryInstall(Assembly assembly)
    {
        if (s_installed || s_harmony == null || assembly == null || assembly.GetName().Name != TargetAssemblyName)
        {
            return;
        }
        try
        {
            Type hangingType = assembly.GetType(HangingAdvertisingTypeName, throwOnError: true);
            MethodInfo usesMethod = AccessTools.DeclaredMethod(hangingType, "Uses", new[] { typeof(GameInventory) });
            if (usesMethod == null || usesMethod.ReturnType != typeof(bool))
            {
                throw new InvalidOperationException($"找不到返回 bool 的 {HangingAdvertisingTypeName}.Uses(GameInventory)。");
            }
            Patches existing = Harmony.GetPatchInfo(usesMethod);
            if (existing == null || !existing.Owners.Contains(s_owner))
            {
                s_harmony.Patch(
                    usesMethod,
                    postfix: new HarmonyMethod(typeof(BillboardCompat), nameof(UsesPostfix)));
            }
            s_installed = true;
            s_lastFailure = null;
            ProjectorShowcaseMod.Log?.Log("已启用 PawnshopBillboards 展示缓存联动兼容。");
        }
        catch (Exception ex)
        {
            string reason = ex.ToString();
            if (s_lastFailure == reason)
            {
                return;
            }
            s_lastFailure = reason;
            ProjectorShowcaseMod.Log?.Warning("PawnshopBillboards 结构变化，跳过展示缓存联动补丁：\n" + ex);
        }
    }

    private static void UsesPostfix(GameInventory __0, ref bool __result)
    {
        if (!__result && ProjectorShowcaseMod.Active && ProjectorStorage.IsProjectorStorage(__0))
        {
            __result = true;
        }
    }
}
