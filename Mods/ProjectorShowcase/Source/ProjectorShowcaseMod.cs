using UnityEngine;

namespace ProjectorShowcase;

/// <summary>
/// 幻影投影仪展示区模组入口。
/// Author: SaintCirno9
/// </summary>
public sealed class ProjectorShowcaseMod : IMod
{
    internal const string ModId = "cirno9.projector_showcase";
    private const string SectionTitle = "投影仪展示区 / Projector Showcase";
    private const string SectionId = ModId + ".options";

    internal const int MinWidth = 3;
    internal const int MaxWidth = 16;
    internal const int MinHeight = 3;
    internal const int MaxHeight = 12;

    internal static ModLog Log { get; private set; }
    internal static ConfigEntry<int> StorageWidth { get; private set; }
    internal static ConfigEntry<int> StorageHeight { get; private set; }
    internal static bool Active { get; private set; }

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
        ModConfig config = ModConfig.For(ModId);
        StorageWidth = config.Define(
            "width",
            ProjectorStorage.DefaultWidth,
            SectionTitle,
            () => Text("cfg_width", "展示区宽度（格） / Showcase width (cells)"),
            MinWidth,
            MaxWidth,
            () => Text("cfg_width_tip", "幻影投影仪展示区列数，默认 7 格。"));
        StorageHeight = config.Define(
            "height",
            ProjectorStorage.DefaultHeight,
            SectionTitle,
            () => Text("cfg_height", "展示区高度（格） / Showcase height (cells)"),
            MinHeight,
            MaxHeight,
            () => Text("cfg_height_tip", "幻影投影仪展示区行数，默认 5 格。"));
        BillboardCompat.Init("mod." + ModId);
    }

    public void OnEnable()
    {
        Active = true;
        StorageWidth.OnChanged += OnSizeConfigChanged;
        StorageHeight.OnChanged += OnSizeConfigChanged;
        ModHook.OnGameLoadedLate += OnStoreStateReady;
        ModHook.OnReturningStoreLate += OnStoreStateReady;

        GameOptionRegistry.GetOrAddSection(SectionId, () => SectionTitle, ModId);
        GameOptionRegistry.Entry widthEntry = GameOptionRegistry.Slider(
            ModId + ".width",
            StorageWidth.Label,
            () => StorageWidth.Value,
            value => StorageWidth.Set(Mathf.RoundToInt(value)),
            MinWidth,
            MaxWidth,
            true,
            StorageWidth.Tooltip);
        widthEntry.source = ModId;
        GameOptionRegistry.Add(SectionId, widthEntry);

        GameOptionRegistry.Entry heightEntry = GameOptionRegistry.Slider(
            ModId + ".height",
            StorageHeight.Label,
            () => StorageHeight.Value,
            value => StorageHeight.Set(Mathf.RoundToInt(value)),
            MinHeight,
            MaxHeight,
            true,
            StorageHeight.Tooltip);
        heightEntry.source = ModId;
        GameOptionRegistry.Add(SectionId, heightEntry);

        BillboardCompat.Start();
        ProjectorStorage.RefreshAllProjectors();
        Log.Log("已启用 1.0.3：幻影投影仪展示区默认 7×5 格，可在选项中调整宽高；投影仪生效时内部物品计入展示柜。");
    }

    public void OnDisable()
    {
        Active = false;
        StorageWidth.OnChanged -= OnSizeConfigChanged;
        StorageHeight.OnChanged -= OnSizeConfigChanged;
        ModHook.OnGameLoadedLate -= OnStoreStateReady;
        ModHook.OnReturningStoreLate -= OnStoreStateReady;
        GameOptionRegistry.RemoveBySource(ModId);
        BillboardCompat.Stop();
        ProjectorStorage.ClearTracking();
    }

    internal static string Text(string key, string fallback)
    {
        string value = ModHelper.GetLocalized(ModId, key);
        return string.IsNullOrEmpty(value) || value == key ? fallback : value;
    }

    private static void OnSizeConfigChanged(int _)
    {
        ProjectorStorage.RefreshAllSizes();
    }

    private static void OnStoreStateReady()
    {
        ProjectorStorage.RefreshAllProjectors();
    }
}
