using System;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SliderNumericInput;

internal static class SliderAttachmentHelper
{
    private const float OptionValueWidth = 56f;
    private const float ModConfigValueWidth = 62f;

    internal static void AttachGameOptionSlider(CustomUIBuilder builder, GameOptionRegistry.Entry entry)
    {
        if (builder == null || entry == null || entry.kind != GameOptionRegistry.Kind.Slider)
        {
            return;
        }
        CustomUIElement sliderElement = builder.Last;
        if (sliderElement == null || sliderElement.slider == null)
        {
            return;
        }

        Slider slider = sliderElement.slider;
        CustomUIWindow window = builder.Handle;
        TextMeshProUGUI valueLabel = FindExistingValueLabel(slider.transform, "label_value_" + entry.id);
        bool createdLabel = false;
        if (valueLabel == null)
        {
            valueLabel = InsertValueLabelBeforeSlider(window, slider, "label_value_" + entry.id, OptionValueWidth);
            createdLabel = valueLabel != null;
        }
        if (valueLabel == null)
        {
            return;
        }

        SliderValueAdapter adapter = SliderValueAdapter.Create(slider, entry.valueText, valueLabel.text);
        if (createdLabel)
        {
            valueLabel.text = adapter.FormatDisplay(slider.value);
        }

        AttachEditor(window != null ? window.root : null, slider, valueLabel, adapter, createdLabel);
    }

    internal static void AttachModConfigWindow(CustomUIWindow window)
    {
        if (window == null || !window.IsAlive || window.ContentRoot == null)
        {
            return;
        }

        Slider[] sliders = window.ContentRoot.GetComponentsInChildren<Slider>(includeInactive: true);
        foreach (Slider slider in sliders)
        {
            if (slider == null)
            {
                continue;
            }
            string key = ExtractSliderKey(slider.gameObject.name);
            string expectedName = string.IsNullOrEmpty(key) ? null : ("label_value_" + key);
            TextMeshProUGUI valueLabel = FindExistingValueLabel(slider.transform, expectedName);
            bool createdLabel = false;
            if (valueLabel == null)
            {
                valueLabel = InsertValueLabelBeforeSlider(window, slider, expectedName ?? "label_value_slider", ModConfigValueWidth);
                createdLabel = valueLabel != null;
            }
            if (valueLabel == null)
            {
                continue;
            }
            if (valueLabel.GetComponent<InlineSliderNumberEditor>() != null)
            {
                continue;
            }

            SliderValueAdapter adapter = SliderValueAdapter.Create(slider, null, createdLabel ? null : valueLabel.text);
            if (createdLabel)
            {
                valueLabel.text = adapter.FormatDisplay(slider.value);
            }

            AttachEditor(window.root, slider, valueLabel, adapter, createdLabel);
        }
    }

    private static void AttachEditor(
        GameObject windowRoot,
        Slider slider,
        TextMeshProUGUI valueLabel,
        SliderValueAdapter adapter,
        bool ownLabelUpdates)
    {
        InlineSliderNumberEditor editor = valueLabel.gameObject.GetComponent<InlineSliderNumberEditor>();
        if (editor == null)
        {
            editor = valueLabel.gameObject.AddComponent<InlineSliderNumberEditor>();
        }
        editor.Initialize(slider, valueLabel, adapter, ownLabelUpdates);
        SliderEditorWindowTracker.Track(windowRoot, editor);
    }

    private static string ExtractSliderKey(string goName)
    {
        if (!string.IsNullOrEmpty(goName) && goName.StartsWith("slider_", StringComparison.Ordinal))
        {
            return goName.Substring("slider_".Length);
        }
        return null;
    }

    /// <summary>
    /// 在滑条所在的同行容器中查找紧邻滑条前方的数值标签。
    /// 优先匹配约定名称 label_value_*，其次匹配弹性空白之后出现的独立标签，避免误认左侧标题。
    /// </summary>
    private static TextMeshProUGUI FindExistingValueLabel(Transform sliderTransform, string preferredName)
    {
        Transform row = sliderTransform != null ? sliderTransform.parent : null;
        if (row == null)
        {
            return null;
        }

        int sliderIndex = sliderTransform.GetSiblingIndex();
        if (!string.IsNullOrEmpty(preferredName))
        {
            for (int i = sliderIndex - 1; i >= 0; i--)
            {
                Transform child = row.GetChild(i);
                if (string.Equals(child.name, preferredName, StringComparison.Ordinal))
                {
                    TextMeshProUGUI tmp = child.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                    if (tmp != null)
                    {
                        return tmp;
                    }
                }
            }
        }

        int flexIndex = -1;
        for (int i = 0; i < sliderIndex; i++)
        {
            if (string.Equals(row.GetChild(i).name, "flexible_space", StringComparison.Ordinal))
            {
                flexIndex = i;
            }
        }

        int minCandidateIndex = flexIndex >= 0 ? flexIndex + 1 : 1;
        for (int i = sliderIndex - 1; i >= minCandidateIndex; i--)
        {
            Transform child = row.GetChild(i);
            if (string.Equals(child.name, "space", StringComparison.Ordinal))
            {
                continue;
            }
            TextMeshProUGUI tmp = child.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (tmp != null)
            {
                return tmp;
            }
        }
        return null;
    }

    private static TextMeshProUGUI InsertValueLabelBeforeSlider(CustomUIWindow window, Slider slider, string goName, float width)
    {
        CustomUIManager manager = CustomUIManager.Instance;
        Transform row = slider != null ? slider.transform.parent : null;
        if (manager == null || row == null)
        {
            return null;
        }

        int sliderIndex = slider.transform.GetSiblingIndex();
        GameObject labelGo = manager.SpawnElement(CustomUIManager.ElementKind.Label, row, window, goName);
        if (labelGo == null)
        {
            return null;
        }

        labelGo.transform.SetSiblingIndex(sliderIndex);
        LayoutElement layout = labelGo.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = labelGo.AddComponent<LayoutElement>();
        }
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;

        TextMeshProUGUI tmp = labelGo.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        if (tmp != null)
        {
            tmp.alignment = TextAlignmentOptions.Right;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.text = slider.wholeNumbers
                ? Mathf.RoundToInt(slider.value).ToString(CultureInfo.InvariantCulture)
                : slider.value.ToString("0.##", CultureInfo.InvariantCulture);
        }
        return tmp;
    }
}

[HarmonyPatch(typeof(GameOptionUIManager), "BuildEntry")]
internal static class GameOptionBuildEntryPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CustomUIBuilder builder, GameOptionRegistry.Entry entry)
    {
        if (!SliderNumericInputMod.Active)
        {
            return;
        }
        try
        {
            SliderAttachmentHelper.AttachGameOptionSlider(builder, entry);
        }
        catch (Exception e)
        {
            SliderNumericInputMod.Log.Error($"挂载选项滑条数字输入失败（{entry?.id}）：{e}");
        }
    }
}

[HarmonyPatch(typeof(GameOptionUIManager), "RefreshRules")]
internal static class GameOptionRefreshRulesPatch
{
    private static void Postfix(CustomUIWindow ___window)
    {
        if (!SliderNumericInputMod.Active || ___window == null || !___window.IsAlive)
        {
            return;
        }
        try
        {
            SliderEditorWindowTracker.SyncInteractableAll(___window.root);
        }
        catch (Exception e)
        {
            SliderNumericInputMod.Log.Error("同步选项滑条禁用状态失败：" + e);
        }
    }
}

[HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.OnApplySetting))]
internal static class GameOptionApplyPatch
{
    private static void Prefix()
    {
        if (SliderNumericInputMod.Active)
        {
            SliderEditCoordinator.FlushActiveEdit();
        }
    }
}

[HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.ConsumedEscapeThisFrame))]
internal static class GameOptionConsumedEscapePatch
{
    private static void Postfix(ref bool __result)
    {
        if (SliderNumericInputMod.Active && !__result && SliderEditCoordinator.ConsumedEscapeThisFrame())
        {
            __result = true;
        }
    }
}

[HarmonyPatch(typeof(CustomUIManager), "HandleEscape")]
internal static class CustomUIHandleEscapePatch
{
    private static bool Prefix()
    {
        if (SliderNumericInputMod.Active && SliderEditCoordinator.ConsumedEscapeThisFrame())
        {
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(ModConfigWindow), nameof(ModConfigWindow.Open))]
internal static class ModConfigOpenPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(LoadedMod mod, CustomUIWindow __result)
    {
        if (!SliderNumericInputMod.Active || __result == null || !__result.IsAlive)
        {
            return;
        }
        try
        {
            SliderAttachmentHelper.AttachModConfigWindow(__result);
        }
        catch (Exception e)
        {
            SliderNumericInputMod.Log.Error($"挂载模组配置滑条数字输入失败（{mod?.Manifest?.ID}）：{e}");
        }
    }
}

[HarmonyPatch(typeof(ModConfigWindow), "Apply")]
internal static class ModConfigApplyPatch
{
    private static void Prefix()
    {
        if (SliderNumericInputMod.Active)
        {
            SliderEditCoordinator.FlushActiveEdit();
        }
    }

    private static void Postfix(CustomUIWindow window)
    {
        if (SliderNumericInputMod.Active && window != null && window.IsAlive)
        {
            SliderEditorWindowTracker.SyncFromSlidersAll(window.root);
        }
    }
}

[HarmonyPatch(typeof(ModConfigWindow), "ResetWidgets")]
internal static class ModConfigResetWidgetsPatch
{
    private static void Prefix()
    {
        if (SliderNumericInputMod.Active)
        {
            SliderEditCoordinator.CancelActiveEdit();
        }
    }

    private static void Postfix(CustomUIWindow window)
    {
        if (SliderNumericInputMod.Active && window != null && window.IsAlive)
        {
            SliderEditorWindowTracker.SyncFromSlidersAll(window.root);
        }
    }
}
