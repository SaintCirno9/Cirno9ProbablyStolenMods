# 游戏原生 ModLoader 说明

基于游戏 `Assembly-CSharp` 反编译结果（`GameSource/Assembly-CSharp/ModLoader.cs` 等）整理，游戏版本以 `manifest.xml` 的 `GameVersions` 写法为准（例如 `1.0.0.*`，旧版模组常见写法为 `0.46D`）。游戏更新后先运行 `Scripts/Update-GameSource.ps1` 再核对本文。

## 模组来源与目录

| 来源 | 目录 | 说明 |
| --- | --- | --- |
| Local | `<GameRoot>\Mods\<文件夹>\` | `ModLoader.LoadAllMods()` 扫描每个子目录 |
| Workshop | `<WorkshopRoot>\<WorkshopId>\` | `ModLoader.LoadWorkshopMods()` 读取已订阅条目 |

每个模组目录读取：

| 文件 / 目录 | 用途 |
| --- | --- |
| `manifest.xml` | 必需，模组身份与程序集清单 |
| `preview.png` | 模组列表预览图 |
| `Textures/`（含 `Textures/Items/`） | 贴图与物品精灵 |
| `Localization/<locale>.csv` | 模组自有本地化，由 `ModHelper.GetLocalized(modId, key)` 读取 |
| `Sounds/` | 音效与音乐 |
| `Books/` | 纸张 / 书籍定义 |
| `LootTables/` | 掉落表 |

## manifest.xml

```xml
<?xml version="1.0" encoding="utf-8"?>
<Manifest>
  <ID>cirno9.example</ID>
  <Name>Example</Name>
  <Author>SaintCirno9</Author>
  <ModVersion>1.0.0</ModVersion>
  <Description>示例。</Description>
  <GameVersions>
    <Version>1.0.0.*</Version>
  </GameVersions>
  <Prerequisites />
  <Assemblies>
    <Assembly>Example.dll</Assembly>
  </Assemblies>
  <Capabilities />
</Manifest>
```

- `ID`：唯一标识，同时决定 Harmony owner `mod.<ID>`、`ModConfigs/<ID>.json` 等。
- `Prerequisites`：依赖的其他模组 `ID`。
- `Assemblies`：只填文件名；未列出的 DLL 会被记为 unlisted。
- `Capabilities`：声明模组使用的敏感能力，已见取值有 `files`、`dynamic-code`、`reflection-external`、`steam`。加载器会扫描程序集得到 detected 能力并与声明比对，结果记入 `SaveFile.es3` 的 `modTrust` 与 `Player.log` 的 `[ModLoader] audit:` 行。

### 仓库反射限制

上面的能力列表用于解释扫描结果，不是推荐权限清单。为兼容 `reflection-external` 即将下线，本仓库新增或维护的模组不得依赖或声明该能力，也不能只删除声明、修改信任记录或绕过扫描来消除告警。

这不等于禁止所有反射：优先使用公开 API 与 `ModHook`，确需反射时限定到已核对的固定游戏/模组类型及成员，并由当前游戏原生 `ModScanner` 验证最终 DLL。运行时任意类型搜索、`dynamic` 和系统/框架目标访问不能假定安全。最终是否合规以当前游戏原生 `ModScanner` 或游戏内 F8 控制台 `mod-scan <ModID>` 的扫描结果为准。

## 加载流程

`ModLoader.Awake()` 依次执行 `LoadModOrder` → `LoadAllMods`（Local）→ `LoadWorkshopMods` → `ApplySavedOrder` → `ValidatePrerequisites` → `LoadModAssets` → `LogAudit`。单个程序集在 `LoadModDll` 中：

1. 拒绝与游戏 / 框架同名的程序集。
2. `Assembly.LoadFrom` 后查找唯一的 `IMod` 实现并实例化。
3. `new Harmony("mod." + ID).PatchAll(assembly)`。
4. `mod.Init(manifest)`，随后 `mod.OnEnable()`；任一步异常即记为 `Init failed`，模组不运行。

以上全部同步发生在 `Awake` 内，早于游戏的 Localization 初始化（`LocSmartStringHelper` 在 `AfterSceneLoad` 才启动 `LocalizationSettings.InitializationOperation`）。

## 本地化初始化陷阱

在 `Init` / `OnEnable` / 补丁静态构造中读取 `LocalizationSettings.SelectedLocale`（含 `LocHelper.GetCurrentLocaleCode()`、`ModHelper.GetCurrentLocale()`、`ModHelper.GetLocalized()`）时，`LocalizationSettings.GetSelectedLocale()` 会在 Localization 未初始化的情况下对 `AvailableLocales` 预加载链执行同步 `WaitForCompletion()`。之后 Localization 内部的 Addressables 操作链出现已失效的句柄，游戏本体与所有后续模组调用 `LocalizedStringDatabase.GetLocalizedString` / `GetTable` 时抛出：

```text
Exception: Attempting to use an invalid operation handle
  at UnityEngine.ResourceManagement.AsyncOperations.GroupOperation.CompleteIfDependenciesComplete ()
  ...
  at LocHelper.Get (System.String table, System.String key, System.Object[] arguments)
```

典型连锁现象：

- 依赖 `ItemConditionList`、`Liquid`、`WaterFeatureHelper`、`ModuleEffectHelper` 等静态构造会调用 `LocHelper` 的游戏类型，其他模组在 `PatchAll` 或 `Init` 时出现 `TypeInitializationException`，加载器报 `Init failed`，模组 `running=False`。
- 主菜单、选项回调等游戏 UI 报 `[LocHelper] ... threw while formatting` 或 `[GameOption] Option callback threw`。

常见触发写法：在 `Init` 中调用 `ModConfig.Define(..., GetLocalized("..."), ...)` 时，`section` 参数会立即求值并间接读取 `SelectedLocale`，从而触发上述问题。

规避方式：

- 加载期只使用常量文本；随语言变化的设置文本放进 `Func<string>` 的 `label` / `tooltip`。
- 需要语言信息时，延迟到 `ModHook.OnGameLoadedLate` 等游戏回调、`LocalizationSettings.SelectedLocaleChanged` 事件，或在协程中 `yield return LocalizationSettings.InitializationOperation` 之后再读取。
