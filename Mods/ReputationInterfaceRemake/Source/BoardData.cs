using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReputationInterfaceRemake;

internal enum MarkerState
{
    Inactive,
    // 已达到阈值，但被其他模组（如势力声望扩展的势力日志任务）判定为未生效。
    Gated,
    Active
}

/// <summary>势力档案中一行的数据快照。</summary>
internal sealed class BoardEntry
{
    public ReputationElement Element;
    public StoreReputation Rep;
    public string Title;
    public Color TitleColor;
    public string Status;
    public string Number;
    public float Value;
    public List<BoardMarker> Markers = new List<BoardMarker>();
}

/// <summary>横条上的一个特性标记。</summary>
internal sealed class BoardMarker
{
    public StoreReputationPerk Perk;
    // 原版或其他模组创建的标记对象，悬停时把事件转发给它，以保留原有提示逻辑。
    public GameObject Source;
    public int Threshold;
    public Sprite Sprite;
    public MarkerState State;
    public Color? SourceTint;
    public Button SourceSwitch;

    public bool IsSwitchedOff(ReputationUIManager manager)
    {
        // 帮派与投资用骷髅表示手动关闭；奖励的解锁状态与该开关相互独立。
        return SourceSwitch != null && SourceSwitch.targetGraphic is Image icon
            && manager.spriteSkull != null && icon.sprite == manager.spriteSkull;
    }
}

/// <summary>
/// 从原版 layout 下的 ReputationElement 收集数据。原版行由 StoreReputation 生成，
/// 其他模组（如深空诊所）直接插入的行没有 StoreReputation，只能从其控件读取。
/// </summary>
internal static class BoardData
{
    // 原版 ReputationElement.GetBarHeight：65.5 * value / 100。
    private const float BarHeightPerPoint = 0.655f;

    // 标题颜色感知亮度上限：原版行背景偏亮，过亮的势力色（如深空诊所的浅青色）会看不清。
    private const float MaxTitleBrightness = 0.55f;

    private static readonly AccessTools.FieldRef<PerkElement, StoreReputationPerk> PerkRef =
        AccessTools.FieldRefAccess<PerkElement, StoreReputationPerk>("perk");

    private const string AugmentedFactionId = "FACTION_AUGMENTED";
    private const string AugmentedModId = "faction_reputation_expansion";
    private const string LighthouseFactionId = "DISASTERDAY_LIGHTHOUSE";
    private const string DisasterDayModId = "disaster_day";
    private const string LighthouseRepKey = "lighthouse_rep";
    private const string HenchmanStarPrefix = "HENCH_STAR_";
    private const string HenchmanSwitchPrefix = "HENCH_SWITCH_";
    private const string HenchmanRiskPrefix = "HENCH_RISK_";

    private static readonly int[] AugmentedThresholds =
    {
        -100, -80, -60, -40, -20, 20, 40, 60, 80, 100, 120, 140, 160, 180, 200
    };

    private static readonly (int Threshold, string Key)[] LighthousePerks =
    {
        (-40, "NO_REPORTS"),
        (-20, "NO_THANKS"),
        (20, "BUYERS"),
        (40, "RICH_BUYERS"),
        (60, "SELLERS"),
        (80, "EXTRA_THANKS"),
        (100, "CONTROLLED_GOODS")
    };

    private static readonly Dictionary<int, KeyValuePair<ReputationElement, StoreReputation>> Bound =
        new Dictionary<int, KeyValuePair<ReputationElement, StoreReputation>>();

    // 原版 OpenUI 用 Object.Destroy 清理旧行，销毁前它们仍留在层级中，需要跳过。
    private static readonly HashSet<GameObject> Stale = new HashSet<GameObject>();

    // 缓存调试预览用的 StoreReputation，保证 0.25s 刷新周期内 perk 引用稳定而不触发反复重建。
    private static readonly Dictionary<string, StoreReputation> DebugReps = new Dictionary<string, StoreReputation>();

    public static void Bind(ReputationElement element, StoreReputation rep)
    {
        if (element != null && rep != null)
        {
            Bound[element.GetInstanceID()] = new KeyValuePair<ReputationElement, StoreReputation>(element, rep);
        }
    }

    public static void MarkStale(Transform layout)
    {
        Stale.RemoveWhere(go => go == null);
        if (layout != null)
        {
            for (int i = 0; i < layout.childCount; i++)
            {
                Stale.Add(layout.GetChild(i).gameObject);
            }
        }
        foreach (int id in Bound.Where(kv => kv.Value.Key == null).Select(kv => kv.Key).ToList())
        {
            Bound.Remove(id);
        }
    }

    /// <summary>当独占行（如灾变日灯塔）刚被创建时，将同级其余未销毁行标为旧行以防止混入采集。</summary>
    public static void MarkSiblingsStale(Transform layout, string exclusiveName)
    {
        Stale.RemoveWhere(go => go == null);
        if (layout == null || string.IsNullOrEmpty(exclusiveName))
        {
            return;
        }
        GameObject keeper = null;
        for (int i = 0; i < layout.childCount; i++)
        {
            GameObject go = layout.GetChild(i).gameObject;
            // 不判断 activeSelf，避免其他模组分页器停用该行后导致排他识别失效。
            if (!Stale.Contains(go) && go.name == exclusiveName && go.GetComponent<ReputationElement>() != null)
            {
                keeper = go;
                break;
            }
        }
        if (keeper == null)
        {
            return;
        }
        for (int i = 0; i < layout.childCount; i++)
        {
            GameObject go = layout.GetChild(i).gameObject;
            if (go != keeper)
            {
                Stale.Add(go);
            }
        }
    }

    public static PerkElement FirstPerkElement(ReputationElement element)
    {
        return element != null ? element.GetComponentInChildren<PerkElement>(true) : null;
    }

    public static List<BoardEntry> Collect(ReputationUIManager manager)
    {
        Stale.RemoveWhere(go => go == null);
        var result = new List<BoardEntry>();
        var presentFactions = new HashSet<string>();
        Transform layout = manager.layout.transform;
        List<StoreReputation> reps = PlayerStore.Instance != null ? PlayerStore.Instance.storeReputations : null;
        for (int i = 0; i < layout.childCount; i++)
        {
            Transform child = layout.GetChild(i);
            if (Stale.Contains(child.gameObject))
            {
                continue;
            }
            ReputationElement element = child.GetComponent<ReputationElement>();
            if (element == null)
            {
                continue;
            }
            var entry = new BoardEntry { Element = element };
            bool inPlayerStore = false;
            if (Bound.TryGetValue(element.GetInstanceID(), out var bound) && bound.Value != null)
            {
                entry.Rep = bound.Value;
                inPlayerStore = reps != null && reps.Contains(bound.Value);
                if (!string.IsNullOrEmpty(bound.Value.factionId))
                {
                    presentFactions.Add(bound.Value.factionId);
                }
            }
            Fill(entry, manager, inPlayerStore);
            result.Add(entry);
        }
        // 在正常声望面板开启调试模式时，追加当前存档尚未解锁的势力预览行；灯塔独立面板保持独占测试。
        if (ReputationInterfaceRemakeMod.DebugShowAllReputations
            && !presentFactions.Contains(LighthouseFactionId))
        {
            AppendDebugEntries(result, presentFactions, manager);
        }
        return result;
    }

    private static void Fill(BoardEntry entry, ReputationUIManager manager, bool inPlayerStore)
    {
        ReputationElement element = entry.Element;
        if (element != null)
        {
            entry.Title = element.reputationTitle != null ? element.reputationTitle.text : "";
            entry.TitleColor = Readable(element.reputationTitle != null ? element.reputationTitle.color : Color.black);
        }
        else if (entry.Rep != null)
        {
            entry.Title = entry.Rep.factionDisplay ?? "";
            entry.TitleColor = Readable(!string.IsNullOrEmpty(entry.Rep.factionColor2)
                ? MetaHelper.HexToColor(entry.Rep.factionColor2)
                : Color.black);
        }
        if (entry.Rep != null)
        {
            double exact = entry.Rep.GetReputationExact();
            entry.Value = (float)exact;
            // 不在 PlayerStore 中的独立声望行（如垃圾场拓展）可能在 Init 后改写状态称号，优先保留控件文本。
            string elementStatus = !inPlayerStore && element != null && element.reputationStatus != null
                ? element.reputationStatus.text
                : null;
            entry.Status = !string.IsNullOrEmpty(elementStatus)
                ? elementStatus
                : StoreReputation.GetReputationStatus(entry.Rep.GetReputation());
            entry.Number = exact.ToString("+0.00;-0.00;0.00");
            AddPerkMarkers(entry, manager, exact, inPlayerStore);
        }
        else if (element != null)
        {
            entry.Status = element.reputationStatus != null ? element.reputationStatus.text : "";
            string number = element.reputationNumber != null ? element.reputationNumber.text.Replace("/200", "").Trim() : "";
            // 直接插入的行数值格式各异（深空诊所用 "0.##"），能解析时统一成原版行的格式，并优先于横条长度取值。
            if (float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                entry.Value = parsed;
                entry.Number = parsed.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            }
            else
            {
                entry.Value = BarValue(element);
                entry.Number = number;
            }
            AddForeignMarkers(entry, manager);
        }
        // 稳定排序，保证同阈值标记的先后顺序在刷新间一致。
        entry.Markers = entry.Markers.OrderBy(m => m.Threshold).ToList();
    }

    private static void AddPerkMarkers(BoardEntry entry, ReputationUIManager manager, double exact, bool inPlayerStore)
    {
        // 优先使用原版 PerkElement 上的图标：其他模组（如势力声望扩展）通过补丁 SetIcon 替换了扩展特性的图标。
        var sources = new Dictionary<StoreReputationPerk, PerkElement>();
        if (entry.Element != null)
        {
            foreach (Transform child in entry.Element.transform)
            {
                PerkElement perkElement = child.GetComponent<PerkElement>();
                StoreReputationPerk perk = perkElement != null ? PerkRef(perkElement) : null;
                if (perk != null && !sources.ContainsKey(perk))
                {
                    sources[perk] = perkElement;
                }
            }
        }
        if (entry.Rep.perks != null)
        {
            foreach (StoreReputationPerk perk in entry.Rep.perks)
            {
                if (perk == null)
                {
                    continue;
                }
                sources.TryGetValue(perk, out PerkElement source);
                Sprite sprite = source != null && source.image != null && source.image.sprite != null
                    ? source.image.sprite
                    : NativeSprite(manager, perk.perkThreshold);
                bool reached = Reached(perk, exact);
                // 独立管理或调试预览行不在 PlayerStore.storeReputations 中，IsPerkUnlocked 会恒返回 false，需按 perk 自身解锁标记判定。
                bool active = inPlayerStore
                    ? reached && StoreReputation.IsPerkUnlocked(perk.perkId)
                    : reached && (perk.isAlwaysActive || perk.isUnlocked);
                entry.Markers.Add(new BoardMarker
                {
                    Perk = perk,
                    Source = source != null ? source.gameObject : null,
                    Threshold = perk.perkThreshold,
                    Sprite = sprite,
                    State = active ? MarkerState.Active : inPlayerStore && reached ? MarkerState.Gated : MarkerState.Inactive
                });
            }
        }
        AddHenchmanMarkers(entry, sources, exact);
    }

    private static bool Reached(StoreReputationPerk perk, double exact)
    {
        return perk.isAlwaysActive || (perk.perkThreshold < 0 ? exact <= perk.perkThreshold : exact >= perk.perkThreshold);
    }

    // 帮派与投资的特性只挂在原版行上，不进入 StoreReputation.perks，也不由 IsPerkUnlocked 判定。
    private static void AddHenchmanMarkers(BoardEntry entry, Dictionary<StoreReputationPerk, PerkElement> sources, double exact)
    {
        foreach (var pair in sources)
        {
            StoreReputationPerk perk = pair.Key;
            PerkElement source = pair.Value;
            if (!source.gameObject.activeSelf || source.image == null || string.IsNullOrEmpty(perk.perkId)
                || !source.name.StartsWith(HenchmanStarPrefix, StringComparison.Ordinal)
                || source.name != HenchmanStarPrefix + perk.perkId
                || entry.Markers.Any(marker => marker.Perk?.perkId == perk.perkId))
            {
                continue;
            }
            Transform switchObject = source.transform.parent.Find(HenchmanSwitchPrefix + perk.perkId);
            Button sourceSwitch = switchObject != null && switchObject.gameObject.activeSelf ? switchObject.GetComponent<Button>() : null;
            // 风险协作不是普通声望档位，保留原图标表达的可用、锁定和关闭颜色。
            bool risk = perk.perkId.StartsWith(HenchmanRiskPrefix, StringComparison.Ordinal);
            bool reached = Reached(perk, exact);
            entry.Markers.Add(new BoardMarker
            {
                Perk = perk,
                Source = source.gameObject,
                Threshold = perk.perkThreshold,
                Sprite = source.image.sprite,
                State = risk || perk.isUnlocked || perk.isAlwaysActive ? MarkerState.Active : reached ? MarkerState.Gated : MarkerState.Inactive,
                SourceTint = source.image.color,
                SourceSwitch = sourceSwitch
            });
        }
    }

    private static void AddForeignMarkers(BoardEntry entry, ReputationUIManager manager)
    {
        var seenPerks = new HashSet<StoreReputationPerk>();
        foreach (Transform child in entry.Element.transform)
        {
            // 原版行预制体自带一个停用的 ReputationPerkElement 模板，原版界面不显示，这里也要跳过。
            if (!child.gameObject.activeSelf || child.GetComponent<IPointerEnterHandler>() == null)
            {
                continue;
            }
            Image icon = FindIcon(child.gameObject, manager);
            if (icon == null)
            {
                continue;
            }
            PerkElement perkElement = child.GetComponent<PerkElement>();
            StoreReputationPerk perk = perkElement != null ? PerkRef(perkElement) : null;
            if (perk != null && !seenPerks.Add(perk))
            {
                continue;
            }
            // 原版 PerkElement.Init 固定把图标设为白色，若能取到 perk 则优先按其状态判断，无 perk 再退回图标亮度。
            int threshold = perk != null ? perk.perkThreshold : Mathf.RoundToInt(((RectTransform)child).anchoredPosition.y / BarHeightPerPoint);
            bool active = perk != null
                ? perk.isAlwaysActive || perk.isUnlocked
                : icon.color.r > 0.9f && icon.color.a > 0.9f;
            entry.Markers.Add(new BoardMarker
            {
                Perk = perk,
                Source = child.gameObject,
                Threshold = threshold,
                Sprite = icon.sprite,
                State = active ? MarkerState.Active : MarkerState.Inactive
            });
        }
    }

    private static void AppendDebugEntries(List<BoardEntry> result, HashSet<string> presentFactions, ReputationUIManager manager)
    {
        if (!presentFactions.Contains(StoreReputation.FACTION_CARTEL))
        {
            AddDebugEntry(result, presentFactions, manager, GetDebugRep(
                StoreReputation.FACTION_CARTEL,
                ResolveCartelDisplayName(),
                "44464c",
                () => StoreReputation.CreatePerks(StoreReputation.FACTION_CARTEL)));
        }
        if (ModHelper.IsModRunning(AugmentedModId) && !presentFactions.Contains(AugmentedFactionId))
        {
            AddDebugEntry(result, presentFactions, manager, GetDebugRep(
                AugmentedFactionId,
                ResolveAugmentedDisplayName(),
                "3E8E86",
                CreateAugmentedPerks));
        }
        if (ModHelper.IsModRunning(DisasterDayModId) && !presentFactions.Contains(LighthouseFactionId))
        {
            // 灾变日将灯塔声望独立保存在 ModData 中（未写入 PlayerStore.storeReputations），调试预览时需实时同步真实数值与特性解锁状态。
            AddDebugEntry(result, presentFactions, manager, GetDebugRep(
                LighthouseFactionId,
                ResolveModLocalized(DisasterDayModId, "guide_seal", "LIGHTHOUSE"),
                "1E3A5F",
                CreateLighthousePerks,
                ModHelper.GetModDataInt(DisasterDayModId, LighthouseRepKey, 0)));
        }
    }

    private static void AddDebugEntry(List<BoardEntry> result, HashSet<string> presentFactions, ReputationUIManager manager, StoreReputation rep)
    {
        var entry = new BoardEntry { Rep = rep };
        Fill(entry, manager, inPlayerStore: false);
        result.Add(entry);
        presentFactions.Add(rep.factionId);
    }

    private static StoreReputation GetDebugRep(string factionId, string display, string color2, Func<List<StoreReputationPerk>> createPerks, int value = 0)
    {
        if (!DebugReps.TryGetValue(factionId, out StoreReputation rep))
        {
            rep = new StoreReputation
            {
                factionId = factionId,
                factionColor2 = color2,
                perks = createPerks()
            };
            DebugReps[factionId] = rep;
        }
        rep.factionDisplay = display;
        SyncDebugRep(rep, value);
        return rep;
    }

    /// <summary>同步调试预览行的实时数值与特性解锁标记，避免缓存旧状态或在独立面板中误显为 0。</summary>
    private static void SyncDebugRep(StoreReputation rep, int value)
    {
        int clamped = Mathf.Clamp(value, -200, 200);
        if (Math.Abs(rep.GetReputationExact() - clamped) > 0.0001
            && PlayerStore.Instance != null
            && PlayerStore.Instance.storeReputations != null)
        {
            rep.SetReputation(clamped);
        }
        if (rep.perks == null)
        {
            return;
        }
        foreach (StoreReputationPerk perk in rep.perks)
        {
            if (perk != null)
            {
                perk.isUnlocked = perk.perkThreshold < 0 ? clamped <= perk.perkThreshold : clamped >= perk.perkThreshold;
            }
        }
    }

    private static List<StoreReputationPerk> CreateAugmentedPerks()
    {
        var perks = new List<StoreReputationPerk>(AugmentedThresholds.Length);
        foreach (int threshold in AugmentedThresholds)
        {
            perks.Add(new StoreReputationPerk
            {
                perkId = "FRE_AUG_" + (threshold < 0 ? "N" : "P") + Math.Abs(threshold).ToString("D3", CultureInfo.InvariantCulture),
                perkThreshold = threshold,
                perkType = threshold >= 0 ? 1 : -1,
                iconName = "Referal"
            });
        }
        return perks;
    }

    private static List<StoreReputationPerk> CreateLighthousePerks()
    {
        var perks = new List<StoreReputationPerk>(LighthousePerks.Length);
        foreach (var item in LighthousePerks)
        {
            perks.Add(new StoreReputationPerk
            {
                perkId = "DISASTERDAY_LH_" + item.Key,
                perkThreshold = item.Threshold,
                perkType = item.Threshold >= 0 ? 1 : -1,
                iconName = "Referal"
            });
        }
        return perks;
    }

    private static string ResolveCartelDisplayName()
    {
        string name = LocHelper.GetLocalizedClientName("faction_name_cartel");
        return string.IsNullOrEmpty(name) || name == "faction_name_cartel" ? "Cartel" : name;
    }

    private static string ResolveAugmentedDisplayName()
    {
        if (PlayerStore.Instance != null && PlayerStore.Instance.storeReputations != null)
        {
            string name = StoreReputation.GetFactionNameByID(AugmentedFactionId);
            if (!string.IsNullOrEmpty(name) && name != "NONE")
            {
                return name;
            }
        }
        return "Newborn Alliance";
    }

    private static string ResolveModLocalized(string modId, string key, string fallback)
    {
        string text = ModHelper.GetLocalized(modId, key);
        return string.IsNullOrEmpty(text) || text == key ? fallback : text;
    }

    private static Image FindIcon(GameObject marker, ReputationUIManager manager)
    {
        Image fallback = null;
        foreach (Image image in marker.GetComponentsInChildren<Image>(true))
        {
            if (image.sprite == null)
            {
                continue;
            }
            if (IsNativeSprite(manager, image.sprite))
            {
                return image;
            }
            fallback = image;
        }
        return fallback;
    }

    internal static bool IsNativeSprite(ReputationUIManager m, Sprite sprite)
    {
        return sprite == m.spriteStar || sprite == m.spriteHappy || sprite == m.spriteSmile
            || sprite == m.spriteSad || sprite == m.spriteAngry || sprite == m.spriteSkull;
    }

    // 与原版 PerkElement.SetIcon 相同的阈值分档。
    public static Sprite NativeSprite(ReputationUIManager m, int threshold)
    {
        if (threshold >= 100) return m.spriteStar;
        if (threshold >= 50) return m.spriteHappy;
        if (threshold >= 0) return m.spriteSmile;
        if (threshold >= -50) return m.spriteSad;
        if (threshold > -100) return m.spriteAngry;
        return m.spriteSkull;
    }

    private static Color Readable(Color color)
    {
        float brightness = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
        if (brightness <= MaxTitleBrightness)
        {
            return color;
        }
        float scale = MaxTitleBrightness / brightness;
        return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
    }

    private static float BarValue(ReputationElement element)
    {
        if (element.greenImage != null && element.greenImage.gameObject.activeSelf)
        {
            return element.greenImage.rectTransform.sizeDelta.y / BarHeightPerPoint;
        }
        if (element.redImage != null && element.redImage.gameObject.activeSelf)
        {
            return -element.redImage.rectTransform.sizeDelta.y / BarHeightPerPoint;
        }
        return 0f;
    }
}
