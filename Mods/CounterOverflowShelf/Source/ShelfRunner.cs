using System;
using UnityEngine;

namespace CounterOverflowShelf;

/// <summary>
/// 每帧维护溢出货架窗口的显隐、标题与位置。
/// Author: SaintCirno9
/// </summary>
internal sealed class ShelfRunner : MonoBehaviour
{
    private string m_lastError;

    private void LateUpdate()
    {
        try
        {
            OverflowShelf.Tick();
        }
        catch (Exception exception)
        {
            // 同一异常每帧都会重复抛出，只记录一次。
            string message = exception.ToString();
            if (message != m_lastError)
            {
                m_lastError = message;
                OverflowShelfMod.Log.Error("维护溢出货架窗口失败：" + message);
            }
        }
    }
}
