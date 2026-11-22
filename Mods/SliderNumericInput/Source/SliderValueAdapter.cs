using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace SliderNumericInput;

/// <summary>
/// 负责滑条内部数值与界面显示文本/用户输入文本之间的双向解析和格式化。
/// 支持纯数字、自定义百分号等仿射缩放格式，以及带单位后缀的已有标签。
/// </summary>
internal sealed class SliderValueAdapter
{
    private readonly Slider slider;
    private readonly Func<float, string> customFormatter;
    private readonly bool isAffineDisplay;
    private readonly float dispMin;
    private readonly float dispMax;
    private readonly float sliderMin;
    private readonly float sliderMax;
    private readonly string affinePrefix;
    private readonly string affineSuffix;
    private readonly string fallbackPrefix;
    private readonly string fallbackSuffix;
    private readonly bool canSyncExternalLabel;

    internal bool AllowDecimal { get; }

    private SliderValueAdapter(
        Slider slider,
        Func<float, string> customFormatter,
        bool isAffineDisplay,
        float dispMin,
        float dispMax,
        float sliderMin,
        float sliderMax,
        string affinePrefix,
        string affineSuffix,
        string fallbackPrefix,
        string fallbackSuffix,
        bool canSyncExternalLabel,
        bool allowDecimal)
    {
        this.slider = slider;
        this.customFormatter = customFormatter;
        this.isAffineDisplay = isAffineDisplay;
        this.dispMin = dispMin;
        this.dispMax = dispMax;
        this.sliderMin = sliderMin;
        this.sliderMax = sliderMax;
        this.affinePrefix = affinePrefix ?? "";
        this.affineSuffix = affineSuffix ?? "";
        this.fallbackPrefix = fallbackPrefix ?? "";
        this.fallbackSuffix = fallbackSuffix ?? "";
        this.canSyncExternalLabel = canSyncExternalLabel;
        AllowDecimal = allowDecimal;
    }

    internal static SliderValueAdapter Create(Slider slider, Func<float, string> customFormatter, string existingLabelText)
    {
        if (customFormatter != null)
        {
            float min = slider.minValue;
            float max = slider.maxValue;
            float mid = slider.wholeNumbers ? Mathf.Round((min + max) * 0.5f) : (min + max) * 0.5f;
            string sMin = SafeFormat(customFormatter, min);
            string sMid = SafeFormat(customFormatter, mid);
            string sMax = SafeFormat(customFormatter, max);

            if (TryExtractNumberToken(sMin, out string pMin, out float vMin, out string sufMin, out bool decMin)
                && TryExtractNumberToken(sMid, out string pMid, out float vMid, out string sufMid, out bool decMid)
                && TryExtractNumberToken(sMax, out string pMax, out float vMax, out string sufMax, out bool decMax)
                && string.Equals(pMin, pMid, StringComparison.Ordinal)
                && string.Equals(pMin, pMax, StringComparison.Ordinal)
                && string.Equals(sufMin, sufMid, StringComparison.Ordinal)
                && string.Equals(sufMin, sufMax, StringComparison.Ordinal)
                && Mathf.Abs(max - min) > 1e-5f
                && Mathf.Abs(vMax - vMin) > 1e-5f)
            {
                float expectedMid = vMin + (mid - min) / (max - min) * (vMax - vMin);
                float tolerance = Mathf.Max(0.55f, Mathf.Abs(vMax - vMin) * 0.02f);
                if (Mathf.Abs(vMid - expectedMid) <= tolerance)
                {
                    bool allowDec = !slider.wholeNumbers || decMin || decMid || decMax || !IsWholeNumber(vMin) || !IsWholeNumber(vMax);
                    return new SliderValueAdapter(
                        slider,
                        customFormatter,
                        isAffineDisplay: true,
                        vMin,
                        vMax,
                        min,
                        max,
                        pMin,
                        sufMin,
                        "",
                        "",
                        canSyncExternalLabel: true,
                        allowDecimal: allowDec);
                }
            }

            return new SliderValueAdapter(
                slider,
                customFormatter,
                isAffineDisplay: false,
                0f,
                0f,
                slider.minValue,
                slider.maxValue,
                "",
                "",
                "",
                "",
                canSyncExternalLabel: true,
                allowDecimal: !slider.wholeNumbers);
        }

        string fbPrefix = "";
        string fbSuffix = "";
        bool canSync = true;
        if (!string.IsNullOrEmpty(existingLabelText))
        {
            if (TryExtractNumberToken(existingLabelText, out string prefix, out float parsed, out string suffix, out _)
                && Mathf.Abs(parsed - slider.value) <= 0.05f)
            {
                fbPrefix = prefix;
                fbSuffix = suffix;
            }
            else
            {
                canSync = false;
            }
        }

        return new SliderValueAdapter(
            slider,
            null,
            isAffineDisplay: false,
            0f,
            0f,
            slider.minValue,
            slider.maxValue,
            "",
            "",
            fbPrefix,
            fbSuffix,
            canSyncExternalLabel: canSync,
            allowDecimal: !slider.wholeNumbers);
    }

    internal string FormatDisplay(float sliderValue)
    {
        if (customFormatter != null)
        {
            return SafeFormat(customFormatter, sliderValue);
        }
        return fallbackPrefix + FormatRaw(sliderValue) + fallbackSuffix;
    }

    internal bool CanSyncDisplay(string currentLabelText)
    {
        if (customFormatter != null)
        {
            return true;
        }
        if (canSyncExternalLabel)
        {
            return true;
        }
        return TryExtractNumberToken(currentLabelText, out _, out _, out _, out _);
    }

    internal string GetInitialEditText(float sliderValue)
    {
        if (isAffineDisplay && customFormatter != null)
        {
            string formatted = SafeFormat(customFormatter, sliderValue);
            if (TryExtractNumberToken(formatted, out string prefix, out float number, out string suffix, out _)
                && string.Equals(prefix, affinePrefix, StringComparison.Ordinal)
                && string.Equals(suffix, affineSuffix, StringComparison.Ordinal))
            {
                return AllowDecimal
                    ? number.ToString("0.##", CultureInfo.InvariantCulture)
                    : Mathf.RoundToInt(number).ToString(CultureInfo.InvariantCulture);
            }
            float mapped = dispMin + (sliderValue - sliderMin) / (sliderMax - sliderMin) * (dispMax - dispMin);
            return AllowDecimal
                ? mapped.ToString("0.##", CultureInfo.InvariantCulture)
                : Mathf.RoundToInt(mapped).ToString(CultureInfo.InvariantCulture);
        }

        return FormatRaw(sliderValue);
    }

    internal bool TryParseSliderValue(string inputText, out float result)
    {
        result = slider.value;
        if (string.IsNullOrWhiteSpace(inputText))
        {
            return false;
        }

        string text = inputText.Trim();
        if (isAffineDisplay)
        {
            text = StripAffixes(text, affinePrefix, affineSuffix);
            if (!TryParseFloat(text, out float typedDisplay))
            {
                return false;
            }
            float t = (typedDisplay - dispMin) / (dispMax - dispMin);
            float raw = sliderMin + t * (sliderMax - sliderMin);
            result = ClampAndSnap(raw);
            return true;
        }

        text = StripAffixes(text, fallbackPrefix, fallbackSuffix);
        if (!TryParseFloat(text, out float parsed))
        {
            return false;
        }
        result = ClampAndSnap(parsed);
        return true;
    }

    private float ClampAndSnap(float raw)
    {
        if (slider.wholeNumbers)
        {
            raw = Mathf.Round(raw);
        }
        return Mathf.Clamp(raw, slider.minValue, slider.maxValue);
    }

    private string FormatRaw(float value)
    {
        if (slider.wholeNumbers)
        {
            return Mathf.RoundToInt(value).ToString(CultureInfo.InvariantCulture);
        }
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string SafeFormat(Func<float, string> formatter, float value)
    {
        try
        {
            return formatter(value) ?? "";
        }
        catch
        {
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    private static string StripAffixes(string text, string prefix, string suffix)
    {
        if (!string.IsNullOrEmpty(prefix) && text.StartsWith(prefix, StringComparison.Ordinal))
        {
            text = text.Substring(prefix.Length);
        }
        if (!string.IsNullOrEmpty(suffix) && text.EndsWith(suffix, StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - suffix.Length);
        }
        return text.Trim();
    }

    private static bool TryParseFloat(string text, out float value)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value))
        {
            return true;
        }
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            && !float.IsNaN(value) && !float.IsInfinity(value))
        {
            return true;
        }
        value = 0f;
        return false;
    }

    private static bool IsWholeNumber(float value)
    {
        return Mathf.Abs(value - Mathf.Round(value)) < 0.001f;
    }

    private static bool TryExtractNumberToken(string text, out string prefix, out float number, out string suffix, out bool hasDecimal)
    {
        prefix = "";
        number = 0f;
        suffix = "";
        hasDecimal = false;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsDigit(c))
            {
                start = i;
                break;
            }
            if ((c == '-' || c == '+') && i + 1 < text.Length
                && (char.IsDigit(text[i + 1]) || (text[i + 1] == '.' && i + 2 < text.Length && char.IsDigit(text[i + 2]))))
            {
                start = i;
                break;
            }
            if (c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return false;
        }

        int end = start;
        if (text[end] == '-' || text[end] == '+')
        {
            end++;
        }
        bool seenDot = false;
        while (end < text.Length)
        {
            char c = text[end];
            if (char.IsDigit(c))
            {
                end++;
            }
            else if (c == '.' && !seenDot && end + 1 < text.Length && char.IsDigit(text[end + 1]))
            {
                seenDot = true;
                hasDecimal = true;
                end++;
            }
            else
            {
                break;
            }
        }

        string token = text.Substring(start, end - start);
        if (!TryParseFloat(token, out number))
        {
            return false;
        }
        prefix = text.Substring(0, start);
        suffix = text.Substring(end);
        return true;
    }
}
