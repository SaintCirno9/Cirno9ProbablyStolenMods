using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReputationInterfaceRemake;

/// <summary>
/// 挂在原版 uiPanel 上：隐藏原版纵向 layout（保留对象，供原版与其他模组继续写入），
/// 在同一区域绘制横向行式视图，并定时从原版行同步数据。
/// </summary>
internal sealed class BoardController : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private const float CarouselInterval = 2.5f;
    private const float HeaderHeight = 18f;
    private const float ScrollbarWidth = 8f;

    // 势力声望扩展在 uiPanel 上创建的分页按钮；新版视图可滚动，不再需要分页。
    private static readonly string[] ForeignPagerNames = { "FRE_AUG_PAGE_NEXT", "FRE_AUG_PAGE_PREV" };
    private const string HenchmanPeoplePageName = "HENCH_PARTNER_UI";

    // 上层区（3838944803）在 OpenUI 时会用该滚动视口包裹原版 layout 并拉宽其 sizeDelta。
    private const string UpperLevelsViewportName = "UL_RepViewport";

    private struct LayoutSnapshot
    {
        public RectTransform Target;
        public Transform Parent;
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;
        public Vector3 LocalScale;
        public Quaternion LocalRotation;
    }

    private static readonly List<BoardController> All = new List<BoardController>();

    private static bool _layoutLogged;
    private static LayoutSnapshot _snapshot;

    private ReputationUIManager manager;
    private RectTransform view;
    private RectTransform header;
    private RectTransform scrollRoot;
    private RectTransform viewport;
    private RectTransform content;
    private GameObject hiddenTarget;
    private CanvasGroup layoutGroup;
    private bool addedLayoutGroup;
    private float savedAlpha;
    private bool savedBlocksRaycasts;
    private bool savedInteractable;
    private bool active;
    private bool dirty = true;
    private float nextRefresh;
    private string signature;
    private BoardStyle style;
    private BoardMarker pinnedMarker;
    private BoardMarkerGroup markerGroup;
    private BoardRow carouselRow;
    private int carouselStart;
    private int carouselCount;
    private int carouselIndex;
    private float nextCarouselStep;
    private BoardMarker carouselShownMarker;
    private PointerEventData carouselEventData;
    private BoardToolbar toolbar;
    private bool toolbarDirty = true;
    private readonly List<GameObject> hiddenPager = new List<GameObject>();
    private readonly List<BoardRow> rows = new List<BoardRow>();
    private readonly HashSet<string> reported = new HashSet<string>();

    internal static void CaptureOriginalLayout(ReputationUIManager m)
    {
        if (m == null || m.layout == null)
        {
            return;
        }
        var layoutRect = m.layout.transform as RectTransform;
        if (layoutRect == null || IsWrappedViewport(layoutRect.parent))
        {
            return;
        }
        _snapshot = new LayoutSnapshot
        {
            Target = layoutRect,
            Parent = layoutRect.parent,
            AnchorMin = layoutRect.anchorMin,
            AnchorMax = layoutRect.anchorMax,
            Pivot = layoutRect.pivot,
            AnchoredPosition = layoutRect.anchoredPosition,
            SizeDelta = layoutRect.sizeDelta,
            LocalScale = layoutRect.localScale,
            LocalRotation = layoutRect.localRotation
        };
    }

    private static bool IsWrappedViewport(Transform parent)
    {
        return parent != null && parent.name == UpperLevelsViewportName;
    }

    internal static void OnOpened(ReputationUIManager m)
    {
        if (m == null || m.uiPanel == null || m.layout == null)
        {
            return;
        }
        BoardController controller = m.uiPanel.GetComponent<BoardController>();
        if (controller == null)
        {
            controller = m.uiPanel.AddComponent<BoardController>();
        }
        controller.ClearPinnedMarker();
        controller.StopGroupCarousel(clearText: false);
        controller.CloseMarkerGroup();
        controller.manager = m;
        controller.dirty = true;
        controller.toolbarDirty = true;
    }

    internal static void RequestRefreshAll()
    {
        foreach (BoardController controller in All)
        {
            if (controller != null)
            {
                controller.dirty = true;
            }
        }
    }

    internal static void DestroyAll()
    {
        foreach (BoardController controller in All.ToArray())
        {
            if (controller != null)
            {
                controller.Restore();
                controller.ReleaseHideTarget();
                Destroy(controller);
            }
        }
        All.Clear();
    }

    private void Awake()
    {
        All.Add(this);
    }

    private void OnDestroy()
    {
        All.Remove(this);
        Restore();
        toolbar?.Destroy();
        if (view != null)
        {
            Destroy(view.gameObject);
        }
    }

    private void OnDisable()
    {
        ClearPinnedMarker();
        StopGroupCarousel(clearText: false);
        CloseMarkerGroup();
        toolbar?.Restore();
        toolbarDirty = true;
    }

    private void LateUpdate()
    {
        if (manager == null || manager.layout == null)
        {
            return;
        }
        try
        {
            if (!ReputationInterfaceRemakeMod.UseNewBoard)
            {
                Restore();
                return;
            }
            if (!active || ResolveHideTarget() != hiddenTarget)
            {
                Activate();
            }
            HideForeignPager();
            // 人物页只停用原版 layout；新版列表在 layout 之外，需要自己同步显隐。
            bool showFactions = !IsHenchmanPeoplePageOpen();
            UpdateToolbar(peopleOpen: !showFactions);
            if (!showFactions)
            {
                ClearPinnedMarker();
                StopGroupCarousel(clearText: false);
                CloseMarkerGroup();
            }
            if (view.gameObject.activeSelf != showFactions)
            {
                view.gameObject.SetActive(showFactions);
                dirty = true;
            }
            if (!showFactions)
            {
                return;
            }
            UpdateGroupCarousel();
            if (!dirty && Time.unscaledTime < nextRefresh)
            {
                return;
            }
            dirty = false;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }
        catch (Exception ex)
        {
            Report("刷新新版势力档案失败：" + ex);
        }
    }

    private GameObject ResolveHideTarget()
    {
        Transform layoutTransform = manager.layout.transform;
        return IsWrappedViewport(layoutTransform.parent) ? layoutTransform.parent.gameObject : manager.layout;
    }

    private bool IsHenchmanPeoplePageOpen()
    {
        if (manager.layout.activeSelf)
        {
            return false;
        }
        Transform people = manager.uiPanel.transform.Find(HenchmanPeoplePageName);
        return people != null && people.gameObject.activeSelf;
    }

    private void UpdateToolbar(bool peopleOpen)
    {
        if (toolbarDirty || toolbar?.SourcesLost == true)
        {
            toolbar ??= new BoardToolbar(manager.uiPanel.transform, BoardStyle.From(manager, null));
            toolbar.Bind();
            toolbarDirty = false;
        }
        float clearance = toolbar.Update(peopleOpen, view);
        if (!Mathf.Approximately(header.anchoredPosition.y, -clearance))
        {
            header.anchoredPosition = new Vector2(0f, -clearance);
            scrollRoot.offsetMax = new Vector2(0f, -HeaderHeight - clearance);
        }
    }

    internal void PreviewMarker(BoardMarker marker, PointerEventData eventData, bool enter)
    {
        if (pinnedMarker == null && CanShowMarker(marker))
        {
            if (enter)
            {
                StopGroupCarousel(clearText: false);
            }
            ShowMarker(marker, eventData, enter);
        }
    }

    internal void PreviewMarkerGroup(BoardRow row, int start, int count, PointerEventData eventData, bool enter)
    {
        if (pinnedMarker != null || !CanShowBoard())
        {
            return;
        }
        if (!enter)
        {
            if (carouselRow == row && carouselStart == start)
            {
                StopGroupCarousel(clearText: true);
            }
            return;
        }
        if (row == null || row.Entry == null || start < 0 || count <= 0 || start + count > row.Entry.Markers.Count)
        {
            return;
        }
        StopGroupCarousel(clearText: false);
        carouselRow = row;
        carouselStart = start;
        carouselCount = count;
        carouselIndex = 0;
        nextCarouselStep = Time.unscaledTime + CarouselInterval;
        carouselEventData = eventData;
        ShowCarouselItem();
    }

    internal void StopGroupCarousel(bool clearText = false)
    {
        if (carouselShownMarker != null)
        {
            MarkerHover.Forward(carouselShownMarker.Source, ResolveCarouselEventData(), enter: false);
            carouselShownMarker = null;
        }
        bool wasActive = carouselRow != null;
        carouselRow = null;
        carouselStart = 0;
        carouselCount = 0;
        carouselIndex = 0;
        if (clearText && wasActive && pinnedMarker == null)
        {
            ClearPreviewText();
        }
    }

    private void UpdateGroupCarousel()
    {
        if (carouselRow == null || pinnedMarker != null || markerGroup != null || !CanShowBoard())
        {
            return;
        }
        if (Time.unscaledTime < nextCarouselStep)
        {
            return;
        }
        nextCarouselStep = Time.unscaledTime + CarouselInterval;
        if (carouselCount > 1)
        {
            carouselIndex = (carouselIndex + 1) % carouselCount;
            ShowCarouselItem();
        }
    }

    private void ShowCarouselItem()
    {
        BoardEntry entry = carouselRow?.Entry;
        if (entry == null || carouselStart < 0 || carouselStart + carouselCount > entry.Markers.Count)
        {
            StopGroupCarousel(clearText: false);
            return;
        }
        int index = Mathf.Clamp(carouselIndex, 0, carouselCount - 1);
        BoardMarker nextMarker = entry.Markers[carouselStart + index];
        PointerEventData eventData = ResolveCarouselEventData();
        if (carouselShownMarker != null && !ReferenceEquals(carouselShownMarker, nextMarker))
        {
            MarkerHover.Forward(carouselShownMarker.Source, eventData, enter: false);
        }
        carouselShownMarker = nextMarker;
        ClearPreviewText();
        ShowMarker(nextMarker, eventData, enter: true);

        if (manager.perkTitle != null)
        {
            string title = manager.perkTitle.text ?? "";
            if (nextMarker.State == MarkerState.Gated)
            {
                string gatedLabel = ModHelper.GetLocalized(ReputationInterfaceRemakeMod.ModId, "group_state_gated");
                string inactiveLabel = LocHelper.GetLocalizedUI("ui_rep_inactive");
                if (!string.IsNullOrEmpty(inactiveLabel) && title.Contains(inactiveLabel))
                {
                    title = title.Replace(inactiveLabel, $"[{gatedLabel}]");
                }
                else if (!title.Contains(gatedLabel))
                {
                    title = $"{title} [{gatedLabel}]";
                }
            }
            manager.perkTitle.text = $"[{index + 1}/{carouselCount}] {title}";
        }
        if (manager.perkFlavorText != null)
        {
            string hint = ModHelper.GetLocalized(ReputationInterfaceRemakeMod.ModId, "group_carousel_hint");
            string flavor = manager.perkFlavorText.text ?? "";
            manager.perkFlavorText.text = string.IsNullOrEmpty(flavor) ? hint : $"{flavor}  [{hint}]";
        }
    }

    private PointerEventData ResolveCarouselEventData()
    {
        if (carouselEventData != null)
        {
            return carouselEventData;
        }
        EventSystem current = EventSystem.current;
        return current != null ? new PointerEventData(current) : null;
    }

    private void ClearPreviewText()
    {
        if (manager == null)
        {
            return;
        }
        if (manager.perkTitle != null) manager.perkTitle.text = "";
        if (manager.perkdescription != null) manager.perkdescription.text = "";
        if (manager.perkFlavorText != null) manager.perkFlavorText.text = "";
    }

    internal void OpenMarkerGroup(BoardRow row, RectTransform anchor, int start, int count)
    {
        if (!CanShowBoard() || viewport.rect.width < 100f || viewport.rect.height < 68f)
        {
            return;
        }
        StopGroupCarousel(clearText: false);
        CloseMarkerGroup();
        ClearPinnedMarker();
        markerGroup = new BoardMarkerGroup(viewport, anchor, row, start, count, style, CloseMarkerGroup);
        markerGroup.Refresh(manager, this);
    }

    private void CloseMarkerGroup()
    {
        markerGroup?.Close();
        markerGroup = null;
    }

    internal void ClickMarker(BoardMarker marker, PointerEventData eventData)
    {
        if (!CanShowMarker(marker))
        {
            return;
        }
        StopGroupCarousel(clearText: false);
        if (marker.SourceSwitch != null)
        {
            ClearPinnedMarker();
            ToggleMarker(marker.SourceSwitch);
        }
        else if (MatchesPinnedMarker(marker))
        {
            ClearPinnedMarker();
        }
        else
        {
            pinnedMarker = marker;
        }
        ShowMarker(marker, eventData, enter: true);
    }

    private bool CanShowMarker(BoardMarker marker)
    {
        return marker != null && CanShowBoard();
    }

    private bool CanShowBoard()
    {
        return manager != null && active && ReputationInterfaceRemakeMod.UseNewBoard
            && view != null && view.gameObject.activeInHierarchy
            && !IsHenchmanPeoplePageOpen();
    }

    private bool MatchesPinnedMarker(BoardMarker marker)
    {
        if (pinnedMarker == null)
        {
            return false;
        }
        // 定时刷新会重建数据快照，以原标记为准；调试预览行没有原标记时用特性对象。
        return !ReferenceEquals(pinnedMarker.Source, null)
            ? pinnedMarker.Source != null && pinnedMarker.Source == marker.Source
            : pinnedMarker.Perk != null && ReferenceEquals(pinnedMarker.Perk, marker.Perk);
    }

    private void ShowMarker(BoardMarker marker, PointerEventData eventData, bool enter)
    {
        try
        {
            if (!MarkerHover.Forward(marker.Source, eventData, enter) && enter && marker.Perk != null)
            {
                manager.UpdateToolTipContent(marker.Perk);
            }
            if (enter && marker.Perk != null && manager.perkTitle != null && marker.IsSwitchedOff(manager))
            {
                string state = ModHelper.GetLocalized(ReputationInterfaceRemakeMod.ModId, "detail_perk_off");
                manager.perkTitle.text = $"{marker.Perk.GetLocalizedPerkName()} [{state}] {marker.Threshold}/200";
            }
        }
        catch (Exception ex)
        {
            Report("显示或转发特性详情失败：" + ex);
        }
    }

    private void ClearPinnedMarker() => pinnedMarker = null;

    private void ToggleMarker(Button source)
    {
        // 原版容器不接收点击，有的原版行还会被分页器隐藏；只检查开关自身是否可用。
        if (source == null || !source.gameObject.activeSelf || !source.enabled || !source.interactable)
        {
            return;
        }
        try
        {
            dirty = true;
            source.onClick.Invoke();
        }
        catch (Exception ex)
        {
            Report("切换扩展声望奖励失败：" + ex);
        }
    }

    private void ReleaseHideTarget()
    {
        if (addedLayoutGroup && layoutGroup != null)
        {
            Destroy(layoutGroup);
        }
        layoutGroup = null;
        addedLayoutGroup = false;
        hiddenTarget = null;
    }

    private void Activate()
    {
        GameObject target = ResolveHideTarget();
        if (hiddenTarget != null && hiddenTarget != target)
        {
            RestoreHideGroup();
            ReleaseHideTarget();
        }
        if (layoutGroup == null)
        {
            hiddenTarget = target;
            layoutGroup = target.GetComponent<CanvasGroup>();
            addedLayoutGroup = layoutGroup == null;
            if (addedLayoutGroup)
            {
                layoutGroup = target.AddComponent<CanvasGroup>();
            }
            savedAlpha = layoutGroup.alpha;
            savedBlocksRaycasts = layoutGroup.blocksRaycasts;
            savedInteractable = layoutGroup.interactable;
        }
        // 只做透明化而不停用：其他模组的标记组件需要保持激活才能响应转发的悬停事件。
        layoutGroup.alpha = 0f;
        layoutGroup.blocksRaycasts = false;
        layoutGroup.interactable = false;
        if (view == null)
        {
            BuildView();
        }
        active = true;
        signature = null;
        dirty = true;
    }

    private void RestoreHideGroup()
    {
        if (layoutGroup == null)
        {
            return;
        }
        layoutGroup.alpha = addedLayoutGroup ? 1f : savedAlpha;
        layoutGroup.blocksRaycasts = addedLayoutGroup || savedBlocksRaycasts;
        layoutGroup.interactable = addedLayoutGroup || savedInteractable;
    }

    private void Restore()
    {
        if (!active)
        {
            return;
        }
        active = false;
        ClearPinnedMarker();
        StopGroupCarousel(clearText: false);
        CloseMarkerGroup();
        toolbar?.Restore();
        toolbarDirty = true;
        RestoreHideGroup();
        foreach (GameObject pager in hiddenPager)
        {
            if (pager != null)
            {
                pager.SetActive(true);
            }
        }
        hiddenPager.Clear();
        if (view != null)
        {
            view.gameObject.SetActive(false);
        }
    }

    private void HideForeignPager()
    {
        Transform[] parents = { manager.uiPanel != null ? manager.uiPanel.transform : null, manager.layout.transform.parent };
        foreach (string name in ForeignPagerNames)
        {
            foreach (Transform parent in parents)
            {
                Transform pager = parent != null ? parent.Find(name) : null;
                if (pager != null && pager.gameObject.activeSelf)
                {
                    pager.gameObject.SetActive(false);
                    if (!hiddenPager.Contains(pager.gameObject))
                    {
                        hiddenPager.Add(pager.gameObject);
                    }
                }
            }
        }
    }

    private void BuildView()
    {
        var layoutRect = (RectTransform)manager.layout.transform;
        bool wrapped = IsWrappedViewport(layoutRect.parent);
        Transform targetParent = layoutRect.parent;
        int siblingIndex = layoutRect.GetSiblingIndex() + 1;
        RectTransform sourceRect = layoutRect;
        bool useSnapshot = false;

        if (wrapped)
        {
            Transform outer = layoutRect.parent;
            targetParent = _snapshot.Target == layoutRect && _snapshot.Parent != null ? _snapshot.Parent : outer.parent;
            siblingIndex = outer.GetSiblingIndex() + 1;
            if (_snapshot.Target == layoutRect)
            {
                useSnapshot = true;
            }
            else if (outer is RectTransform outerRect)
            {
                sourceRect = outerRect;
            }
        }

        view = Ui.Rect("ReputationInterfaceRemake_View", targetParent);
        if (useSnapshot)
        {
            view.anchorMin = _snapshot.AnchorMin;
            view.anchorMax = _snapshot.AnchorMax;
            view.pivot = _snapshot.Pivot;
            view.anchoredPosition = _snapshot.AnchoredPosition;
            view.sizeDelta = _snapshot.SizeDelta;
            view.localScale = _snapshot.LocalScale;
            view.localRotation = _snapshot.LocalRotation;
        }
        else
        {
            view.anchorMin = sourceRect.anchorMin;
            view.anchorMax = sourceRect.anchorMax;
            view.pivot = sourceRect.pivot;
            view.anchoredPosition = sourceRect.anchoredPosition;
            view.sizeDelta = sourceRect.sizeDelta;
            view.localScale = sourceRect.localScale;
            view.localRotation = sourceRect.localRotation;
        }
        view.SetSiblingIndex(siblingIndex);
        view.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        header = Ui.Rect("Header", view);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0f, 1f);
        header.anchoredPosition = Vector2.zero;
        header.sizeDelta = new Vector2(-(ScrollbarWidth + 2f), HeaderHeight);

        scrollRoot = Ui.Rect("Scroll", view);
        Ui.Stretch(scrollRoot, 0f, 0f, 0f, -HeaderHeight);
        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();

        viewport = Ui.Rect("Viewport", scrollRoot);
        Ui.Stretch(viewport, 0f, 0f, -(ScrollbarWidth + 2f), 0f);
        // 透明但可接收射线，滚轮在行间空白处也能滚动。
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();

        content = Ui.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup vertical = content.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.childControlHeight = true;
        vertical.childControlWidth = true;
        vertical.childForceExpandHeight = false;
        vertical.childForceExpandWidth = true;
        vertical.spacing = 2f;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RectTransform bar = Ui.Rect("Scrollbar", scrollRoot);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = new Vector2(1f, 1f);
        bar.pivot = new Vector2(1f, 1f);
        bar.anchoredPosition = Vector2.zero;
        bar.sizeDelta = new Vector2(ScrollbarWidth, 0f);
        bar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.12f);
        RectTransform sliding = Ui.Rect("Sliding Area", bar);
        Ui.Stretch(sliding, 0f, 0f, 0f, 0f);
        RectTransform handle = Ui.Rect("Handle", sliding);
        Ui.Stretch(handle, 0f, 0f, 0f, 0f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.25f, 0.28f, 0.25f, 0.75f);
        Scrollbar scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        if (!_layoutLogged)
        {
            _layoutLogged = true;
            var components = new StringBuilder();
            foreach (Component component in manager.layout.GetComponents<Component>())
            {
                components.Append(component != null ? component.GetType().Name : "null").Append(' ');
            }
            string wrapNote = wrapped ? " 包裹容器=" + layoutRect.parent.name : "";
            ReputationInterfaceRemakeMod.Log.Log("原版 layout：父级=" + targetParent.name + wrapNote + " 尺寸=" + view.rect.size
                + " 锚点=" + view.anchorMin + "-" + view.anchorMax + " 组件=" + components);
        }
    }

    private void Refresh()
    {
        List<BoardEntry> entries = BoardData.Collect(manager);
        float width = viewport.rect.width;
        if (width <= 1f)
        {
            return;
        }
        string next = Signature(entries, width);
        if (next != signature)
        {
            Rebuild(entries, width);
            signature = next;
        }
        BoardMarker refreshedPin = null;
        for (int i = 0; i < rows.Count && i < entries.Count; i++)
        {
            rows[i].Apply(entries[i], manager, this);
            if (pinnedMarker != null && refreshedPin == null)
            {
                refreshedPin = entries[i].Markers.Find(MatchesPinnedMarker);
            }
        }
        pinnedMarker = refreshedPin;
        markerGroup?.Refresh(manager, this);
    }

    private static string Signature(List<BoardEntry> entries, float width)
    {
        var builder = new StringBuilder();
        builder.Append(Mathf.RoundToInt(width));
        foreach (BoardEntry entry in entries)
        {
            builder.Append('|');
            if (entry.Element != null)
            {
                builder.Append(entry.Element.GetInstanceID());
            }
            else
            {
                builder.Append(entry.Rep != null ? entry.Rep.factionId : entry.Title ?? "");
            }
            builder.Append(':');
            foreach (BoardMarker marker in entry.Markers)
            {
                int identity = marker.Perk != null ? RuntimeHelpers.GetHashCode(marker.Perk) : marker.Source != null ? marker.Source.GetInstanceID() : 0;
                builder.Append(marker.Threshold).Append('/').Append(identity).Append(marker.SourceSwitch != null ? '+' : '-').Append(',');
            }
        }
        return builder.ToString();
    }

    private void Rebuild(List<BoardEntry> entries, float width)
    {
        StopGroupCarousel(clearText: false);
        CloseMarkerGroup();
        // 先停用再销毁，避免 VerticalLayoutGroup 在销毁生效前的这一帧仍计入旧行。
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            GameObject old = content.GetChild(i).gameObject;
            old.SetActive(false);
            Destroy(old);
        }
        for (int i = header.childCount - 1; i >= 0; i--)
        {
            Destroy(header.GetChild(i).gameObject);
        }
        rows.Clear();
        if (entries.Count == 0)
        {
            return;
        }
        ReputationElement sample = entries.Find(e => e.Element != null)?.Element;
        style = BoardStyle.From(manager, sample);
        var geometry = new BoardGeometry(width, style.Icon + 4f);
        bool hasSwitches = entries.Exists(entry => entry.Markers.Exists(marker => marker.SourceSwitch != null));
        for (int i = 0; i < entries.Count; i++)
        {
            rows.Add(BoardRow.Build(content, entries[i], geometry, style, i));
        }
        BuildHeader(geometry, rows.Exists(row => row.HasGroups), hasSwitches);
    }

    private void BuildHeader(BoardGeometry geometry, bool hasGroups, bool hasSwitches)
    {
        TextMeshProUGUI hint = Ui.Text(header, "PinHint", style, style.Status * 0.85f, geometry.NameX, 0f,
            geometry.BarX - geometry.NameX - 36f, HeaderHeight, TextAlignmentOptions.MidlineLeft, BoardStyle.Muted);
        hint.text = ModHelper.GetLocalized(ReputationInterfaceRemakeMod.ModId, hasGroups ? "group_expand_hint"
            : hasSwitches ? "detail_switch_hint" : "detail_pin_hint");
        for (int value = -200; value <= 200; value += 100)
        {
            string label = value > 0 ? "+" + value : value.ToString();
            TextMeshProUGUI text = Ui.Text(header, "Axis" + value, style, style.Status * 0.85f, geometry.X(value) - 30f, 0f, 60f, HeaderHeight,
                TextAlignmentOptions.Center, BoardStyle.Muted);
            text.text = label;
        }
    }

    private void Report(string message)
    {
        if (reported.Add(message))
        {
            ReputationInterfaceRemakeMod.Log.Error(message);
        }
    }
}
