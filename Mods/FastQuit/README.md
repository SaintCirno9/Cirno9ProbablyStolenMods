# 退出游戏加速 / Faster Exit

- ID：`cirno9.fast_quit`
- 作者：SaintCirno9

## 功能

退出游戏时照常调用每个模组的 `OnDisable`，跳过原版加载器逐模组撤销 Harmony 补丁以及各模组在 `OnDisable` 中自行撤销补丁的步骤，消除装大量模组时的退出卡顿。

## 原因

原版 `ModLoader.OnApplicationQuit` 对每个模组执行 `OnDisable` 后调用 `Harmony.UnpatchAll(id)`。游戏自带 Harmony 2.4.2 的 `UnpatchAll` 对该 owner 的每个补丁单独调用 `Unpatch`，每次都会重新生成并编译目标方法的替换体，剩余的 Transpiler 也会全部重跑。约 90 个模组、3000 余个补丁时，这一步实测约 24 秒。进程随后就会结束，撤销补丁没有实际作用。

## 实现

游戏的模组扫描禁止补丁或反射 `ModLoader` 等加载器类型（`loader-target`），因此不改加载器本身，而是在 Harmony 一侧处理：

- `OnEnable` 订阅 `Application.wantsToQuit`，记录退出请求所在帧号，始终返回 `true`，不干预退出。
- 前缀 `Harmony.UnpatchAll(string)`、`Harmony.Unpatch(MethodBase, HarmonyPatchType, string)`、`Harmony.Unpatch(MethodBase, MethodInfo)`：在该帧之后 5 帧内跳过，owner 为空或以 `dse.`（加载器自身）开头时放行。`UnpatchSelf()` 内部调用 `UnpatchAll(Id)`，同样覆盖。
- 既覆盖加载器对 `mod.<ID>` 的撤销，也覆盖模组在 `OnDisable` 里用自己的 owner 撤销（实测深空爱回收、更好的性能优化、酿酒与睡觉兼容补丁等每个 0.4–1.2 秒）。
- 加载器 `OnApplicationQuit` 中的逐模组循环是同步执行的，帧号不变，整段都在窗口内。加载器照常对每个模组调用 `OnDisable`，`ModSteamAuth.CancelAll()` 也照常执行。
- 相邻两次 `UnpatchAll("mod.<ID>")` 的间隔即该模组 `OnDisable` 的耗时，超过 100 ms 时写日志，便于定位剩余的慢模组。

## 注意与风险

- 模组停用后补丁保留到进程结束。剩余退出过程中若游戏调用到已停用模组的补丁，补丁会在模组已清理的状态下运行，可能在 Player.log 留下报错。原版退出不存档，实测未见报错，但不能保证所有模组。
- 退出判断依赖 `Application.wantsToQuit`：排在本模组之后的处理器取消退出时，之后 5 帧内的撤销也会被跳过；不经过正常退出（如结束进程）时不生效，也无副作用。
- 依赖游戏自带 Harmony 2.4.2 的 `UnpatchAll` / `Unpatch` 签名；更新后可能失效或被扫描拒绝。`UnpatchCategory`、直接调用 `PatchProcessor.Unpatch` 未拦截。
- 扫描器禁止补丁加载器类型，本模组改在 Harmony 一侧处理；若日后扫描器把 HarmonyLib 列入禁止目标，本模组将无法激活。
- 不加速：开启数据统计授权时原版退出前等待 1 秒；Unity 自身关闭约 1 秒；`OnDisable` 中撤销补丁以外的工作。
