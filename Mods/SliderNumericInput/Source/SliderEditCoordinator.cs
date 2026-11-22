using System.Collections.Generic;
using UnityEngine;

namespace SliderNumericInput;

/// <summary>
/// 协调全局唯一的活跃滑条编辑框，处理 Escape 取消防穿透以及窗口应用/重置前的提交与清理。
/// </summary>
internal static class SliderEditCoordinator
{
    private static readonly List<InlineSliderNumberEditor> AllEditors = new List<InlineSliderNumberEditor>();
    private static InlineSliderNumberEditor activeEditor;
    private static int escapeConsumedFrame = -1;

    internal static void Register(InlineSliderNumberEditor editor)
    {
        if (editor != null && !AllEditors.Contains(editor))
        {
            AllEditors.Add(editor);
        }
    }

    internal static void Unregister(InlineSliderNumberEditor editor)
    {
        AllEditors.Remove(editor);
        if (activeEditor == editor)
        {
            activeEditor = null;
        }
    }

    internal static void BeginEdit(InlineSliderNumberEditor editor)
    {
        if (activeEditor != null && activeEditor != editor)
        {
            activeEditor.CommitAndExit();
        }
        activeEditor = editor;
    }

    internal static void EndEdit(InlineSliderNumberEditor editor)
    {
        if (activeEditor == editor)
        {
            activeEditor = null;
        }
    }

    internal static void MarkEscapeConsumed()
    {
        escapeConsumedFrame = Time.frameCount;
    }

    /// <summary>
    /// 在 StoreUIManager 或 CustomUIManager 处理返回键时调用。
    /// 若当前处于数字编辑模式或本帧刚因 Esc 取消编辑，则拦截本次返回以保住设置窗口。
    /// </summary>
    internal static bool ConsumedEscapeThisFrame()
    {
        if (escapeConsumedFrame == Time.frameCount)
        {
            return true;
        }
        if (activeEditor != null && activeEditor.IsEditing)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                activeEditor.CancelEdit();
                escapeConsumedFrame = Time.frameCount;
                return true;
            }
            return true;
        }
        return false;
    }

    internal static void FlushActiveEdit()
    {
        if (activeEditor != null && activeEditor.IsEditing)
        {
            activeEditor.CommitAndExit();
        }
    }

    internal static void CancelActiveEdit()
    {
        if (activeEditor != null && activeEditor.IsEditing)
        {
            activeEditor.CancelEdit();
        }
    }

    internal static void ClearAll()
    {
        activeEditor = null;
        escapeConsumedFrame = -1;
        for (int i = AllEditors.Count - 1; i >= 0; i--)
        {
            InlineSliderNumberEditor editor = AllEditors[i];
            if (editor != null)
            {
                editor.Cleanup();
            }
        }
        AllEditors.Clear();
    }
}
