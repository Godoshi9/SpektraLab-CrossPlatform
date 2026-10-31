# 构建 Windows / Android

两端直接打包 `ui/` 中的同一份文件，调用 `native/bridge.cpp` 和上游 Vulkan 引擎。

## Windows 主机依赖

- Visual Studio 2022 Build Tools：Desktop development with C++、Windows SDK、CMake / Ninja。
- .NET 8 SDK；发布时打包固定的 runtime **8.0.33**。
- JDK 17；Git；PowerShell 7。
- Android SDK：`platforms;android-34`、`build-tools;36.0.0`、NDK **30.0.16248370**。
- 可选 Inno Setup 6，制作 Windows 安装器。

脚本读取 `ANDROID_HOME` 或 `%LOCALAPPDATA%/Android/Sdk`，通过 `vswhere` 查找 Visual Studio，不修改全局 PATH。

```powershell
# 从仓库根目录执行，自动下载并校验 LibRaw 和 Vulkan-Headers。
./crossplatform/scripts/build.ps1 -Platform all -RunTests
./crossplatform/scripts/build.ps1 -Platform windows -RunTests
./crossplatform/scripts/build.ps1 -Platform android
# 包含 x86_64 模拟器库。
./crossplatform/scripts/build.ps1 -Platform android -Emulator
```

需要代理时，在当前进程配置 `HTTPS_PROXY` / `HTTP_PROXY`，或配置 Git 代理。NDK 的宿主 `glslc` 编译两端相同的 Vulkan 1.0 shaders，没有开启 fast-math 或更改上游物理资料。

## 产物与签名

- `build/app-windows/`：完整 Windows 免安装目录。
- `crossplatform/android/app/build/outputs/apk/debug/app-debug.apk`：包名为 `app.spektralab.mobile.debug` 的 Debug APK。
- `crossplatform/android/app/build/outputs/apk/release/`：未配置签名时为 unsigned release。
- `dist/`：本地发布的安装器、ZIP、签名 APK。

```powershell
./crossplatform/scripts/sign-android.ps1
& "$env:LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe" crossplatform/windows/installer.iss
Compress-Archive -Path build/app-windows/* -DestinationPath dist/SpektraLab-0.1.1-Windows-x64-Portable.zip -Force
```

首次签名会在 **`crossplatform/.signing/`** 创建发布密钥及配置，后续重复使用。请私下备份整个目录，以便覆盖升级；该目录及密钥文件已被 Git 忽略。CI 不持有密钥，上传 Debug APK 和 Windows 构建产物。

项目的 Gradle daemon 已关闭，避免一次性打包完成后继续常驻占用内存。Windows 0.1.1 是导入内存修复，Android 已发布 APK 保持 0.1.0。

## 验证

`-RunTests` 执行原生 CTests 与 Windows 导出自测，需要可用的 Vulkan GPU。GitHub Windows runner 只构建，不宣称已验证图像正确性。

```powershell
./build/app-windows/SpektraLab.exe --self-test D:/SpektraLab-test
# self-test 先生成 test-scene.png；再驱动真实 WebView 和 native bridge。
./build/app-windows/SpektraLab.exe --ui-test D:/SpektraLab-test
# 大图连续导入、内存自适应及 PNG / 失败保留测试。
./build/app-windows/SpektraLab.exe --memory-test D:/SpektraLab-memory-test
# Debug APK 的显式验证入口，release 忽略此参数。
adb shell am start -n app.spektralab.mobile.debug/app.spektralab.mobile.MainActivity --ez self_test true
adb pull /sdcard/Android/data/app.spektralab.mobile.debug/files/validation
```

UI 自测检查导入、调节、分割对比和 390 CSS px 布局。Android 自测调用 APK 内的 JNI / Vulkan 库，校验原图、明暗编辑、确定性恢复和 PNG 输出。

## 行为约定

- Native handle 只能在串行队列使用；新图像解码、建会话、渲染全部成功后才替换旧图像。
- Windows 跨平台构建保留至少 1 GiB 的系统 commit 余量；上游独立桌面构建仍使用 3 GiB。导入前按 commit / 物理内存余量预算工作尺寸，变化期间分配失败可继续缩小重试；每次操作结束释放闲置 scratch / staging，保留当前源图与效果。预算只是预估，每次 Vulkan 分配仍有保护。RAW 原始解码峰值不由工作尺寸限制保证。
- 共享 UI 合并连续调节，仅发布最新请求；导入、导出时锁定冲突操作。关闭等待工作结束，Android 旋转屏幕保留会话。
- Native RGBA16 统一转换为 ARGB8，预览与导出使用同一像素。当前输出不携带源 EXIF。
- Windows 使用系统图像解码和 EXIF 方向处理；Android ImageDecoder 限制尺寸并转到 sRGB。RAW 为上游兼容解码、线性 ProPhoto 输入。
- 本地 origin 只提供打包 UI。Android 不申请网络权限；禁止远程导航和页面指定任意读写路径。
- Windows 先编码临时文件再提交，拒绝覆盖当前源照片。Android 先在缓存编码，再写入用户选择的文档提供方；提供方写入中断可能留下不完整目标，应用会报告错误。

完整功能边界见根 README。上游 macOS 文档保留用于研究，不代表本版本已实现全部上游功能。
