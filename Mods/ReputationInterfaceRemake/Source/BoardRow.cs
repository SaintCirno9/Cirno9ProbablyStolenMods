using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReputationInterfaceRemake;

/// <summary>按原版控件推导的字体与图标尺寸，保证新视图与游戏画布缩放一致。</summary>
internal sealed class BoardStyle
{
    public static readonly Color Ink = new Color(0.17f, 0.2f, 0.18f);
    public static readonly Color Muted = new Color(0.3f, 0.33f, 0.3f);
    public static readonly Color Positive = new Color(0.45f, 0.7f, 0.26f);
    public static readonly Color Negative = new Color(0.76f, 0.34f, 0.29f);

    public TMP_FontAsset Font;
    public Material FontMaterial;
    public float Title;
    public float Status;
    public float Number;
    public float Icon;

    public static BoardStyle From(ReputationUIManager manager, ReputationElement sample)
    {
        TextMeshProUGUI template = sample != null && sample.reputationTitle != null ? sample.reputationTitle : manager.perkdescription;
        var style = new BoardStyle
        {
            Font = template.font,
            FontMaterial = template.fontSharedMaterial,
            Title = Mathf.Clamp(template.fontSize, 12f, 40f),
            Status = Mathf.Clamp(sample != null && sample.reputationStatus != null ? sample.reputationStatus.fontSize : template.fontSize * 0.7f, 9f, 28f),
            Icon = 18f
        };
        style.Number = Mathf.Clamp(style.Title * 0.95f, 11f, 36f);
        PerkElement perk = BoardData.FirstPerkElement(sample);
        if (perk != null)
        {
            float height = ((RectTransform)perk.transform).rect.height;
            if (height > 1f)
            {
                style.Icon = Mathf.Clamp(height, 14f, 28f);
            }
        }
        return style;
    }
}

/// <summary>一行内的水平分区：势力名、数值、-200 到 200 横条。</summary>
internal readonly struct BoardGeometry
{
    public readonly float NameX;
    public readonly float NameWidth;
    public readonly float NumberX;
    public readonly float NumberWidth;
    public readonly float BarX;
    public readonly float BarWidth;

    public BoardGeometry(float width, float icon)
    {
        NameX = 12f;
        NameWidth = Mathf.Clamp(width * 0.17f, 90f, 220f);
        NumberX = NameX + NameWidth + 4f;
        NumberWidth = Mathf.Clamp(width * 0.085f, 56f, 110f);
        BarX = NumberX + NumberWidth + icon * 0.5f + 8f;
        BarWidth = Mathf.Max(width - icon * 0.5f - 8f - BarX, 100f);
    }

    public float X(float value)
    {
        return BarX + (Mathf.Clamp(value, -200f, 200f) + 200f) / 400f * BarWidth;
    }
}

internal sealed class BoardRow
{
    private const float Pad = 4f;
    private const float TrackHeight = 6f;
    private const float SwitchInset = 1f;
    private static readonly Color SwitchOnColor = new Color(0.22f, 0.43f, 0.25f);
    private static readonly Color SwitchThumbColor = new Color(0.95f, 0.96f, 0.9f);
    private static readonly Color GatedTint = new Color(1f, 0.78f, 0.42f, 1f);

    private sealed class GroupSlot
    {
        public int Start;
        public int Count;
        public Image Icon;
        public TextMeshProUGUI CountShadow;
        public TextMeshProUGUI CountLabel;
        public Image SwitchBadge;
        public RectTransform SwitchThumb;
        public GroupHover Hover;
    }

    private TextMeshProUGUI title;
    private TextMeshProUGUI status;
    private TextMeshProUGUI number;
    private Image swatch;
    private Image fill;
    private RectTransform valueMark;
    private BoardGeometry geometry;
    private float trackY;
    private BoardController controller;
    private readonly List<(int Index, Image Image, MarkerHover Hover, Image Badge, RectTransform Thumb)> markers =
        new List<(int, Image, MarkerHover, Image, RectTransform)>();
    private readonly List<GroupSlot> groups = new List<GroupSlot>();

    internal BoardEntry Entry { get; private set; }
    internal bool HasGroups => groups.Count > 0;

    public static BoardRow Build(RectTransform parent, BoardEntry entry, BoardGeometry geometry, BoardStyle style, int index)
    {
        var row = new BoardRow { geometry = geometry };
        float icon = style.Icon;
        // 图标中心距小于单个图标宽度时才会发生实际重叠；既避免把 -30 卡在 -40 和 -20 中间，又不误合并 +20 档位。
        float clusterGap = icon;
        row.trackY = Pad + icon + 8f;
        float textBlock = style.Title * 1.25f + style.Status * 1.25f;
        float height = Mathf.Max(row.trackY + 12f, textBlock + Pad * 2f);

        RectTransform root = Ui.Rect("Faction_" + index, parent);
        LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        Image background = root.gameObject.AddComponent<Image>();
        background.color = index % 2 == 0 ? new Color(0f, 0f, 0f, 0.07f) : new Color(1f, 1f, 1f, 0.06f);
        background.raycastTarget = false;

        row.swatch = Ui.Box(root, "Swatch", 3f, Pad, 4f, height - Pad * 2f, Color.white);
        float textTop = (height - textBlock) * 0.5f;
        row.title = Ui.Text(root, "Title", style, style.Title, geometry.NameX, textTop, geometry.NameWidth, style.Title * 1.25f,
            TextAlignmentOptions.MidlineLeft, BoardStyle.Ink);
        row.status = Ui.Text(root, "Status", style, style.Status, geometry.NameX, textTop + style.Title * 1.25f, geometry.NameWidth, style.Status * 1.25f,
            TextAlignmentOptions.MidlineLeft, BoardStyle.Muted);
        row.number = Ui.Text(root, "Number", style, style.Number, geometry.NumberX, (height - style.Number * 1.3f) * 0.5f, geometry.NumberWidth, style.Number * 1.3f,
            TextAlignmentOptions.MidlineRight, BoardStyle.Ink);

        Ui.Box(root, "Track", geometry.BarX, row.trackY - TrackHeight * 0.5f, geometry.BarWidth, TrackHeight, new Color(0.2f, 0.23f, 0.2f, 0.35f));
        row.fill = Ui.Box(root, "Fill", geometry.BarX, row.trackY - TrackHeight * 0.5f, 0f, TrackHeight, BoardStyle.Positive);
        for (int value = -200; value <= 200; value += 20)
        {
            float tick = value == 0 ? 16f : value % 100 == 0 ? 11f : 6f;
            float tickWidth = value == 0 ? 2f : 1f;
            Ui.Box(root, "Tick" + value, geometry.X(value) - tickWidth * 0.5f, row.trackY - tick * 0.5f, tickWidth, tick, new Color(0.17f, 0.2f, 0.18f, 0.6f));
        }
        row.valueMark = Ui.Box(root, "Value", geometry.BarX, row.trackY - 8f, 3f, 16f, BoardStyle.Ink).rectTransform;

        // 仅合并真正重叠或极度靠近的奖励，避免相邻阈值（如每 +20 一档）被大面积折叠。
        for (int i = 0; i < entry.Markers.Count;)
        {
            float center = geometry.X(entry.Markers[i].Threshold);
            int end = i + 1;
            while (end < entry.Markers.Count && geometry.X(entry.Markers[end].Threshold) - center < clusterGap)
            {
                end++;
            }
            if (end - i > 1)
            {
                row.BuildGroup(root, entry, style, center, i, end - i);
                i = end;
                continue;
            }

            Image image = Ui.Box(root, "Perk_" + entry.Markers[i].Threshold, center - icon * 0.5f, Pad, icon, icon, Color.white);
            image.preserveAspect = true;
            image.raycastTarget = true;
            MarkerHover hover = image.gameObject.AddComponent<MarkerHover>();
            (Image badge, RectTransform thumb) = entry.Markers[i].SourceSwitch != null
                ? BuildSwitchBadge(image.rectTransform, icon)
                : default;
            row.markers.Add((i, image, hover, badge, thumb));
            i = end;
        }
        return row;
    }

    private void BuildGroup(RectTransform root, BoardEntry entry, BoardStyle style, float center, int start, int count)
    {
        float icon = style.Icon;
        RectTransform groupRect = Ui.Place("PerkGroup", root, center - icon * 0.5f, Pad, icon, icon);
        Image hit = groupRect.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = groupRect.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => controller?.OpenMarkerGroup(this, groupRect, start, count));
        GroupHover hover = groupRect.gameObject.AddComponent<GroupHover>();

        Image mainIcon = Ui.Box(groupRect, "Icon", 0f, 0f, icon, icon, Color.white);
        mainIcon.preserveAspect = true;

        bool hasSwitch = false;
        for (int i = 0; i < count; i++)
        {
            if (entry.Markers[start + i].SourceSwitch != null)
            {
                hasSwitch = true;
                break;
            }
        }
        (Image switchBadge, RectTransform switchThumb) = hasSwitch
            ? BuildSwitchBadge(groupRect, icon)
            : default;

        float badgeHeight = Mathf.Clamp(icon * 0.55f, 10f, 13f);
        float badgeWidth = count >= 10 ? 16f : 12f;
        float badgeFontSize = Mathf.Clamp(icon * 0.52f, 9f, 12f);
        float badgeX = icon - badgeWidth + 2f;
        float badgeY = -3f;
        string countText = count.ToString();

        TextMeshProUGUI countShadow = Ui.Text(groupRect, "CountShadow", style, badgeFontSize, badgeX + 0.8f, badgeY + 0.8f, badgeWidth, badgeHeight,
            TextAlignmentOptions.TopRight, new Color(0.86f, 0.88f, 0.81f, 0.85f));
        countShadow.enableAutoSizing = false;
        countShadow.fontStyle = FontStyles.Bold;
        countShadow.overflowMode = TextOverflowModes.Overflow;
        countShadow.text = countText;

        TextMeshProUGUI countLabel = Ui.Text(groupRect, "Count", style, badgeFontSize, badgeX, badgeY, badgeWidth, badgeHeight,
            TextAlignmentOptions.TopRight, BoardStyle.Ink);
        countLabel.enableAutoSizing = false;
        countLabel.fontStyle = FontStyles.Bold;
        countLabel.overflowMode = TextOverflowModes.Overflow;
        countLabel.text = countText;

        groups.Add(new GroupSlot
        {
            Start = start,
            Count = count,
            Icon = mainIcon,
            CountShadow = countShadow,
            CountLabel = countLabel,
            SwitchBadge = switchBadge,
            SwitchThumb = switchThumb,
            Hover = hover
        });
    }

    internal static (Image Badge, RectTransform Thumb) BuildSwitchBadge(RectTransform marker, float icon)
    {
        // 小角标不依赖字体。拨片位置区分开关，两层图形均不拦截奖励图标的点击。
        float width = Mathf.Max(8f, icon * 0.54f);
        float height = Mathf.Max(4f, icon * 0.27f);
        float thumbSize = height - SwitchInset * 2f;
        Image badge = Ui.Box(marker, "SwitchBadge", icon - width + SwitchInset, icon - height + SwitchInset, width, height, BoardStyle.Ink);
        Image thumb = Ui.Box(badge.rectTransform, "Thumb", SwitchInset, SwitchInset, thumbSize, thumbSize, SwitchThumbColor);
        return (badge, thumb.rectTransform);
    }

    public void Apply(BoardEntry entry, ReputationUIManager manager, BoardController controller)
    {
        Entry = entry;
        this.controller = controller;
        title.text = entry.Title;
        title.color = entry.TitleColor;
        swatch.color = entry.TitleColor;
        status.text = entry.Status;
        number.text = entry.Number;
        number.color = entry.Value > 0f ? BoardStyle.Positive * 0.8f + BoardStyle.Ink * 0.2f
            : entry.Value < 0f ? BoardStyle.Negative : BoardStyle.Ink;

        float zero = geometry.X(0f);
        float current = geometry.X(entry.Value);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchoredPosition = new Vector2(Mathf.Min(zero, current), -(trackY - TrackHeight * 0.5f));
        fillRect.sizeDelta = new Vector2(Mathf.Abs(current - zero), TrackHeight);
        fill.color = entry.Value >= 0f ? BoardStyle.Positive : BoardStyle.Negative;
        fill.enabled = Mathf.Abs(current - zero) >= 0.5f;
        valueMark.anchoredPosition = new Vector2(current - 1.5f, valueMark.anchoredPosition.y);

        foreach (var (index, image, hover, badge, thumb) in markers)
        {
            BoardMarker marker = entry.Markers[index];
            ApplyMarkerIcon(marker, manager, image, badge, thumb);
            hover.Bind(marker, controller);
        }

        foreach (GroupSlot slot in groups)
        {
            ApplyGroupSlot(slot, entry, manager, controller);
        }
    }

    internal static void ApplyMarkerIcon(BoardMarker marker, ReputationUIManager manager, Image image, Image badge, RectTransform thumb)
    {
        image.sprite = marker.Sprite;
        Color stateTint = marker.State == MarkerState.Active ? Color.white
            : marker.State == MarkerState.Gated ? GatedTint
            : new Color(1f, 1f, 1f, 0.35f);
        image.color = stateTint * (marker.SourceTint ?? Color.white);
        ApplySwitchBadge(marker, manager, badge, thumb);
    }

    private void ApplyGroupSlot(GroupSlot slot, BoardEntry entry, ReputationUIManager manager, BoardController controller)
    {
        bool anyActiveOn = false;
        bool anyActive = false;
        bool anyGated = false;
        BoardMarker firstSwitchMarker = null;
        BoardMarker representative = entry.Markers[slot.Start];
        int bestScore = int.MinValue;

        for (int i = 0; i < slot.Count; i++)
        {
            BoardMarker marker = entry.Markers[slot.Start + i];
            bool off = marker.IsSwitchedOff(manager);
            if (marker.State == MarkerState.Active)
            {
                anyActive = true;
                if (!off)
                {
                    anyActiveOn = true;
                }
            }
            else if (marker.State == MarkerState.Gated)
            {
                anyGated = true;
            }
            if (firstSwitchMarker == null && marker.SourceSwitch != null)
            {
                firstSwitchMarker = marker;
            }

            // 优先展示已生效或被门控的独特图标，避免用通用五角星掩盖特色图标。
            int score = (marker.State == MarkerState.Active && !off ? 30 : marker.State == MarkerState.Gated ? 20 : marker.State == MarkerState.Active ? 10 : 0)
                + (marker.Sprite != null && !BoardData.IsNativeSprite(manager, marker.Sprite) ? 4 : 0)
                + (marker.SourceSwitch != null ? 2 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                representative = marker;
            }
        }

        slot.Icon.sprite = representative.Sprite;
        Color stateTint = anyActiveOn ? Color.white
            : anyGated ? GatedTint
            : anyActive ? Color.white
            : new Color(1f, 1f, 1f, 0.35f);
        slot.Icon.color = stateTint * (representative.SourceTint ?? Color.white);

        if (anyActiveOn || anyActive)
        {
            slot.CountLabel.color = BoardStyle.Ink;
            slot.CountShadow.color = new Color(0.86f, 0.88f, 0.81f, 0.85f);
        }
        else if (anyGated)
        {
            slot.CountLabel.color = new Color(0.50f, 0.27f, 0.04f, 1f);
            slot.CountShadow.color = new Color(0.86f, 0.88f, 0.81f, 0.85f);
        }
        else
        {
            slot.CountLabel.color = new Color(0.17f, 0.2f, 0.18f, 0.50f);
            slot.CountShadow.color = new Color(0.86f, 0.88f, 0.81f, 0.45f);
        }

        BoardMarker switchMarker = representative.SourceSwitch != null ? representative : firstSwitchMarker;
        if (switchMarker != null)
        {
            ApplySwitchBadge(switchMarker, manager, slot.SwitchBadge, slot.SwitchThumb);
        }
        slot.Hover.Bind(this, slot.Start, slot.Count, controller);
    }

    private static void ApplySwitchBadge(BoardMarker marker, ReputationUIManager manager, Image badge, RectTransform thumb)
    {
        if (badge == null)
        {
            return;
        }
        bool off = marker.IsSwitchedOff(manager);
        badge.color = off ? BoardStyle.Ink : SwitchOnColor;
        float x = off ? SwitchInset : badge.rectTransform.sizeDelta.x - thumb.sizeDelta.x - SwitchInset;
        thumb.anchoredPosition = new Vector2(x, -SwitchInset);
    }
}

/// <summary>聚合节点悬停处理：轻度放大并在底部详情框轮播组内各奖励的完整效果。</summary>
internal sealed class GroupHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BoardRow row;
    private int start;
    private int count;
    private BoardController controller;

    public void Bind(BoardRow ownerRow, int startIndex, int itemCount, BoardController owner)
    {
        row = ownerRow;
        start = startIndex;
        count = itemCount;
        controller = owner;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = Vector3.one * 1.25f;
        transform.SetAsLastSibling();
        controller?.PreviewMarkerGroup(row, start, count, eventData, enter: true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = Vector3.one;
        controller?.PreviewMarkerGroup(row, start, count, eventData, enter: false);
    }

    private void OnDisable()
    {
        transform.localScale = Vector3.one;
        controller?.StopGroupCarousel();
    }
}

/// <summary>悬停转发：优先调用原标记上的处理器（原版 PerkElement 或其他模组的组件），否则直接写原版详情框。</summary>
internal sealed class MarkerHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private BoardMarker marker;
    private BoardController controller;
    internal bool Enlarge { get; set; } = true;

    public void Bind(BoardMarker value, BoardController owner)
    {
        marker = value;
        controller = owner;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Enlarge)
        {
            transform.localScale = Vector3.one * 1.3f;
            transform.SetAsLastSibling();
        }
        if (controller != null)
        {
            controller.PreviewMarker(marker, eventData, enter: true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = Vector3.one;
        if (controller != null)
        {
            controller.PreviewMarker(marker, eventData, enter: false);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller != null && eventData.button == PointerEventData.InputButton.Left)
        {
            controller.ClickMarker(marker, eventData);
        }
    }

    private void OnDisable()
    {
        transform.localScale = Vector3.one;
    }

    internal static bool Forward(GameObject source, PointerEventData eventData, bool enter)
    {
        if (source == null)
        {
            return false;
        }
        bool handled = false;
        foreach (MonoBehaviour behaviour in source.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null)
            {
                continue;
            }
            if (enter && behaviour is IPointerEnterHandler enterHandler)
            {
                enterHandler.OnPointerEnter(eventData);
                handled = true;
            }
            else if (!enter && behaviour is IPointerExitHandler exitHandler)
            {
                exitHandler.OnPointerExit(eventData);
                handled = true;
            }
        }
        return handled;
    }
}

/// <summary>以左上角为原点、向下为正的简单 UI 构建工具。</summary>
internal static class Ui
{
    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    public static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(right, top);
    }

    public static RectTransform Place(string name, Transform parent, float x, float y, float width, float height)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    public static Image Box(Transform parent, string name, float x, float y, float width, float height, Color color)
    {
        Image image = Place(name, parent, x, y, width, height).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, BoardStyle style, float size, float x, float y, float width, float height,
        TextAlignmentOptions alignment, Color color)
    {
        TextMeshProUGUI text = Place(name, parent, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = style.Font;
        if (style.FontMaterial != null)
        {
            text.fontSharedMaterial = style.FontMaterial;
        }
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMax = size;
        text.fontSizeMin = Mathf.Max(6f, size * 0.6f);
        text.alignment = alignment;
        text.color = color;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }
}
