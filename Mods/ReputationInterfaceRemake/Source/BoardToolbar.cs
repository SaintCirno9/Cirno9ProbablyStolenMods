using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ReputationInterfaceRemake;

/// <summary>统一绘制第三方顶部控件，只转发点击，不修改原按钮的层级或样式。</summary>
internal sealed class BoardToolbar
{
    private static readonly Color ButtonColor = new Color(0.71f, 0.74f, 0.67f);
    private static readonly Color SelectedColor = new Color(0.79f, 0.85f, 0.68f);
    private const float Gap = 4f;
    private const float Height = 26f;
    // 窗口顶边保留原生操作区，不在关闭等原生按钮的同一排绘制。
    private const float WindowControlsInset = 36f;

    private readonly RectTransform root;
    private readonly Control factions;
    private readonly Control people;
    private readonly Control journal;
    private GameObject originalTabs;
    private GameObject originalJournal;
    private RectTransform peoplePage;
    private float originalPeopleTop;
    private float lastWidth;

    public BoardToolbar(Transform panel, BoardStyle style)
    {
        root = Ui.Rect("ReputationInterfaceRemake_Toolbar", panel);
        root.anchorMin = new Vector2(0.04f, 1f);
        root.anchorMax = new Vector2(0.96f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.anchoredPosition = new Vector2(0f, -WindowControlsInset);
        root.sizeDelta = new Vector2(0f, Height);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        root.gameObject.SetActive(false);
        factions = new Control(root, "Factions", style, right: false);
        people = new Control(root, "People", style, right: false);
        journal = new Control(root, "Journal", style, right: true);
    }

    public bool SourcesLost => (!ReferenceEquals(originalTabs, null) && originalTabs == null)
        || (!ReferenceEquals(originalJournal, null) && originalJournal == null);

    public void Bind()
    {
        Restore();
        Transform panel = root.parent;
        // 重新打开时旧页签可能还在等待销毁，取本次 OpenUI 最后创建的直接子对象。
        for (int i = panel.childCount - 1; i >= 0; i--)
        {
            Transform child = panel.GetChild(i);
            if (child.name == "HENCH_PARTNER_TABS" && child.gameObject.activeSelf)
            {
                Button[] buttons = child.GetComponentsInChildren<Button>();
                if (buttons.Length == 2 && factions.Bind(buttons[0]) && people.Bind(buttons[1]))
                {
                    originalTabs = child.gameObject;
                    originalTabs.SetActive(false);
                }
                break;
            }
        }
        Transform original = panel.Find("FRE_ReputationJournalShortcut");
        if (original != null && original.gameObject.activeSelf && journal.Bind(original.GetComponent<Button>()))
        {
            originalJournal = original.gameObject;
            originalJournal.SetActive(false);
        }
        bool hasTabs = originalTabs != null;
        if (hasTabs)
        {
            // 人物页同样会在重开窗口时替换，避免对待销毁的旧对象预留空间。
            for (int i = panel.childCount - 1; i >= 0; i--)
            {
                if (panel.GetChild(i) is RectTransform child && child.name == "HENCH_PARTNER_UI")
                {
                    peoplePage = child;
                    originalPeopleTop = child.offsetMax.y;
                    break;
                }
            }
        }
        factions.SetVisible(hasTabs);
        people.SetVisible(hasTabs);
        journal.SetVisible(originalJournal != null);
        root.SetAsLastSibling();
        root.gameObject.SetActive(hasTabs || originalJournal != null);
        lastWidth = -1f;
    }

    public float Update(bool peopleOpen, RectTransform view)
    {
        if (!root.gameObject.activeSelf)
        {
            return 0f;
        }
        bool hasTabs = originalTabs != null;
        factions.UpdateState(hasTabs && !peopleOpen);
        people.UpdateState(hasTabs && peopleOpen);
        journal.UpdateState(selected: false);
        float width = root.rect.width;
        if (width > 0f && !Mathf.Approximately(width, lastWidth))
        {
            lastWidth = width;
            float tabWidth = Mathf.Min(96f, Mathf.Max(0f, (width * (originalJournal != null ? 0.58f : 1f) - Gap) * 0.5f));
            factions.Place(0f, tabWidth);
            people.Place(tabWidth + Gap, tabWidth);
            journal.Place(0f, Mathf.Min(156f, hasTabs ? width * 0.39f : width));
        }
        PlacePeoplePage();
        // 工具栏独立于势力列表；只有覆盖到刻度区域时才让列表下移。
        float bottom = view.InverseTransformPoint(root.TransformPoint(new Vector3(0f, root.rect.yMin))).y;
        return Mathf.Max(0f, view.rect.yMax - bottom + Gap);
    }

    public void Restore()
    {
        if (root != null)
        {
            root.gameObject.SetActive(false);
        }
        if (originalTabs != null)
        {
            originalTabs.SetActive(true);
        }
        if (originalJournal != null)
        {
            originalJournal.SetActive(true);
        }
        if (peoplePage != null)
        {
            SetPeopleTop(originalPeopleTop);
        }
        peoplePage = null;
        originalTabs = originalJournal = null;
        factions.Bind(null);
        people.Bind(null);
        journal.Bind(null);
    }

    private void PlacePeoplePage()
    {
        if (peoplePage == null)
        {
            return;
        }
        // 人物页与工具栏同属 uiPanel。仅收缩页顶，保留页底和左右位置。
        Rect panel = ((RectTransform)root.parent).rect;
        float anchorTop = panel.yMin + panel.height * peoplePage.anchorMax.y;
        float toolbarBottom = panel.yMax - WindowControlsInset - Height;
        SetPeopleTop(Mathf.Min(originalPeopleTop, toolbarBottom - Gap - anchorTop));
    }

    private void SetPeopleTop(float top)
    {
        Vector2 offset = peoplePage.offsetMax;
        if (!Mathf.Approximately(offset.y, top))
        {
            peoplePage.offsetMax = new Vector2(offset.x, top);
        }
    }

    public void Destroy()
    {
        Restore();
        if (root != null)
        {
            UnityEngine.Object.Destroy(root.gameObject);
        }
    }

    private sealed class Control
    {
        private readonly Image background;
        private readonly Image accent;
        private readonly TextMeshProUGUI label;
        private readonly Button button;
        private Button source;

        public Control(RectTransform parent, string name, BoardStyle style, bool right)
        {
            background = Ui.Box(parent, name, 0f, 0f, 0f, Height, ButtonColor);
            RectTransform rect = background.rectTransform;
            float x = right ? 1f : 0f;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(x, 1f);
            background.raycastTarget = true;
            button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(InvokeSource);
            label = Ui.Text(rect, "Label", style, Mathf.Clamp(style.Status, 12f, 18f), 0f, 0f, 0f, Height,
                TextAlignmentOptions.Center, BoardStyle.Ink);
            Ui.Stretch(label.rectTransform, 6f, 2f, -6f, -2f);
            accent = Ui.Box(rect, "Selected", 0f, 0f, 0f, 2f, BoardStyle.Positive);
            accent.rectTransform.anchorMin = Vector2.zero;
            accent.rectTransform.anchorMax = Vector2.right;
            accent.rectTransform.pivot = Vector2.zero;
            accent.rectTransform.anchoredPosition = Vector2.zero;
        }

        public bool Bind(Button original)
        {
            source = null;
            var caption = original != null ? original.GetComponentInChildren<TextMeshProUGUI>() : null;
            if (caption == null || string.IsNullOrWhiteSpace(caption.text))
            {
                return false;
            }
            source = original;
            label.text = caption.text;
            return true;
        }

        public void SetVisible(bool visible) => background.gameObject.SetActive(visible);

        public void Place(float x, float width)
        {
            background.rectTransform.anchoredPosition = new Vector2(x, 0f);
            background.rectTransform.sizeDelta = new Vector2(width, Height);
        }

        public void UpdateState(bool selected)
        {
            background.color = selected ? SelectedColor : ButtonColor;
            if (accent.gameObject.activeSelf != selected)
            {
                accent.gameObject.SetActive(selected);
            }
            button.interactable = CanInvoke;
        }

        // 原按钮或它的父对象由工具栏隐藏，仍保留原来的点击回调。
        private bool CanInvoke => source != null && source.enabled && source.interactable;

        private void InvokeSource()
        {
            if (!CanInvoke)
            {
                return;
            }
            try
            {
                source.onClick.Invoke();
            }
            catch (Exception ex)
            {
                ReputationInterfaceRemakeMod.Log.Error("执行顶部按钮失败：" + ex);
            }
        }
    }
}
