using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReputationInterfaceRemake;

/// <summary>在刻度列表内展开一组奖励，不改变势力行高度。</summary>
internal sealed class BoardMarkerGroup
{
    private const float Pad = 4f;
    private const float HeaderHeight = 24f;
    private static readonly Color InactiveMuted = new Color(0.3f, 0.33f, 0.3f, 0.55f);
    private static readonly Color InactiveInk = new Color(0.17f, 0.2f, 0.18f, 0.55f);
    private static readonly Color GatedInk = new Color(0.45f, 0.28f, 0.05f);

    private readonly RectTransform root;
    private readonly BoardRow row;
    private readonly int start;
    private readonly (Image Icon, MarkerHover Hover, Image Badge, RectTransform Thumb, TextMeshProUGUI Threshold, TextMeshProUGUI Name)[] items;

    public BoardMarkerGroup(RectTransform viewport, RectTransform anchor, BoardRow row, int start, int count,
        BoardStyle style, Action close)
    {
        this.row = row;
        this.start = start;
        items = new (Image, MarkerHover, Image, RectTransform, TextMeshProUGUI, TextMeshProUGUI)[count];
        root = Ui.Rect("PerkGroupOverlay", viewport);
        Ui.Stretch(root, 0f, 0f, 0f, 0f);
        Image outside = root.gameObject.AddComponent<Image>();
        outside.color = Color.clear;
        AddButton(outside, close, Selectable.Transition.None);

        float rowHeight = Mathf.Max(26f, style.Icon + 6f);
        Rect bounds = viewport.rect;
        float width = Mathf.Min(310f, bounds.width - Pad * 2f);
        float height = Mathf.Min(HeaderHeight + count * rowHeight + Pad, bounds.height - Pad * 2f);
        Vector3 center = viewport.InverseTransformPoint(anchor.TransformPoint(anchor.rect.center));
        float below = bounds.yMax - center.y + style.Icon * 0.5f + Pad;
        float top = below + height <= bounds.height - Pad ? below : bounds.yMax - center.y - style.Icon * 0.5f - Pad - height;
        top = Mathf.Clamp(top, Pad, bounds.height - height - Pad);
        float left = Mathf.Clamp(center.x - bounds.xMin - width * 0.5f, Pad, bounds.width - width - Pad);
        Image panel = Ui.Box(root, "GroupPanel", left, top, width, height, new Color(0.80f, 0.82f, 0.75f));
        panel.raycastTarget = true;
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = BoardStyle.Muted;
        outline.effectDistance = new Vector2(1f, -1f);

        float fontSize = Mathf.Min(18f, style.Status);
        TextMeshProUGUI title = Ui.Text(panel.rectTransform, "Title", style, fontSize, 8f, 0f, width - 38f, HeaderHeight,
            TextAlignmentOptions.MidlineLeft, BoardStyle.Ink);
        string range = FormatThresholdRange(row.Entry.Markers, start, count);
        title.text = $"{row.Entry.Title}  {range} (×{count})";
        Image closeButton = Ui.Box(panel.rectTransform, "Close", width - HeaderHeight, 0f, HeaderHeight, HeaderHeight, BoardStyle.Muted);
        AddButton(closeButton, close, Selectable.Transition.ColorTint);
        Ui.Text(closeButton.rectTransform, "Label", style, fontSize, 0f, 0f, HeaderHeight, HeaderHeight,
            TextAlignmentOptions.Center, Color.white).text = "×";

        RectTransform clip = Ui.Place("RewardViewport", panel.rectTransform, Pad, HeaderHeight, width - Pad * 2f, height - HeaderHeight - Pad);
        clip.gameObject.AddComponent<RectMask2D>();
        RectTransform content = Ui.Rect("Rewards", clip);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, rowHeight * count);
        ScrollRect scroll = panel.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = clip;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = rowHeight;
        float itemWidth = width - Pad * 2f;
        string fallbackName = ModHelper.GetLocalized(ReputationInterfaceRemakeMod.ModId, "group_reward");
        for (int i = 0; i < count; i++)
        {
            BoardMarker marker = row.Entry.Markers[start + i];
            Image item = Ui.Box(content, "Reward", 0f, i * rowHeight, itemWidth, rowHeight, i % 2 == 0
                ? new Color(0f, 0f, 0f, 0.09f) : new Color(1f, 1f, 1f, 0.08f));
            item.raycastTarget = true;
            Image icon = Ui.Box(item.rectTransform, "Icon", Pad, 3f, style.Icon, style.Icon, Color.white);
            icon.preserveAspect = true;
            (Image badge, RectTransform thumb) = marker.SourceSwitch != null ? BoardRow.BuildSwitchBadge(icon.rectTransform, style.Icon) : default;
            MarkerHover hover = item.gameObject.AddComponent<MarkerHover>();
            hover.Enlarge = false;
            float textLeft = style.Icon + Pad * 2f;
            TextMeshProUGUI thresholdText = Ui.Text(item.rectTransform, "Threshold", style, fontSize, textLeft, 0f, 54f, rowHeight,
                TextAlignmentOptions.MidlineLeft, BoardStyle.Muted);
            thresholdText.text = marker.Threshold.ToString("+0;-0;0");
            TextMeshProUGUI nameText = Ui.Text(item.rectTransform, "Name", style, fontSize, textLeft + 54f, 0f, itemWidth - textLeft - 58f, rowHeight,
                TextAlignmentOptions.MidlineLeft, BoardStyle.Ink);
            nameText.text = marker.Perk?.GetLocalizedPerkName() ?? fallbackName;
            items[i] = (icon, hover, badge, thumb, thresholdText, nameText);
        }
    }

    internal static string FormatThresholdRange(IList<BoardMarker> markers, int start, int count)
    {
        int min = markers[start].Threshold;
        int max = min;
        for (int i = 1; i < count; i++)
        {
            int t = markers[start + i].Threshold;
            if (t < min) min = t;
            if (t > max) max = t;
        }
        return min == max ? min.ToString("+0;-0;0") : $"{min:+0;-0;0} ~ {max:+0;-0;0}";
    }

    public void Refresh(ReputationUIManager manager, BoardController controller)
    {
        for (int i = 0; i < items.Length; i++)
        {
            BoardMarker marker = row.Entry.Markers[start + i];
            var (icon, hover, badge, thumb, thresholdText, nameText) = items[i];
            BoardRow.ApplyMarkerIcon(marker, manager, icon, badge, thumb);
            thresholdText.color = marker.State == MarkerState.Inactive ? InactiveMuted : BoardStyle.Muted;
            nameText.color = marker.State == MarkerState.Active ? BoardStyle.Ink
                : marker.State == MarkerState.Gated ? GatedInk
                : InactiveInk;
            hover.Bind(marker, controller);
        }
    }

    public void Close()
    {
        if (root != null)
        {
            root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }

    private static void AddButton(Image image, Action click, Selectable.Transition transition)
    {
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = transition;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => click());
    }
}
