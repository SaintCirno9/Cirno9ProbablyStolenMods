namespace SliderNumericInput;

/// <summary>
/// 模组入口：为选项窗口与模组配置窗口中的滑条数值显示添加点击原地输入功能。
/// Author: SaintCirno9
/// </summary>
public sealed class SliderNumericInputMod : IMod
{
    internal const string ModId = "cirno9.slider_numeric_input";

    internal static ModLog Log { get; private set; }
    internal static bool Active { get; private set; }

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
    }

    public void OnEnable()
    {
        Active = true;
        Log.Log("已启用：点击滑条旁显示的数值即可直接输入数字修改。");
    }

    public void OnDisable()
    {
        Active = false;
        SliderEditCoordinator.ClearAll();
    }
}
