namespace ModKeybindHub;

/// <summary>
/// 模组入口：每次打开选项窗口前由补丁整理模组快捷键分区。
/// Author: SaintCirno9
/// </summary>
public sealed class KeybindHubMod : IMod
{
    internal const string ModId = "cirno9.keybind_hub";

    internal static ModLog Log { get; private set; }

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
    }

    public void OnEnable()
    {
        KeybindHub.Active = true;
        Log.Log("已启用：打开选项窗口时按模组汇总快捷键。");
    }

    public void OnDisable()
    {
        KeybindHub.Restore();
    }
}
