using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace ModKeybindHub;

/// <summary>
/// 把原版按键分区中的模组条目按所属模组搬到独立分区，并镜像模组配置里的按键项。
/// 条目对象本身原样搬移，getter/setter/source 不变，原模组的改键、保存与 RemoveBySource 照常生效。
/// Author: SaintCirno9
/// </summary>
internal static class KeybindHub
{
    private const string SectionPrefix = KeybindHubMod.ModId + ":";

    private const string MirrorPrefix = KeybindHubMod.ModId + ":cfg:";

    private const string ConflictLinkPrefix = "keyhub_conflict:";

    /// <summary>与原版 Warning 文本相近的红色。</summary>
    private const string WarningColor = "#E05A4F";

    private sealed class Owner
    {
        public string Key;

        public string Name;

        /// <summary>在 ModLoader.Mods 中的顺序，用于分区排序；非已加载模组排在最后。</summary>
        public int Order;
    }

    private static readonly string[] TextHotkeyHints = { "key", "bind", "shortcut", "control", "热键", "快捷键", "按键", "键位" };

    private static readonly HashSet<string> s_warnedSources = new HashSet<string>();

    /// <summary>本模组分区 ID → 所属模组名，用于冲突提示。</summary>
    private static readonly Dictionary<string, string> s_sectionOwnerNames = new Dictionary<string, string>();

    internal static bool Active;

    internal static void Organize()
    {
        Dictionary<string, Owner> owners = new Dictionary<string, Owner>();
        MoveModEntries(owners);
        SyncConfigMirrors(owners);
        MoveSectionsToEnd(owners);
    }

    /// <summary>禁用时把搬走的条目放回原版按键分区，并删除镜像条目与本模组分区。</summary>
    internal static void Restore()
    {
        Active = false;
        GameOptionRegistry.Section keybind = GameOptionRegistry.FindSection(GameOptionRegistry.SECTION_KEYBIND);
        foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
        {
            if (!IsHubSection(section))
            {
                continue;
            }
            for (int i = section.entries.Count - 1; i >= 0; i--)
            {
                if (!IsMirror(section.entries[i]))
                {
                    keybind?.entries.Add(section.entries[i]);
                    section.entries.RemoveAt(i);
                }
            }
        }
        GameOptionRegistry.RemoveBySource(KeybindHubMod.ModId);
        s_sectionOwnerNames.Clear();
        List<string> links = new List<string>();
        foreach (string linkId in TMPIconHover.CustomLinks.Keys)
        {
            if (linkId.StartsWith(ConflictLinkPrefix, StringComparison.Ordinal))
            {
                links.Add(linkId);
            }
        }
        links.ForEach(linkId => TMPIconHover.CustomLinks.Remove(linkId));
    }

    private static void MoveModEntries(Dictionary<string, Owner> owners)
    {
        GameOptionRegistry.Section keybind = GameOptionRegistry.FindSection(GameOptionRegistry.SECTION_KEYBIND);
        if (keybind == null)
        {
            return;
        }
        int moved = 0;
        for (int i = 0; i < keybind.entries.Count;)
        {
            GameOptionRegistry.Entry entry = keybind.entries[i];
            Owner owner = ResolveOwner(entry, owners);
            if (owner == null)
            {
                i++;
                continue;
            }
            keybind.entries.RemoveAt(i);
            GetSection(owner).entries.Add(entry);
            moved++;
        }
        if (moved > 0)
        {
            KeybindHubMod.Log.Log($"已从原版按键分区搬出 {moved} 个模组条目。");
        }
    }

    /// <summary>
    /// 按条目回调所在程序集判定归属；回调来自游戏程序集时视为原版，除非 source 明确是某个已加载模组的 ID。
    /// </summary>
    private static Owner ResolveOwner(GameOptionRegistry.Entry entry, Dictionary<string, Owner> owners)
    {
        if (entry == null || IsMirror(entry))
        {
            return null;
        }
        Delegate callback = (Delegate)entry.getKey ?? (Delegate)entry.getBool ?? (Delegate)entry.getFloat
            ?? (Delegate)entry.getIndex ?? (Delegate)entry.setKey ?? entry.label;
        System.Reflection.Assembly assembly = callback?.Method.DeclaringType?.Assembly;
        LoadedMod mod = null;
        if (assembly != null && assembly != typeof(GameOptionRegistry).Assembly)
        {
            mod = ModLoader.Mods.Find(m => m.Assembly == assembly);
        }
        if (mod == null && !string.IsNullOrEmpty(entry.source))
        {
            mod = ModLoader.Mods.Find(m => m.Manifest != null && m.Manifest.ID == entry.source);
        }
        if (mod == null && entry.id != null && entry.id.IndexOf(':') > 0)
        {
            // ModHelper.GameOptionId 的格式为 "<modId>:<optionId>"
            string prefix = entry.id.Substring(0, entry.id.IndexOf(':'));
            mod = ModLoader.Mods.Find(m => m.Manifest != null && m.Manifest.ID == prefix);
        }
        if (mod != null)
        {
            return GetOwner(owners, mod.Manifest.ID, ModName(mod), ModLoader.Mods.IndexOf(mod));
        }
        if (assembly == null || assembly == typeof(GameOptionRegistry).Assembly)
        {
            return null;
        }
        // 回调来自非游戏程序集但对不上已加载模组（例如模组带了多个 DLL），按 source 或程序集名单独成组
        string name = string.IsNullOrEmpty(entry.source) ? assembly.GetName().Name : entry.source;
        if (s_warnedSources.Add(name))
        {
            KeybindHubMod.Log.Warning($"条目 '{entry.id}' 来自程序集 {assembly.GetName().Name}，未匹配到已加载模组，按 '{name}' 单独分组。");
        }
        return GetOwner(owners, "other:" + name, name, int.MaxValue);
    }

    private static Owner GetOwner(Dictionary<string, Owner> owners, string key, string name, int order)
    {
        if (!owners.TryGetValue(key, out Owner owner))
        {
            owner = new Owner { Key = key, Name = name, Order = order };
            owners[key] = owner;
        }
        return owner;
    }

    private static string ModName(LoadedMod mod)
    {
        return string.IsNullOrEmpty(mod.Manifest.Name) ? mod.Manifest.ID : mod.Manifest.Name;
    }

    /// <summary>为每个运行中模组的 ModConfig 按键项（KeyCode 类型或以文本保存键名）建立可改键的镜像条目，并清理已失效的镜像。</summary>
    private static void SyncConfigMirrors(Dictionary<string, Owner> owners)
    {
        HashSet<string> wanted = new HashSet<string>();
        for (int index = 0; index < ModLoader.Mods.Count; index++)
        {
            LoadedMod mod = ModLoader.Mods[index];
            if (mod.Manifest == null || !mod.IsEnabled || mod.Instance == null)
            {
                continue;
            }
            ModConfig config = ModConfig.Find(mod.Manifest.ID);
            if (config == null)
            {
                continue;
            }
            HashSet<string> registered = null;
            foreach (ConfigEntry configEntry in config.Entries)
            {
                Func<KeyCode> get;
                Action<KeyCode> set;
                if (configEntry is ConfigEntry<KeyCode> keyEntry)
                {
                    get = () => keyEntry.Value;
                    set = keyEntry.Set;
                }
                else if (configEntry is ConfigEntry<string> textEntry && IsTextHotkey(textEntry))
                {
                    get = () => TryParseKey(textEntry.Value, out KeyCode key) ? key : KeyCode.None;
                    set = key => textEntry.Set(key.ToString());
                }
                else
                {
                    continue;
                }
                registered ??= RegisteredKeyBindIds(mod.Manifest.ID);
                if (registered.Contains(configEntry.Key))
                {
                    continue;
                }
                string optionId = MirrorPrefix + mod.Manifest.ID + ":" + configEntry.Key;
                wanted.Add(optionId);
                if (GameOptionRegistry.Find(optionId) != null)
                {
                    continue;
                }
                Owner owner = GetOwner(owners, mod.Manifest.ID, ModName(mod), index);
                GameOptionRegistry.Entry mirror = GameOptionRegistry.KeyBind(optionId, configEntry.Label, get, set, () => MirrorTooltip(configEntry));
                mirror.source = KeybindHubMod.ModId;
                GetSection(owner).entries.Add(mirror);
            }
        }
        foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
        {
            if (IsHubSection(section))
            {
                section.entries.RemoveAll(e => IsMirror(e) && !wanted.Contains(e.id));
            }
        }
    }

    /// <summary>
    /// 该模组已在选项窗口注册的按键条目 ID（去掉 "&lt;modId&gt;:" 与 "key_" 前缀）。
    /// 有些模组把同一个键位同时写进模组配置和选项窗口，ID 相同时不再镜像，避免重复条目和自我冲突。
    /// </summary>
    private static HashSet<string> RegisteredKeyBindIds(string modId)
    {
        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string modPrefix = modId + ":";
        string hubSectionId = SectionPrefix + modId;
        foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
        {
            foreach (GameOptionRegistry.Entry entry in section.entries)
            {
                if (entry.kind != GameOptionRegistry.Kind.KeyBind || entry.id == null || IsMirror(entry))
                {
                    continue;
                }
                if (section.id != hubSectionId && entry.source != modId && !entry.id.StartsWith(modPrefix, StringComparison.Ordinal))
                {
                    continue;
                }
                string id = entry.id.StartsWith(modPrefix, StringComparison.Ordinal) ? entry.id.Substring(modPrefix.Length) : entry.id;
                if (id.StartsWith("key_", StringComparison.Ordinal))
                {
                    id = id.Substring("key_".Length);
                }
                ids.Add(id);
            }
        }
        return ids;
    }

    /// <summary>
    /// 以文本保存键名的配置项（如 "F6"）：默认值必须是合法键名，且键名或分类带有快捷键相关字样，
    /// 避免把恰好等于键名的普通文本选项（如 "Space"、"Home"）当成快捷键。
    /// </summary>
    private static bool IsTextHotkey(ConfigEntry<string> entry)
    {
        if (!TryParseKey(entry.Default, out _))
        {
            return false;
        }
        foreach (string hint in TextHotkeyHints)
        {
            if (Contains(entry.Key, hint) || Contains(entry.Section, hint))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Contains(string text, string hint)
    {
        return text != null && text.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>只接受单个键名；Enum.TryParse 也会接受数字和逗号组合，这里排除。</summary>
    private static bool TryParseKey(string text, out KeyCode key)
    {
        key = KeyCode.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        text = text.Trim();
        if (char.IsDigit(text[0]) || text[0] == '-' || text[0] == '+' || text.IndexOf(',') >= 0)
        {
            return false;
        }
        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(typeof(KeyCode), key);
    }

    private static string MirrorTooltip(ConfigEntry entry)
    {
        string original = entry.Tooltip?.Invoke();
        string note = L("mirror_tooltip");
        return string.IsNullOrEmpty(original) ? note : original + "\n" + note;
    }

    private static GameOptionRegistry.Section GetSection(Owner owner)
    {
        string sectionId = SectionPrefix + owner.Key;
        GameOptionRegistry.Section section = GameOptionRegistry.FindSection(sectionId);
        if (section != null)
        {
            return section;
        }
        string name = owner.Name;
        s_sectionOwnerNames[sectionId] = name;
        // 标题沿用原版“按键绑定 / KEY BINDS”，与原版按键分区一致
        return GameOptionRegistry.GetOrAddSection(sectionId, () => LocHelper.GetLocalizedMainMenu("option_keybind") + " · " + name, KeybindHubMod.ModId);
    }

    /// <summary>把本模组分区移到列表末尾，按模组加载顺序排列。</summary>
    private static void MoveSectionsToEnd(Dictionary<string, Owner> owners)
    {
        List<GameOptionRegistry.Section> hubSections = GameOptionRegistry.Sections.FindAll(IsHubSection);
        if (hubSections.Count == 0)
        {
            return;
        }
        GameOptionRegistry.Sections.RemoveAll(IsHubSection);
        hubSections.Sort((a, b) =>
        {
            int order = SectionOrder(a, owners).CompareTo(SectionOrder(b, owners));
            return order != 0 ? order : string.CompareOrdinal(a.id, b.id);
        });
        GameOptionRegistry.Sections.AddRange(hubSections);
    }

    private static int SectionOrder(GameOptionRegistry.Section section, Dictionary<string, Owner> owners)
    {
        string key = section.id.Substring(SectionPrefix.Length);
        if (owners.TryGetValue(key, out Owner owner))
        {
            return owner.Order;
        }
        int index = ModLoader.Mods.FindIndex(m => m.Manifest != null && m.Manifest.ID == key);
        return index >= 0 ? index : int.MaxValue;
    }

    private static readonly MethodInfo s_refreshRules = AccessTools.Method(typeof(GameOptionUIManager), "RefreshRules");

    private static CustomUIWindow s_window;

    private static Dictionary<string, KeyCode> s_pendingKeys;

    private static bool s_rightClickWarned;

    /// <summary>给窗口内所有按键按钮挂上右键清空组件，并记下当前窗口与原版待应用按键表。</summary>
    internal static void AttachRightClick(CustomUIWindow window, Dictionary<string, KeyCode> pendingKeys)
    {
        s_window = window;
        s_pendingKeys = pendingKeys;
        if (s_refreshRules == null)
        {
            if (!s_rightClickWarned)
            {
                s_rightClickWarned = true;
                KeybindHubMod.Log.Warning("未找到 GameOptionUIManager.RefreshRules，右键清空不可用。");
            }
            return;
        }
        foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
        {
            foreach (GameOptionRegistry.Entry entry in section.entries)
            {
                if (entry.kind != GameOptionRegistry.Kind.KeyBind)
                {
                    continue;
                }
                Button button = window.Get(entry.id)?.button;
                if (button == null)
                {
                    continue;
                }
                RightClickClear clear = button.GetComponent<RightClickClear>() ?? button.gameObject.AddComponent<RightClickClear>();
                clear.OptionId = entry.id;
            }
        }
    }

    /// <summary>
    /// 只改原版待应用按键表，与原版捕获到新键后的处理一致；点“应用”时才由条目自己的 setter 写回。
    /// 原版捕获可绑定鼠标右键，按下右键时正在捕获（或当帧刚结束捕获）的点击由 RightClickClear 过滤掉。
    /// </summary>
    internal static void ClearPendingKey(string optionId, Button button)
    {
        GameOptionUIManager ui = GameOptionUIManager.Instance;
        if (!Active || ui == null || !ui.IsOpen || s_window == null || s_pendingKeys == null || button == null || !button.interactable)
        {
            return;
        }
        CustomUIElement element = s_window.Get(optionId);
        // 按钮对象可能来自已关闭的旧窗口，必须确认仍是当前窗口里该条目的按钮
        if (element == null || element.button != button || !s_pendingKeys.ContainsKey(optionId))
        {
            return;
        }
        if (KeyBindHelper.IsCapturing)
        {
            return;
        }
        try
        {
            s_pendingKeys[optionId] = KeyCode.None;
            element.SetText(KeyBindHelper.KeyName(KeyCode.None));
            s_refreshRules.Invoke(ui, null);
        }
        catch (Exception e)
        {
            KeybindHubMod.Log.Error($"右键清空条目 '{optionId}' 失败：{e}");
        }
    }

    /// <summary>
    /// 在按键条目名称后追加冲突标记，悬停列出同键条目；为本模组分区条目改键时改为显示原版的改键状态
    /// （原版只在按键设置分区标题旁显示，滚到末尾时看不到）。每次原版刷新规则后整体重写名称文本。
    /// </summary>
    internal static void DecorateLabels(GameOptionUIManager ui, CustomUIWindow window)
    {
        string capturing = KeyBindHelper.CapturingOptionId;
        string status = GameOptionUIManager.KeyBindStatus;
        foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
        {
            foreach (GameOptionRegistry.Entry entry in section.entries)
            {
                if (entry.kind != GameOptionRegistry.Kind.KeyBind)
                {
                    continue;
                }
                CustomUIElement label = window.Get("label_" + entry.id);
                if (label == null || !label.IsAlive)
                {
                    continue;
                }
                string suffix = "";
                if (entry.id == capturing && IsHubSection(section) && !string.IsNullOrEmpty(status))
                {
                    suffix = " <color=" + WarningColor + ">" + status + "</color>";
                }
                else
                {
                    string tooltip = ConflictTooltip(ui, entry, out int count);
                    if (count > 0)
                    {
                        string linkId = ConflictLinkPrefix + entry.id;
                        TMPIconHover.CustomLinks[linkId] = () => tooltip;
                        suffix = " <link=\"" + linkId + "\"><color=" + WarningColor + ">" + string.Format(L("conflict_marker"), count) + "</color></link>";
                    }
                }
                label.SetText(BaseLabel(entry) + suffix);
            }
        }
    }

    /// <summary>与原版 GameOptionUIManager.BuildEntry 生成的名称文本保持一致。</summary>
    private static string BaseLabel(GameOptionRegistry.Entry entry)
    {
        string text = Label(entry);
        if (entry.tooltip != null)
        {
            text += " <link=\"gameopt:" + entry.id + "\"><sprite name=\"info\" tint=1></link>";
        }
        return text;
    }

    /// <summary>列出窗口内待应用按键与该条目相同的其他按键条目及其所属模组，以及开启时的开发者工具。</summary>
    private static string ConflictTooltip(GameOptionUIManager ui, GameOptionRegistry.Entry entry, out int count)
    {
        count = 0;
        KeyCode key = ui.GetKey(entry.id);
        if (key == KeyCode.None)
        {
            return null;
        }
        StringBuilder text = null;
        foreach (GameOptionRegistry.Section section in GameOptionRegistry.Sections)
        {
            foreach (GameOptionRegistry.Entry other in section.entries)
            {
                if (other == entry || other.kind != GameOptionRegistry.Kind.KeyBind || ui.GetKey(other.id) != key)
                {
                    continue;
                }
                text ??= new StringBuilder(string.Format(L("conflict_tooltip_title"), KeyBindHelper.KeyName(key)));
                text.Append('\n').Append(string.Format(L("conflict_tooltip_item"), Label(other), OwnerName(section)));
                count++;
            }
        }
        string devTool = DevToolUsing(key);
        if (devTool != null)
        {
            text ??= new StringBuilder(string.Format(L("conflict_tooltip_title"), KeyBindHelper.KeyName(key)));
            text.Append('\n').Append(string.Format(L("conflict_tooltip_item"), devTool, L("owner_devtools")));
            count++;
        }
        return text?.ToString();
    }

    /// <summary>
    /// 游戏目录有 dev.txt 时开发者工具写死占用 F9（调试面板，Shift+F9 为旧版面板）与 F8（控制台），
    /// 不在选项窗口中，单独计入冲突。
    /// </summary>
    private static string DevToolUsing(KeyCode key)
    {
        if (key != KeyCode.F8 && key != KeyCode.F9)
        {
            return null;
        }
        if (!DevTools.IsEnabled)
        {
            return null;
        }
        return key == KeyCode.F9 ? L("devtools_debug_panel") : L("devtools_console");
    }

    private static string OwnerName(GameOptionRegistry.Section section)
    {
        if (s_sectionOwnerNames.TryGetValue(section.id, out string name))
        {
            return name;
        }
        if (section.id == GameOptionRegistry.SECTION_KEYBIND)
        {
            return L("owner_vanilla");
        }
        try
        {
            return section.label?.Invoke() ?? section.id;
        }
        catch (Exception e)
        {
            KeybindHubMod.Log.Warning($"读取分区 '{section.id}' 的名称失败：{e.Message}");
            return section.id;
        }
    }
    private static string Label(GameOptionRegistry.Entry entry)
    {
        try
        {
            return entry.label?.Invoke() ?? entry.id;
        }
        catch (Exception e)
        {
            KeybindHubMod.Log.Warning($"读取条目 '{entry.id}' 的名称失败：{e.Message}");
            return entry.id;
        }
    }

    private static bool IsHubSection(GameOptionRegistry.Section section)
    {
        return section.source == KeybindHubMod.ModId && section.id != null && section.id.StartsWith(SectionPrefix, StringComparison.Ordinal);
    }

    private static bool IsMirror(GameOptionRegistry.Entry entry)
    {
        return entry.id != null && entry.id.StartsWith(MirrorPrefix, StringComparison.Ordinal);
    }

    private static string L(string key)
    {
        return ModHelper.GetLocalized(KeybindHubMod.ModId, key);
    }
}
