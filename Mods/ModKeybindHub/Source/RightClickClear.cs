using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModKeybindHub;

/// <summary>
/// 挂在选项窗口按键按钮上，右键把该条目的待应用按键设为“无”。原版 Button 只响应左键，两者互不干扰。
/// 原版改键捕获可绑定鼠标右键：按下右键时若正在捕获或当帧刚结束捕获（右键已被绑定），这次点击不清空。
/// Author: SaintCirno9
/// </summary>
internal sealed class RightClickClear : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
{
    internal string OptionId;

    private bool _pressedDuringCapture;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            // SuppressHotkeys 在捕获中或结束捕获的当帧为真，覆盖原版先于事件系统处理捕获的情况
            _pressedDuringCapture = KeyBindHelper.SuppressHotkeys;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right || string.IsNullOrEmpty(OptionId))
        {
            return;
        }
        if (_pressedDuringCapture)
        {
            _pressedDuringCapture = false;
            return;
        }
        KeybindHub.ClearPendingKey(OptionId, GetComponent<Button>());
    }
}
