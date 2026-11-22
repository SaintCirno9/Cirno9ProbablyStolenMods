using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SliderNumericInput;

/// <summary>
/// 挂载在滑条数值标签上的原地编辑控制器。
/// 平时保持数值文本正常显示（带微弱悬停提示），点击后在原位置激活 TMP_InputField 进入输入模式。
/// </summary>
internal sealed class InlineSliderNumberEditor : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private static readonly Color HoverBgColor = new Color(0.54f, 0.75f, 0.73f, 0.18f);
    private static readonly Color HoverTextColor = Color.white;
    private static readonly Color InputBorderColor = new Color32(138, 191, 187, 235);
    private static readonly Color InputFillColor = new Color32(22, 18, 28, 245);
    private static readonly Color InputSelectionColor = new Color32(138, 191, 187, 100);
    private static readonly Color InputCaretColor = new Color32(235, 245, 244, 255);
    private const float DisabledAlpha = 0.45f;
    private const float MinInputWidth = 68f;
    private const float MinInputHeight = 30f;

    private Slider slider;
    private TextMeshProUGUI valueText;
    private SliderValueAdapter adapter;
    private bool ownsLabelUpdates;
    private Color baseTextColor;

    private Image hitBgImage;
    private GameObject inputRoot;
    private RectTransform inputRect;
    private TMP_InputField inputField;
    private TextMeshProUGUI inputText;

    private bool isHovered;
    private bool isEditing;
    private bool cleanedUp;

    internal bool IsEditing => isEditing;

    internal void Initialize(Slider targetSlider, TextMeshProUGUI labelText, SliderValueAdapter valueAdapter, bool ownLabelUpdates)
    {
        slider = targetSlider;
        valueText = labelText;
        adapter = valueAdapter;
        ownsLabelUpdates = ownLabelUpdates;
        baseTextColor = valueText.color;

        // 将标签本身射线关闭，统一由铺满数值格子的透明背景承接点击与悬停，便于小数字（如 0、7）命中。
        valueText.raycastTarget = false;
        CreateHitBackground();

        if (ownsLabelUpdates && slider != null)
        {
            slider.onValueChanged.AddListener(HandleOwnedSliderChanged);
        }

        SliderEditCoordinator.Register(this);
        SyncInteractable();
    }

    private void CreateHitBackground()
    {
        GameObject hitGo = new GameObject("edit_hit_bg", typeof(RectTransform));
        RectTransform rect = (RectTransform)hitGo.transform;
        rect.SetParent(valueText.rectTransform, worldPositionStays: false);
        rect.SetAsFirstSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(-4f, -2f);
        rect.offsetMax = new Vector2(4f, 2f);

        hitGo.AddComponent<LayoutElement>().ignoreLayout = true;
        hitBgImage = hitGo.AddComponent<Image>();
        hitBgImage.color = Color.clear;
        hitBgImage.raycastTarget = true;
    }

    private void EnsureInputField()
    {
        if (inputField != null)
        {
            return;
        }

        inputRoot = new GameObject("inline_number_input", typeof(RectTransform));
        inputRoot.SetActive(false);
        inputRect = (RectTransform)inputRoot.transform;
        inputRect.SetParent(valueText.rectTransform, worldPositionStays: false);
        inputRect.anchorMin = Vector2.zero;
        inputRect.anchorMax = Vector2.one;
        inputRect.pivot = new Vector2(0.5f, 0.5f);
        inputRoot.AddComponent<LayoutElement>().ignoreLayout = true;

        Image borderImage = inputRoot.AddComponent<Image>();
        borderImage.color = InputBorderColor;
        borderImage.raycastTarget = false;

        GameObject fillGo = new GameObject("bg", typeof(RectTransform));
        RectTransform fillRect = (RectTransform)fillGo.transform;
        fillRect.SetParent(inputRect, worldPositionStays: false);
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(1.5f, 1.5f);
        fillRect.offsetMax = new Vector2(-1.5f, -1.5f);
        Image fillImage = fillGo.AddComponent<Image>();
        fillImage.color = InputFillColor;
        fillImage.raycastTarget = true;

        GameObject areaGo = new GameObject("TextArea", typeof(RectTransform));
        RectTransform areaRect = (RectTransform)areaGo.transform;
        areaRect.SetParent(fillRect, worldPositionStays: false);
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(5f, 0f);
        areaRect.offsetMax = new Vector2(-5f, 0f);
        areaGo.AddComponent<RectMask2D>();

        GameObject textGo = new GameObject("Text", typeof(RectTransform));
        RectTransform textRect = (RectTransform)textGo.transform;
        textRect.SetParent(areaRect, worldPositionStays: false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        inputText = textGo.AddComponent<TextMeshProUGUI>();
        inputText.font = valueText.font;
        inputText.fontSize = valueText.fontSize;
        inputText.color = Color.white;
        inputText.enableWordWrapping = false;
        inputText.overflowMode = TextOverflowModes.Overflow;
        inputText.richText = false;
        inputText.raycastTarget = false;
        inputText.margin = Vector4.zero;

        inputField = inputRoot.AddComponent<TMP_InputField>();
        inputField.targetGraphic = fillImage;
        inputField.textViewport = areaRect;
        inputField.textComponent = inputText;
        inputField.fontAsset = valueText.font;
        inputField.pointSize = valueText.fontSize;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = 16;
        inputField.onFocusSelectAll = true;
        inputField.customCaretColor = true;
        inputField.caretColor = InputCaretColor;
        inputField.caretWidth = 2;
        inputField.selectionColor = InputSelectionColor;
        inputField.transition = Selectable.Transition.None;
        inputField.navigation = new Navigation { mode = Navigation.Mode.None };
        inputField.onEndEdit.AddListener(HandleEndEdit);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!cleanedUp && !isEditing && IsInteractable())
        {
            isHovered = true;
            RefreshVisualState();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isHovered)
        {
            isHovered = false;
            RefreshVisualState();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (cleanedUp || isEditing || eventData == null)
        {
            return;
        }
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging)
        {
            return;
        }
        if (!IsInteractable() || KeyBindHelper.IsCapturing)
        {
            return;
        }

        EnterEditMode();
    }

    internal void EnterEditMode()
    {
        if (cleanedUp || isEditing || slider == null || valueText == null)
        {
            return;
        }

        SliderEditCoordinator.BeginEdit(this);
        EnsureInputField();

        // 注意：不能关闭 valueText.enabled，否则父级 HorizontalLayoutGroup 会把缺少固定高度的标签节点压成 0 高度。
        // 使用显式中心/右对齐锚点与固定尺寸，并用透明文本色隐藏原数值以保持整行高度完全稳定。
        Rect rect = valueText.rectTransform.rect;
        float fontSize = valueText.fontSize > 0f ? valueText.fontSize : 18f;
        float boxWidth = Mathf.Max(MinInputWidth, rect.width + 8f);
        float boxHeight = Mathf.Max(MinInputHeight, rect.height + 4f, fontSize * 1.35f + 6f);
        bool rightAligned = IsRightAligned(valueText.alignment);
        Vector2 anchor = rightAligned ? new Vector2(1f, 0.5f) : new Vector2(0.5f, 0.5f);
        inputRect.anchorMin = anchor;
        inputRect.anchorMax = anchor;
        inputRect.pivot = anchor;
        inputRect.anchoredPosition = rightAligned ? new Vector2(2f, 0f) : Vector2.zero;
        inputRect.sizeDelta = new Vector2(boxWidth, boxHeight);
        inputRect.SetAsLastSibling();

        if (valueText.font != null)
        {
            inputText.font = valueText.font;
            inputField.fontAsset = valueText.font;
        }
        inputText.fontSize = fontSize;
        inputField.pointSize = fontSize;
        inputText.alignment = rightAligned
            ? TextAlignmentOptions.MidlineRight
            : TextAlignmentOptions.MidlineLeft;
        inputField.contentType = adapter.AllowDecimal
            ? TMP_InputField.ContentType.DecimalNumber
            : TMP_InputField.ContentType.IntegerNumber;

        isEditing = true;
        isHovered = false;
        if (hitBgImage != null)
        {
            hitBgImage.color = Color.clear;
            hitBgImage.raycastTarget = false;
        }
        valueText.color = Color.clear;

        string initialText = adapter.GetInitialEditText(slider.value);
        inputRoot.SetActive(true);
        inputField.SetTextWithoutNotify(initialText);
        inputField.Select();
        inputField.ActivateInputField();
        inputField.caretPosition = initialText.Length;
        inputField.selectionAnchorPosition = 0;
        inputField.selectionFocusPosition = initialText.Length;
    }

    internal void CommitAndExit()
    {
        if (isEditing)
        {
            ExitEditMode(commit: true, inputField != null ? inputField.text : null);
        }
    }

    internal void CancelEdit()
    {
        if (isEditing)
        {
            ExitEditMode(commit: false, null);
        }
    }

    internal void SyncFromSlider()
    {
        if (cleanedUp)
        {
            return;
        }
        if (isEditing)
        {
            ExitEditMode(commit: false, null);
        }
        if (slider != null && valueText != null && (ownsLabelUpdates || adapter.CanSyncDisplay(valueText.text)))
        {
            valueText.text = adapter.FormatDisplay(slider.value);
        }
        SyncInteractable();
    }

    internal void SyncInteractable()
    {
        if (cleanedUp)
        {
            return;
        }
        bool interactable = IsInteractable();
        if (!interactable && isEditing)
        {
            ExitEditMode(commit: false, null);
        }
        if (!interactable)
        {
            isHovered = false;
        }
        if (hitBgImage != null)
        {
            hitBgImage.raycastTarget = interactable && !isEditing;
        }
        RefreshVisualState();
    }

    private void HandleEndEdit(string text)
    {
        if (!isEditing)
        {
            return;
        }
        bool canceled = (inputField != null && inputField.wasCanceled) || Input.GetKeyDown(KeyCode.Escape);
        if (canceled)
        {
            SliderEditCoordinator.MarkEscapeConsumed();
        }
        ExitEditMode(commit: !canceled, text);
    }

    private void ExitEditMode(bool commit, string text)
    {
        isEditing = false;
        SliderEditCoordinator.EndEdit(this);

        if (commit && slider != null && adapter.TryParseSliderValue(text, out float targetValue))
        {
            if (!Mathf.Approximately(slider.value, targetValue))
            {
                slider.value = targetValue;
            }
            else if (ownsLabelUpdates && valueText != null)
            {
                valueText.text = adapter.FormatDisplay(slider.value);
            }
        }

        if (inputField != null)
        {
            EventSystem es = EventSystem.current;
            if (es != null && !es.alreadySelecting && es.currentSelectedGameObject == inputRoot)
            {
                es.SetSelectedGameObject(null);
            }
            inputRoot.SetActive(false);
        }

        SyncInteractable();
    }

    private void HandleOwnedSliderChanged(float value)
    {
        if (!cleanedUp && !isEditing && valueText != null && adapter != null)
        {
            valueText.text = adapter.FormatDisplay(value);
        }
    }

    private void RefreshVisualState()
    {
        if (valueText == null)
        {
            return;
        }
        if (isEditing)
        {
            valueText.color = Color.clear;
            if (hitBgImage != null)
            {
                hitBgImage.color = Color.clear;
            }
            return;
        }
        if (!IsInteractable())
        {
            Color disabledColor = baseTextColor;
            disabledColor.a = baseTextColor.a * DisabledAlpha;
            valueText.color = disabledColor;
            if (hitBgImage != null)
            {
                hitBgImage.color = Color.clear;
            }
            return;
        }

        valueText.color = isHovered ? HoverTextColor : baseTextColor;
        if (hitBgImage != null)
        {
            hitBgImage.color = isHovered ? HoverBgColor : Color.clear;
        }
    }

    private bool IsInteractable()
    {
        return slider != null && slider.interactable;
    }

    private static bool IsRightAligned(TextAlignmentOptions alignment)
    {
        return alignment == TextAlignmentOptions.Right
            || alignment == TextAlignmentOptions.TopRight
            || alignment == TextAlignmentOptions.BottomRight
            || alignment == TextAlignmentOptions.MidlineRight
            || alignment == TextAlignmentOptions.BaselineRight
            || alignment == TextAlignmentOptions.CaplineRight;
    }

    private void OnDisable()
    {
        if (isEditing)
        {
            ExitEditMode(commit: false, null);
        }
        isHovered = false;
        RefreshVisualState();
    }

    private void OnDestroy()
    {
        SliderEditCoordinator.Unregister(this);
        if (ownsLabelUpdates && slider != null)
        {
            slider.onValueChanged.RemoveListener(HandleOwnedSliderChanged);
        }
    }

    internal void Cleanup()
    {
        if (cleanedUp)
        {
            return;
        }
        cleanedUp = true;
        if (isEditing)
        {
            ExitEditMode(commit: false, null);
        }
        if (ownsLabelUpdates && slider != null)
        {
            slider.onValueChanged.RemoveListener(HandleOwnedSliderChanged);
        }
        if (valueText != null)
        {
            valueText.color = baseTextColor;
            valueText.enabled = true;
            valueText.raycastTarget = true;
        }
        if (hitBgImage != null)
        {
            Destroy(hitBgImage.gameObject);
            hitBgImage = null;
        }
        if (inputRoot != null)
        {
            Destroy(inputRoot);
            inputRoot = null;
            inputField = null;
        }
        Destroy(this);
    }
}

/// <summary>
/// 挂载在窗口根节点上的轻量索引，方便在 RefreshRules / ResetWidgets / Apply 时同步该窗口内的全部滑条数值编辑器。
/// </summary>
internal sealed class SliderEditorWindowTracker : MonoBehaviour
{
    private readonly List<InlineSliderNumberEditor> editors = new List<InlineSliderNumberEditor>();

    internal static void Track(GameObject windowRoot, InlineSliderNumberEditor editor)
    {
        if (windowRoot == null || editor == null)
        {
            return;
        }
        SliderEditorWindowTracker tracker = windowRoot.GetComponent<SliderEditorWindowTracker>();
        if (tracker == null)
        {
            tracker = windowRoot.AddComponent<SliderEditorWindowTracker>();
        }
        if (!tracker.editors.Contains(editor))
        {
            tracker.editors.Add(editor);
        }
    }

    internal static void SyncInteractableAll(GameObject windowRoot)
    {
        if (windowRoot == null)
        {
            return;
        }
        SliderEditorWindowTracker tracker = windowRoot.GetComponent<SliderEditorWindowTracker>();
        if (tracker == null)
        {
            return;
        }
        for (int i = tracker.editors.Count - 1; i >= 0; i--)
        {
            InlineSliderNumberEditor editor = tracker.editors[i];
            if (editor == null)
            {
                tracker.editors.RemoveAt(i);
            }
            else
            {
                editor.SyncInteractable();
            }
        }
    }

    internal static void SyncFromSlidersAll(GameObject windowRoot)
    {
        if (windowRoot == null)
        {
            return;
        }
        SliderEditorWindowTracker tracker = windowRoot.GetComponent<SliderEditorWindowTracker>();
        if (tracker == null)
        {
            return;
        }
        for (int i = tracker.editors.Count - 1; i >= 0; i--)
        {
            InlineSliderNumberEditor editor = tracker.editors[i];
            if (editor == null)
            {
                tracker.editors.RemoveAt(i);
            }
            else
            {
                editor.SyncFromSlider();
            }
        }
    }
}
