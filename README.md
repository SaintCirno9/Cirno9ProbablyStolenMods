# Cirno9 Probably Stolen Mods

SaintCirno9 维护的《Probably Stolen》（深空当铺）原生模组开发仓库。源码与游戏运行目录分离，构建后自动部署到游戏本地 `Mods` 目录。

## 已发布模组

| 模组 | 模组 ID | 简介 | Steam 创意工坊 |
| --- | --- | --- | --- |
| [自动售货机增强](Mods/VendingMachineEnhancement/) | `cirno9.vending_machine_enhancement` | 为自动售货机与自动售水机补齐改装模组仓、扩容上限翻倍至 14×16 并修复窗口位置记忆 | [3843312107](https://steamcommunity.com/sharedfiles/filedetails/?id=3843312107) |
| [滑条数字输入](Mods/SliderNumericInput/) | `cirno9.slider_numeric_input` | 点击选项与模组配置滑条旁的数值直接原地输入数字 | [3841350358](https://steamcommunity.com/sharedfiles/filedetails/?id=3841350358) |
| [框选批量打开容器](Mods/BatchOpenContainers/) | `cirno9.batch_open_containers` | 左键框选多个容器或机器后一键全部打开，支持记忆位置与平铺/层叠排列 | [3839958730](https://steamcommunity.com/sharedfiles/filedetails/?id=3839958730) |
| [提示框自适应缩放](Mods/TooltipAutoScale/) | `cirno9.tooltip_auto_scale` | 悬停提示框超长时先适度加宽折行，仍超高或过宽时自动等比缩小并防溢出屏幕 | [3839077271](https://steamcommunity.com/sharedfiles/filedetails/?id=3839077271) |
| [投影仪展示区](Mods/ProjectorShowcase/) | `cirno9.projector_showcase` | 为幻影投影仪增加可调尺寸的独立展示区面板，生效时计入展示柜 | [3837855513](https://steamcommunity.com/sharedfiles/filedetails/?id=3837855513) |
| [柜台扩容货架](Mods/CounterOverflowShelf/) | `cirno9.counter_overflow_shelf` | 接管溢出货物与原版翻页，将摆不下的货物放进柜台旁可滚动扩容货架 | [3836730459](https://steamcommunity.com/sharedfiles/filedetails/?id=3836730459) |
| [退出游戏加速](Mods/FastQuit/) | `cirno9.fast_quit` | 退出游戏时跳过逐个撤销 Harmony 补丁，消除大量模组下的退出卡顿 | [3834715728](https://steamcommunity.com/sharedfiles/filedetails/?id=3834715728) |
| [模组快捷键汇总](Mods/ModKeybindHub/) | `cirno9.keybind_hub` | 按模组拆分选项中的快捷键并镜像模组配置键位，支持冲突标注与右键清空 | [3834652943](https://steamcommunity.com/sharedfiles/filedetails/?id=3834652943) |
| [势力声望面板重制](Mods/ReputationInterfaceRemake/) | `cirno9.reputation_interface_remake` | 用可滚动的横向行式界面替代原版势力档案，兼容多个主流势力扩展模组 | [3834158985](https://steamcommunity.com/sharedfiles/filedetails/?id=3834158985) |

## 目录

| 目录 / 文件 | 用途 |
| --- | --- |
| `Mods/<模组名>/` | 独立工程、源码、`manifest.xml` 与运行资源 |
| `Scripts/` | 游戏源码反编译、SteamCMD 安装与创意工坊发布脚本 |
| `Tools/SteamCMD/` | 仓库内的 Valve 官方 SteamCMD，不提交 |
| `Docs/` | 游戏 ModLoader 说明与长期开发规范 |
| `GameSource/` | 游戏程序集反编译结果，本地保留，不提交 |
| `External/` | 第三方模组参考源码，本地保留，不提交 |
| `Artifacts/bin/<模组名>/<配置>/` | 构建产物，不提交 |
| `Artifacts/Workshop/<模组名>/` | 创意工坊上传目录与 `item.vdf`，不提交 |
| `.cache/` | 临时记录、备份与缓存，不提交 |
| `Directory.Build.props/targets` | 公共目标框架、游戏引用、依赖校验与构建后部署 |
| `GamePaths.props` | 构建时读取 `.env` 的游戏路径 |
| `.env.example` | 本机配置模板；实际 `.env` 不提交 |

## 首次配置

```powershell
Copy-Item ".env.example" ".env"
```

在 `.env` 中填写游戏安装目录（包含 `Probably Stolen.exe` 的目录）：

```dotenv
PROBABLY_STOLEN_GAME_ROOT=
```

也可给构建传 `-p:GameRootDir=...`，给脚本传 `-GameRootDir ...`。

## 构建与部署

从仓库根目录执行，构建成功后默认自动部署：

```powershell
dotnet build "Mods/<模组名>/<模组名>.csproj" -c Release
```

仅构建，不修改游戏目录：

```powershell
dotnet build "Mods/<模组名>/<模组名>.csproj" -c Release -p:DeployMods=false
```

部署由 `Directory.Build.targets` 在构建后完成，目标为 `<游戏目录>/Mods/<模组名>/`：复制 `manifest.xml` 声明的 DLL、`manifest.xml`、`preview.png` 以及 `Textures`、`Localization`、`Sounds`、`Books`、`LootTables` 目录。本游戏运行期间通常不锁定本地 `Mods` 目录下的 DLL，一般可直接覆盖部署（重启游戏生效）；如遇文件占用报错，请先退出游戏后再重新构建。

## 发布到创意工坊

流程依据官方模组指南 `ModdingGuide.md` 的 “Publishing with steamcmd” 章节，使用拥有游戏的 Steam 账号。

首次安装 Valve 官方 SteamCMD 到 `Tools/SteamCMD/`（校验 Valve 签名，不提交；登录后该目录会缓存 Steam 凭据）：

```powershell
pwsh -NoProfile -File "Scripts/Install-SteamCmd.ps1"
```

每个模组的工坊元数据放在 `Mods/<模组名>/Workshop/` 并随仓库提交：

| 文件 | 内容 |
| --- | --- |
| `workshop.json` | `publishedFileId`（首次发布前为 `"0"`）、英文 `title`、其他语言标题 `titles`、可选标签 `tags`、`lastPublishedVersion` |
| `description.bbcode` | 英文描述，Steam BBCode，不能含双引号和反斜杠 |
| `description.<语言>.bbcode` | 其他语言描述，如 `description.schinese.bbcode`，与 `titles.<语言>` 成对出现 |

先预览上传内容，不登录也不上传：

```powershell
pwsh -NoProfile -File "Scripts/Publish-WorkshopMod.ps1" -ModName <模组名> -DryRun
```

正式发布（先提高 `manifest.xml` 的 `ModVersion`）：

```powershell
pwsh -NoProfile -File "Scripts/Publish-WorkshopMod.ps1" -ModName <模组名> -ChangeNote "更新说明"
```

脚本会：

- 以 `-p:DeployMods=false` 构建 Release，把声明的 DLL、`manifest.xml`、`preview.png` 和资源目录整理到 `Artifacts/Workshop/<模组名>/content/`，生成 `item.vdf`；
- 按游戏扫描规则拦下未声明的 DLL、脚本、可执行文件、压缩包、源美术文件和点开头的条目，并检查 `preview.png` 小于 1 MB、`GameVersions` 与 `Player.log` 中的游戏版本匹配；若存在小于 1 MB 的 `Workshop/preview.gif`，优先用作工坊主封面图；
- 提示完成游戏内 F8 控制台 `mod-scan <模组ID>` 等发布前检查并确认（`-Yes` 跳过）；
- 调用 SteamCMD 前经 `Scripts/SteamCmdRegion.ps1` 自动核对当前网络出口与 SteamCMD 区域配置，避免跨区连接超时；
- 已有条目先经本机 Steam 客户端（`ISteamUGC`）提交其他语言的标题、描述与标签，再调用 `steamcmd +login <账号> +workshop_build_item <item.vdf> +quit` 上传内容并写英文文案；SteamCMD 放最后是因为它会挤下线桌面 Steam；
- 账号依次取 `-SteamUser`、`.env` 的 `STEAM_USERNAME`、SteamCMD 登录缓存；密码与令牌验证码在 SteamCMD 中输入。`-Yes` 为非交互模式，要求登录缓存已存在，并隐去输出里的账号名；
- `-ChangeNote` 只写入 english，其他语言需在条目“改动说明”页点“编辑”手动补（脚本结束时会提示）；
- 成功后把条目 ID 与版本写回 `workshop.json`，需提交该文件，之后同一命令即更新原条目。

新条目默认以隐藏（`Private`，官方 visibility `2`）创建；可用 `-Visibility Public|FriendsOnly|Private` 指定，省略时更新不改变可见性。首次上传需在 Steam 网站接受一次创意工坊法律协议。SteamCMD 路径与账号也可在 `.env` 中用 `STEAMCMD_PATH`、`STEAM_USERNAME` 指定。

只改了 `description.bbcode` 或 `workshop.json` 的标题时，不必重传内容：

```powershell
pwsh -NoProfile -File "Scripts/Publish-WorkshopMod.ps1" -ModName <模组名> -MetadataOnly
```

`-MetadataOnly` 只更新已有条目的标题、描述（以及显式传入的 `-Visibility`），不构建、不上传模组文件和预览图，不检查也不改 `workshop.json` 的版本记录。依据是 Steamworks 文档：更新条目时 VDF 只写需要更新的键。

仅替换已有条目的主封面图（`preview.png` 或 `Workshop/preview.gif`）而不改动内容与文案时，可使用 `-PreviewOnly`：

```powershell
pwsh -NoProfile -File "Scripts/Publish-WorkshopMod.ps1" -ModName <模组名> -PreviewOnly
```

SteamCMD 与桌面 Steam 客户端共用账号会话，每次上传都会把客户端挤下线，上传结束后在客户端里重新连接即可。

发布后可回查工坊上的实际状态，逐语言对比标题与描述，并核对标签、可见性、内容大小与更新时间（只读，需桌面 Steam 已重新连接；有差异时退出码为 1）：

```powershell
pwsh -NoProfile -File "Scripts/Test-WorkshopItem.ps1" -ModName <模组名>
```

## 开发工具

```powershell
pwsh -NoProfile -File "Scripts/Update-GameSource.ps1"
```

使用 `ilspycmd` 将 `Assembly-CSharp`、`Assembly-CSharp-firstpass` 反编译到 `GameSource/`，旧结果先移入 `.cache/GameSource-backups/`。可用 `-Assembly` 指定其他程序集。

## 许可证

本仓库代码采用 [MIT License](LICENSE) 授权。

Copyright (c) 2026 SaintCirno9

本许可证不覆盖游戏《Probably Stolen》本体及其资源，也不覆盖任何第三方模组与素材。

## AI 辅助生成说明

本仓库的代码由 AI 编码代理生成，作者负责需求和维护。
