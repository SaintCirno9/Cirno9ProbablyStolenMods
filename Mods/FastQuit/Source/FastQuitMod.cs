using UnityEngine;

namespace FastQuit;

/// <summary>
/// 模组入口：订阅退出请求，退出加速由 <see cref="QuitUnpatchGuard"/> 完成。
/// Author: SaintCirno9
/// </summary>
public sealed class FastQuitMod : IMod
{
    internal static ModLog Log { get; private set; }

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
    }

    public void OnEnable()
    {
        Application.wantsToQuit += QuitUnpatchGuard.OnWantsToQuit;
    }

    public void OnDisable()
    {
        Application.wantsToQuit -= QuitUnpatchGuard.OnWantsToQuit;
    }
}
