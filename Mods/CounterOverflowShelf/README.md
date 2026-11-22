# 柜台扩容货架 / Counter Expansion Shelf

- ID：`cirno9.counter_overflow_shelf`
- 工坊标题：`柜台扩容货架（货物摆不下不消失）` / `Counter Expansion Shelf (No Lost Goods)`
- 版本：1.2.0
- 作者：SaintCirno9

## 功能

顾客带来的货物或杰克逊博士的库存在谈判柜台上摆不下时，原版 1.0 起会对部分店内卖货顾客启用翻页（最多 12 页），未启用翻页或超出页数上限的则直接销毁（`Player.log` 中的 `Destroyed unplaced items: ... (front counter full)` 或 `does not fit the front counter or a stock page`）。本模组开启后完全接管原版翻页与溢出销毁，把摆不下的货物统一放进柜台旁的扩容货架：

- 货架由真实原版格子（`GameGridInventory`）持有货物，外层滚动组件只负责显示，宽 18 格，支持滚轮与拖动滚动条，容量上限 200 行。
- 不改动原版柜台尺寸，也无需使用原版翻页按钮来回切换页面；货物放进货架后始终留在同一个真实原版格子容器里，全部直观可见。
- 窗口采用原版储藏区的 Machinery 样式：灰色边框、居中浅米灰标题、深色格子和灰色滚动条。滚动槽与滑块使用原版灰色九宫格资源，不再保留旧滚动条贴图自带的橙色边框。
- 右键货架空白处或使用原版整理菜单快捷键，可按大小、从头/从尾整理并撤销。安装并启用 [Smart Inventory Arrange](https://steamcommunity.com/sharedfiles/filedetails/?id=3829206666)（核对版本 3.17.57-mono.1）后，还支持其“智能排列”和“保留容器和机器排列”，这两行支持悬停颜色反馈；智能排序算法由该模组提供，不是本模组内置功能。固定物品与移除限制保持不变，整理不改变购买状态，仍可取放、批发与清台。
- 本次交易出现溢出货物后，货架在柜台可见时自动显示；拿空后仍保留，方便把查看后不买的货物放回，直到顾客离开、离开博士或原版清台时才收起空货架。默认贴在柜台一侧；设置的那一侧超出屏幕时换到另一侧（杰克逊博士的柜台靠右，通常显示在左侧）。
- 拖动标题栏可移动窗口，拖动右下角手柄可按整行调整显示高度（宽度固定 18 格）。位置按相对柜台的偏移记住，与行数一起按店内、杰克逊博士处分别保存在 `<GameData>\ModData\cirno9_counter_overflow_shelf\layout.txt`（游戏把 ID 里的点换成下划线），与存档无关。
- 窗口没有关闭按钮，Esc 也不会关掉它，避免货架上的货被藏起来后随清台销毁。
- 把货物从货架拖到柜台或谈判区，即可照常谈判、购买；没买的货可以从柜台或谈判区拖回货架腾出位置。只卖货的顾客在柜台与谈判区拿空后，只要货架上还有未购货物也不会误判已卖完而提前离开。
- 支持原生 Alt/Ctrl + 点击：货架上的未购物品快速移到谈判区；在谈判区对未购物品使用 Alt/Ctrl + 点击时，优先退回前柜台，放不下的剩余数量自动进入扩容货架，即使本次交易还没有生成货架也有效。谈判区或货架也放不下时，未转移部分留在原处，不销毁、不自动购买；店内与杰克逊博士处均适用，固定物品和移除限制照常生效。
- 快捷键沿用游戏设置，不硬编码 Alt/Ctrl/Shift。Ctrl 原本会按来源在库存与柜台间转移，Alt 原本转到谈判区，Shift 原本转到展示柜；已购物品保持这些原版规则，Shift 不把未购物品送进展示柜。
- 批发连同货架上的货物一起计价、一次性买下（谈判面板显示的批发总价已包含货架，仅剩货架有货时批发按钮依然可见），避免只卖货的顾客批发后直接离开、货架上的货随清台销毁。买下的货物与原版一样按“已购物品移入库存”选项处理，其余从后台开始找空位移出货架。
- 违禁品与赃物检查、报警举报判定及举报奖金同步计入货架上的未购货物，避免溢出物品进入货架后导致误报或少算奖金。
- 货架只接收非己方货物：本模组放入的溢出货物、货架内部的挪动，以及从谈判柜台或谈判区放回的货物；自己的物品和其他容器里的物品放不上去。
- 顾客离开、拉下卷帘门、离开杰克逊博士、日终等原版清除柜台的时机，货架上剩下的非己方货物一并清除，结果与原版“放不下就销毁”一致；已归玩家的物品先移进店内容器，店内全满时留在货架上并在日志提示。

## 设置

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `enabled` | 开 | 关闭后新的溢出货物恢复原版处理（店内按原版翻页，未启用翻页的按原版销毁），已在货架上的货物不受影响 |
| `visible_rows` | 4 | 店内货架默认显示的行数（2~30）；修改后丢弃店内拖动调整过的行数 |
| `jackson_rows` | 12 | 杰克逊博士处货架默认显示的行数（2~30）；修改后丢弃博士处拖动调整过的行数 |
| `side` | 右侧 | 货架默认贴在柜台的哪一侧；修改后两处都恢复默认位置 |

## 实现

- `StockPageHelper.IsPagingFor` 前缀：总开关开启时直接返回 `false`，屏蔽原版 1.0 货物翻页，让所有非己方溢出货物统一落入 `"front counter full"` 路径进入扩容货架；总开关关闭时自动恢复原版翻页。
- `GraphUtils.DestroyIfUnplaced` 前缀：上下文为 `front counter full` 或 `does not fit the front counter or a stock page` 时先把物品放进货架；放进去后物品已有所属容器，原版方法直接按“已放置”返回，不再销毁。覆盖 `PlayerStore.TryAddDirectSellingItemToTable(GameItem, ...)`、`AddDirectSellingItemToTableMiddle` 与直接调用 `StockPageHelper.PlaceOrHold` 的路径。
- `PlayerStore.TryAddDirectSellingItemToTable(string, ...)` 前缀：原版在柜台已满且未启用翻页时不创建物品、只记 `offer skipped`；此时改走 `GameItem` 重载，让货物同样经过上面的销毁拦截进入货架。玩家自己的物品（`isOwend`）保持原版行为。
- 状态同步：
  - `StockPageHelper.HasHeldStock` 后缀：传入当前顾客且货架上仍有未购货物时返回 `true`，防止逐件买空柜台后只卖货的顾客提前离开；
  - `StockPageHelper.GetHeldItems` 后缀：传入当前顾客时追加货架上的未购货物，让违禁品/赃物判定与举报奖励同步覆盖货架；
  - `EmporiumEntry.GetAllNonOwendItemCount` 后缀：在 `WholesaleDepth == 0` 时加上货架上未购货物数量，避免仅剩货架有货时批发按钮隐藏或误判结束。
- 清除时机：`PlayerStore.RemoveNotOwnedItemFromTable`、`EmporiumEntry.ClearFrontInv`、`EmporiumEntry.ClearFrontInvNonOwned` 以及 `EmporiumEntry.RemoveNonOwnedItem`（仅无当前顾客时）后缀。非己方货物先 `GunHelper.ReturnPlayerParts` 再销毁；`ClearFrontInv*` 路径与原版一样计入柜台上遗留的核废料。
- 批发：`NegociationUIManager.OnWholesaleClick` 与 `PlayerStore.GetWholesalePrice` 执行期间，`EmporiumEntry.GetAllNonOwnedItem` 后缀把货架上的非己方货物追加到结果（以物易物列表等其余调用不受影响）；批发后缀把已购物品用 `GraphUtils.EmporiumTryAdd` 从后台开始移出货架。
- 窗口使用 `WindowStyle.Machinery`，标题拖动与高度调整由模组处理，`canMove=false` 使其不参加 Esc 关闭队列与浮动窗口数量上限，但仍保留在 `WindowsHandler.visibleWindows` 中供智能排列识别；`PixelWindow.Validate` 后缀只对货架恢复右下角调整手柄。窗口设为 `isImportant`，关闭按钮隐藏，装饰关闭射线检测，位置继续用世界坐标换算。
- 窗口每次由隐藏变为显示时调用 `ToFront(force: true)`，避免原版把 `canMove=false` 窗口放在底层、展示柜等容器的物品盖住货架；显示期间不持续抢占窗口顺序。
- 库存直接继承 `GameGridInventory`，不再使用隐藏整理视图。物品的 `parentInventory`、菜单目标和可见格子是同一个对象，满足智能排序的归属、快照、提交及撤销校验；滚动组件不持有货物、不增加存档节点，也不需要修改第三方排序算法。
- 标题栏、边框与右下角调整手柄使用透明命中区域，只有 Unity 将拖动交给货架时才开始移动或缩放，避免与其他重叠窗口一起移动。格子与滚动条保留自己的鼠标事件，拖动期间仍由 `ShelfRunner` 更新位置或行数。
- 放回货架：离开谈判柜台和谈判区的原版处理（`RemovedItemToClientArea`、`RemovedItemToWeightedArea`）挂在来源容器的移除事件上，放回货架时照常触发。
- 快捷转移：仅在 `ItemQuickTransferHandler.OnEventRelease`（默认 Ctrl）与 `ItemQuickTransferAltHandler1.OnEventRelease`（默认 Alt）中接管货架、谈判区的未购物品，复用原版按下状态、改键、松开时命中校验、音效及输入重置。通过 `GraphUtils.TryAcceptOnce` / `TryAcceptAll` 转移，保留原生容器事件与数量、归属校验；角色交易的 `not_purchased` 物品和其他来源仍走原版。Shift 不额外接管。
- 窗口由常驻的 `ShelfRunner` 在 `LateUpdate` 中维护显隐、标题件数与位置；场景切换后旧窗口随场景销毁，下次溢出时重建。

## 局限

- 货架不在原版存档的物品清单里。原版不会在交易中途存档；用手动存档类模组在交易进行中存档时，货架上的货物不会被保存，与原版销毁的结果相同。
- 部分顾客与剧情只在货物进入柜台时触发反应，货架上的货物在拖上柜台后才会触发；货物在柜台与货架之间来回挪动时，这些进出柜台的原版处理会重复执行，与在柜台和谈判区之间来回挪动相同。
- 自动接货处理原版翻页溢出以及以 `front counter full` / `does not fit the front counter or a stock page` 销毁的货物；另支持使用 Alt/Ctrl 从谈判区退回、前柜台放不下的未购物品。与上述操作无关的其他模组或原版 `EmporiumTryAdd` 等放置失败路径不受影响。
- 与「柜台增大，顾客带来的东西全显示」（`probablystolen.counterexpand`，工坊 3834275644）、「供货清单」（`supply.list`，工坊 3835115070）、「商人货物翻页」（工坊 3835147893）、「杰克逊博士商店翻页」（工坊 3831077199）功能重叠，建议只保留其一；同时启用「供货清单」时会在日志里提示。
