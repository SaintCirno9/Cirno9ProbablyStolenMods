# 提示框自适应缩放 (`TooltipAutoScale`)

解决《Probably Stolen》在安装机器多级升级（`MoreDeviceUpgrades`）、模块词条、基因种子或自带多段说明时，物品/机器悬停提示框被默认 26 个中文字符折行拉得过长并冲出屏幕底部导致无法阅读的问题。

## 功能机制

1. **第一阶段：超长先适度加宽折行**
   - 当悬停提示框高度仍在屏幕安全占比内时，保持原版宽度与外观不变。
   - 仅当行数超过当前最大占屏比例所允许的舒适行数时，在屏幕宽度预算内分步加宽 `RichTextBuilder` 折行宽度（同步放宽 `OverlayHandler.ShowTooltip` / `ShowTooltip2` 的 `maxCharWidth` 裁剪上限），一旦满足高度上限或达到允许最大宽度即停止；若加宽无法减少行数（例如纯手动换行列表），则不增加无效水平留白。
   - 加宽作用域严格限定在物品/机器悬停提示生成路径，不影响对话、书籍或事件日志排版。
2. **第二阶段：仍超高或过宽时自动等比缩小**
   - 当提示框加宽后仍超过屏幕占比上限时，按实际渲染宽高对 `OverlayHandler.tooltipWindow` 整体等比缩小，并受最小缩放下限保护以保持文本可读。
   - 基于缩放后视觉包围盒重新计算光标侧翻转（避免遮挡指针下的物品/机器）及屏幕四边钳位，防止底部或侧边溢出屏幕外。

## 选项配置

可在游戏设置（Options -> 提示框自适应缩放）中实时调整：
- **超长先适度加宽折行**：默认开启；关闭后将跳过加宽，直接按占屏上限等比缩放。
- **提示框最大占屏比例 (%)**：`50 ~ 95`，默认 `85`。
- **最小自动缩放比例 (%)**：`35 ~ 100`，默认 `50`。

## 兼容性与权限

- 仅使用公开游戏 API 与 Harmony 补丁，不声明也不依赖 `reflection-external`（`<Capabilities />`）。
- 仅作用于 `OverlayHandler.current.GetTooltipWindow()`，不干扰其他界面窗口。
- **兼容工坊 `3834796164`（浮窗位置优化 / `StableTooltipSide`）**：在 `PixelWindow.Show` 末尾以低优先级按缩放后实际宽高重算落点，遵循其象限对齐设置且保持紧贴悬停目标。
- **兼容工坊 `3828868413`（`ItemDetails` 物品详情）**：开启顶部类型标签时自动预留安全顶边距，避免超长提示框贴顶时上方标签被裁出屏幕。

## 构建与部署

```pwsh
dotnet build "Mods/TooltipAutoScale/TooltipAutoScale.csproj" -c Release
```

构建后自动部署至 `<LocalMods>\TooltipAutoScale\`。
