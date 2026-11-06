# VibeLab：Shared Space Android 工程交付

## 本次故障

旧提交把 Unity 源码直接作为云端构建根目录。VibeLab 在根目录执行
`assembleRelease`，但该目录没有 `settings.gradle`，因此失败。

发布工具 `pico-release` 的 `add` 只校验配置、素材与 Git，不负责 Unity 导出。
完整的 `release-to-vibelab` 编排有 Unity 检测和导出步骤；此前安装的旧版插件
没有此技能，实际上传绕过了导出。新脚本的 PXR Loader 预检也不适用于本游戏的
Spatial Adapter，不能通过切换为 PICO XR 来处理。

## 当前工程

- Unity 6000.3.25f1，PICO Shared Space / Spatial Adapter。
- Android IL2CPP，ARM64；包名 `com.yangel.orbitalforge`，版本 `0.4.4`。
- 入口 `com.unity3d.player.SpatialAdapterPlayerActivity`，体积容器 style 2。
- Unity 源码与导出的 Gradle 工程共同保留在仓库中；云端只运行根目录 Gradle。
- Gradle 9.3.1 wrapper，AGP 9.0.0，Java 17，Android SDK / Build Tools 36。

## 重新生成

1. 在原 Unity 工程调用 `SpaceBall.Editor.OrbitalAndroidBuild.ExportForVibeLab()`，
   或使用菜单 `SpaceBall > Export Shared Space Gradle for VibeLab`。
   输出 `Builds/SpaceBall/VibeLab-Gradle`。入口检查 Shared Space 配置，使用 Release
   选项，并保留项目的 Spatial GLB 优化与官方 bundle 打包钩子。
2. 使用该 Unity Editor 附带的 Java 和 Gradle，在导出目录运行
   `:unityLibrary:buildIl2Cpp --rerun-tasks`，然后运行 `assembleRelease`。
   先验证原始 Release APK，保留为前置对照。
3. 在导出目录生成匹配的 Gradle wrapper。9.3.1 的官方 distribution SHA256：
   `b266d5ff6b90eada6dc3b20cb090e3731302e553a27c5d3e4df1f0d76beaff06`。
4. 从 Unity 工程根运行：

   ```powershell
   python Tools/Prepare-VibeLabSharedSpace.py `
     --export-root Builds/SpaceBall/VibeLab-Gradle `
     --output Builds/SpaceBall/VibeLab-Prepared
   ```

   输出必须是新目录。脚本保留已验证 Release APK 中的最终原生库，移除 C++
   中间产物、本机路径、缓存、签名文件与 APK，禁用再次编译/去符号，生成
   `.vibelab-prebuilt.json`。不会使用 LFS、压缩或拆分原生库。
5. 在处理后的目录使用 wrapper 执行 `assembleRelease`。用全新依赖缓存、空 Maven
   local 和仅含 Java/系统工具的 PATH 验证；不得依赖 Node、Unity 或 Spatial Editor。
6. 使用 `Tools/Validate-VibeLabSharedSpace.py` 对照前后 APK，传入
   `--expected-package com.yangel.orbitalforge`。要求名称、版本、Spatial 入口和容器
   元数据正确，35 个 Spatial 文件逐个 SHA 一致，18 个 ARM64 原生库逐个 SHA 一致。
   同时使用导出工作流的 APK 校验器检查 ELF 与 zipalign。
7. 只把处理后的 Gradle 输入同步到发布仓库，保留 `.git`、`vibelab.yaml`、
   `.vibelab/`、用户已确认的素材和 Unity 源码。不可调用会清空目标仓库的通用镜像步骤。

空间资源、程序集、场景、SDK、包名或版本发生变化后，应重新执行完整导出及前后比对。
生成 APK 成功与云端构建成功是不同证据；云端状态必须从 Release `state` 单独核实。

## 本次导出的额外处理

- Spatial Adapter 的 library Manifest 引用了 launcher 的图标和名称资源；
  library 与 launcher 共享现有资源目录，使根目录 `assembleRelease` 的库资源检查通过。
- 一些源原生库包含待移除符号，独立环境缺少对应 NDK 时 Gradle 会原样打包。
  因此取已经验证的第一份 Release APK 的最终库作为固定输入，并阻止再次 strip。
  AAR 内的另外 10 个库也与对照 APK 逐项核对，不使用 `pickFirsts` 掩盖冲突。
- PICO 部分第三方库仍使用 4 KB ELF LOAD 对齐。本流程记录该事实，不宣称全面兼容
  16 KB 页面设备。真机性能、正式签名和完整商店验收另行完成。

## 发布

保留现有应用 ID 和网页编辑过的素材/信息，先提交、推送并核对完整 Git SHA，再运行：

```powershell
pico-release-cli-cn --json add
pico-release-cli-cn --json commit
# 核对 CLI 返回的预览页，并完成所需协议确认。
pico-release-cli-cn --json push --wait
pico-release-cli-cn --json state --pending
```

Cloud clone、构建、提交和审核状态分别报告；`add` 或 `commit` 成功不代表 APK 构建通过。
