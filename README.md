# 星轨重构 · Android Gradle 发布工程

本仓库用于 PICO VibeLab 云端构建，仅包含 Unity 导出的 Android Gradle 工程、必要发布配置、构建说明和许可文件。Unity 开发源工程单独维护。

- 应用名称：星轨重构 / ORBITAL FORGE
- 包名：`com.yangel.orbitalforge`
- 版本：`0.4.4`
- 平台：PICO Shared Space / Spatial Adapter，ARM64
- 工具链：Java 17、Android SDK / Build Tools 36、Gradle wrapper 9.3.1、AGP 9.0.0

## 构建

Windows：

```powershell
./gradlew.bat --no-daemon assembleRelease
```

Linux / macOS：

```sh
./gradlew --no-daemon assembleRelease
```

输出：`launcher/build/outputs/apk/release/launcher-release.apk`。

原生代码与 Spatial 资源在 Unity 开发环境中预先导出并验证。云端直接使用已验证的预编译库，不再执行 IL2CPP 编译或原生库去符号；无需安装 Unity、Node 或 Spatial Editor。
`unityLibrary/src/main/assets/` 是 APK 必需的运行资源。

## VibeLab

`vibelab.yaml` 保留已有应用关联及远端素材引用，`.vibelab/` 为忽略的本地发布状态。

```sh
pico-release-cli-cn --json add
pico-release-cli-cn --json commit
# 核对返回的预览页和协议确认后提交。
pico-release-cli-cn --json push --wait
```

修改玩法、场景、包名、版本或 SDK 后，须在独立 Unity 开发工程中重新导出，并重新验证原生库及 Spatial 资源。

## 许可

Noto 字体许可见 `LICENSES/NotoSansCJK-OFL.txt`。其他 SDK 原生依赖保留在导出 AAR / JAR 中，使用时遵循各自许可。
