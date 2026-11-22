using UnityEngine;

namespace CounterOverflowShelf;

/// <summary>
/// 模组入口：柜台摆不下的顾客货物与杰克逊博士库存改放进柜台旁的溢出货架，不再被原版销毁。
/// Author: SaintCirno9
/// </summary>
public sealed class OverflowShelfMod : IMod
{
    internal const string ModId = "cirno9.counter_overflow_shelf";

    internal const string SideRight = "右侧 / Right";

    internal const string SideLeft = "左侧 / Left";

    // 加载阶段 section 会立即求值，只能用常量，不能读当前语言。
    private const string Section = "柜台扩容货架 / Counter Expansion Shelf";

    private const string SupplyListModId = "supply.list";

    private static ConfigEntry<bool> s_enabled;

    private static ConfigEntry<int> s_visibleRows;

    private static ConfigEntry<int> s_jacksonRows;

    private static ConfigEntry<string> s_side;

    private GameObject m_runner;

    internal static ModLog Log { get; private set; }

    /// <summary>模组处于启用状态（加载器调用过 OnEnable 且尚未 OnDisable）。</summary>
    internal static bool Active { get; private set; }

    /// <summary>设置里的总开关；关闭后新的溢出货物恢复原版处理（原版翻页或销毁），已在货架上的货物不受影响。</summary>
    internal static bool CaptureEnabled => s_enabled == null || s_enabled.Value;

    /// <summary>玩家没有拖动调整过时的显示行数；杰克逊博士的柜台高得多，单独设置。</summary>
    internal static int DefaultRows(bool jackson)
    {
        ConfigEntry<int> entry = jackson ? s_jacksonRows : s_visibleRows;
        int fallback = jackson ? 12 : 4;
        return entry == null ? fallback : Mathf.Clamp(entry.Value, 2, 30);
    }

    internal static bool PreferLeft => s_side != null && s_side.Value == SideLeft;

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
        ModConfig config = ModHelper.GetConfig(ModId);
        if (config == null)
        {
            Log.Warning("读取模组配置失败，使用默认设置。");
            return;
        }
        s_enabled = config.Define("enabled", true, Section, () => Text("cfg_enabled"), () => Text("cfg_enabled_tip"));
        s_visibleRows = config.Define("visible_rows", 4, Section, () => Text("cfg_rows"), 2, 30, () => Text("cfg_rows_tip"));
        s_jacksonRows = config.Define("jackson_rows", 12, Section, () => Text("cfg_jackson_rows"), 2, 30, () => Text("cfg_jackson_rows_tip"));
        s_side = config.Define("side", SideRight, Section, () => Text("cfg_side"), new[] { SideRight, SideLeft }, () => Text("cfg_side_tip"));
        // 改设置表示要回到设置的值，丢掉拖动调整过的行数或位置。
        s_visibleRows.OnChanged += _ =>
        {
            ShelfLayout.ClearRows(jackson: false);
            OverflowShelf.ApplyRows();
        };
        s_jacksonRows.OnChanged += _ =>
        {
            ShelfLayout.ClearRows(jackson: true);
            OverflowShelf.ApplyRows();
        };
        s_side.OnChanged += _ =>
        {
            ShelfLayout.ClearOffsets();
            OverflowShelf.ResetPlacement();
        };
    }

    public void OnEnable()
    {
        Active = true;
        if (m_runner == null)
        {
            m_runner = new GameObject("CounterOverflowShelfRunner");
            Object.DontDestroyOnLoad(m_runner);
            m_runner.AddComponent<ShelfRunner>();
        }
        if (ModLoader.IsModEnabled(SupplyListModId))
        {
            Log.Warning("检测到「供货清单」（supply.list）：它也会接管柜台放不下的货物，与本模组同时启用时货物可能被两边重复处理，建议只保留其一。");
        }
        Log.Log("已启用：柜台放不下的货物会放进柜台扩容货架。");
    }

    public void OnDisable()
    {
        Active = false;
        OverflowShelf.Shutdown();
        if (m_runner != null)
        {
            Object.Destroy(m_runner);
            m_runner = null;
        }
    }

    internal static string Text(string key, params object[] args)
    {
        return ModHelper.GetLocalized(ModId, key, args);
    }
}
