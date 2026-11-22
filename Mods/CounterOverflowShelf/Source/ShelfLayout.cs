using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CounterOverflowShelf;

/// <summary>
/// 货架窗口的位置（相对谈判柜台左上角的偏移）与显示行数，店内和杰克逊博士处分别记录。
/// 存在模组数据目录的文件里，与存档无关。
/// Author: SaintCirno9
/// </summary>
internal static class ShelfLayout
{
    private const string FileName = "layout.txt";

    // 拖动或调整大小时每帧都在变，停下一会儿再写文件。
    private const float SaveDelay = 0.5f;

    private static readonly Dictionary<string, string> s_values = new Dictionary<string, string>();

    private static bool s_loaded;

    private static bool s_dirty;

    private static float s_changedAt;

    internal static bool TryGetOffset(bool jackson, out Vector2 offset)
    {
        EnsureLoaded();
        offset = default;
        if (!TryGetFloat(Key(jackson, "x"), out float x) || !TryGetFloat(Key(jackson, "y"), out float y))
        {
            return false;
        }
        offset = new Vector2(x, y);
        return true;
    }

    internal static void SetOffset(bool jackson, Vector2 offset)
    {
        Set(Key(jackson, "x"), offset.x.ToString("R", CultureInfo.InvariantCulture));
        Set(Key(jackson, "y"), offset.y.ToString("R", CultureInfo.InvariantCulture));
    }

    internal static void ClearOffsets()
    {
        Remove(Key(false, "x"));
        Remove(Key(false, "y"));
        Remove(Key(true, "x"));
        Remove(Key(true, "y"));
    }

    /// <summary>玩家调整过就用调整后的行数，否则用设置里该场景的默认行数。</summary>
    internal static int GetRows(bool jackson)
    {
        EnsureLoaded();
        if (s_values.TryGetValue(Key(jackson, "rows"), out string text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rows) && rows > 0)
        {
            return rows;
        }
        return OverflowShelfMod.DefaultRows(jackson);
    }

    internal static void SetRows(bool jackson, int rows)
    {
        Set(Key(jackson, "rows"), rows.ToString(CultureInfo.InvariantCulture));
    }

    internal static void ClearRows(bool jackson)
    {
        Remove(Key(jackson, "rows"));
    }

    internal static void SaveIfIdle()
    {
        if (s_dirty && Time.unscaledTime - s_changedAt >= SaveDelay)
        {
            Save();
        }
    }

    internal static void Save()
    {
        if (!s_dirty)
        {
            return;
        }
        s_dirty = false;
        StringBuilder builder = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in s_values)
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }
        try
        {
            ModHelper.WriteModDataFileText(OverflowShelfMod.ModId, FileName, builder.ToString());
        }
        catch (Exception exception)
        {
            OverflowShelfMod.Log.Error("保存溢出货架窗口布局失败：" + exception);
        }
    }

    private static void EnsureLoaded()
    {
        if (s_loaded)
        {
            return;
        }
        s_loaded = true;
        string text;
        try
        {
            text = ModHelper.ReadModDataFileText(OverflowShelfMod.ModId, FileName);
        }
        catch (Exception exception)
        {
            OverflowShelfMod.Log.Error("读取溢出货架窗口布局失败，使用默认布局：" + exception);
            return;
        }
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        foreach (string line in text.Split('\n'))
        {
            int separator = line.IndexOf('=');
            if (separator > 0)
            {
                s_values[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
            }
        }
    }

    private static bool TryGetFloat(string key, out float value)
    {
        value = 0f;
        return s_values.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void Set(string key, string value)
    {
        EnsureLoaded();
        if (s_values.TryGetValue(key, out string old) && old == value)
        {
            return;
        }
        s_values[key] = value;
        MarkDirty();
    }

    private static void Remove(string key)
    {
        EnsureLoaded();
        if (s_values.Remove(key))
        {
            MarkDirty();
        }
    }

    private static void MarkDirty()
    {
        s_dirty = true;
        s_changedAt = Time.unscaledTime;
    }

    private static string Key(bool jackson, string name)
    {
        return (jackson ? "jackson." : "store.") + name;
    }
}
