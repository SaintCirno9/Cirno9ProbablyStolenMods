using UnityEngine;

namespace ReputationInterfaceRemake;

/// <summary>
/// 模组入口：在游戏原生“选项”界面注册开关；实际替换由补丁在打开势力档案时挂载 <see cref="BoardController"/> 完成。
/// Author: SaintCirno9
/// </summary>
public sealed class ReputationInterfaceRemakeMod : IMod
{
    internal const string ModId = "cirno9.reputation_interface_remake";

    private const string OptionSectionId = "settings";
    private const string PrefUseNewBoard = ModId + ".use_new_board";
    private const string PrefDebugShowAll = ModId + ".debug_show_all_reputations";

    private static bool _useNewBoard = true;
    private static bool _debugShowAllReputations;

    internal static ModLog Log { get; private set; }

    internal static bool UseNewBoard => _useNewBoard;
    internal static bool DebugShowAllReputations => _debugShowAllReputations;

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
        LoadSettings();
    }

    public void OnEnable()
    {
        LoadSettings();
        RegisterGameOptions();
        Log?.Log("已启用：打开势力档案时替换为横向行式界面；可在游戏“选项”中随时切换。");
    }

    public void OnDisable()
    {
        ModHelper.RemoveGameOptions(ModId);
        BoardController.DestroyAll();
    }

    private static void LoadSettings()
    {
        _useNewBoard = PlayerPrefs.GetInt(PrefUseNewBoard, 1) != 0;
        _debugShowAllReputations = PlayerPrefs.GetInt(PrefDebugShowAll, 0) != 0;
    }

    private static void RegisterGameOptions()
    {
        // 加载期禁止读取语言：所有标签与说明必须保持延迟委托，待玩家打开选项窗口时再求值。
        ModHelper.AddGameOptionSection(ModId, OptionSectionId,
            () => ModHelper.GetLocalized(ModId, "option_section"));
        ModHelper.AddGameOptionToggle(ModId, OptionSectionId, "use_new_board",
            () => ModHelper.GetLocalized(ModId, "config_use_new_board"),
            () => _useNewBoard,
            SetUseNewBoard,
            () => ModHelper.GetLocalized(ModId, "config_use_new_board_tip"));
        ModHelper.AddGameOptionToggle(ModId, OptionSectionId, "debug_show_all_reputations",
            () => ModHelper.GetLocalized(ModId, "config_debug_show_all"),
            () => _debugShowAllReputations,
            SetDebugShowAllReputations,
            () => ModHelper.GetLocalized(ModId, "config_debug_show_all_tip"));
    }

    private static void SetUseNewBoard(bool value)
    {
        _useNewBoard = value;
        PlayerPrefs.SetInt(PrefUseNewBoard, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static void SetDebugShowAllReputations(bool value)
    {
        _debugShowAllReputations = value;
        PlayerPrefs.SetInt(PrefDebugShowAll, value ? 1 : 0);
        PlayerPrefs.Save();
    }
}
