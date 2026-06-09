# TodoSnap

把一张截图，变成一句待办。

TodoSnap 是一个极致轻量的 Windows 桌面小工具：截图 → 粘贴 → 自动提炼成一句待办 →
钉在屏幕侧边的悬浮窗里，点一下圆圈即完成（带淡出动画）。常驻系统托盘，离线可用。

---

## 功能

- **粘贴即分析**：在主窗口 `Ctrl+V` 粘贴截图（或拖入图片文件），自动提炼一句待办。
- **离线 OCR**：默认使用 Windows 内置 OCR（`Windows.Media.Ocr`），无网络、无第三方依赖。
- **在线 AI（可选）**：放置 `apikey.txt` 或在设置里填写 Key 后优先调用 OpenAI 兼容 Vision 接口，失败/超时自动回退 OCR。
- **悬浮待办栏**：始终置顶、不抢焦点；可拖拽四边自由缩放，可拖到屏幕边缘自动吸附为"贴边细条+数字徽标"，点细条平滑展开。
- **添加反馈**：贴边状态下添加新待办会自动 peek 展开短显新内容，5–6 秒无操作后自动收回贴边。
- **一键完成**：点击圆形按钮 → 0.3s 淡出 → 从列表与 JSON 中移除。
- **全局快捷键**：默认 `Alt + \``，托盘隐藏时也能呼出主窗口；设置内可自定义并检测冲突。
- **设置主界面**：深/浅主题切换、背景透明度滑块、快捷键录制、API Key 管理、开机自启、浮窗显隐与重置。
- **系统托盘**：左键显示主窗口；右键菜单（显示主窗口 / 设置 / 显示隐藏待办栏 / 退出）。
- **本地存储**：`%AppData%\TodoSnap\tasks.json`、`config.json`、可选 `apikey.txt`。
- **单实例**：通过 Mutex 防止重复启动。
- **高 DPI**：清单 Per-Monitor v2 DPI 感知。

## 运行环境

- Windows 10 1809 (17763) 及以上 —— Windows OCR 需要安装对应语言的"可选功能 → 文本识别 (OCR)"。
  中文识别请在「设置 → 时间和语言 → 语言 → 中文 → 选项」中确认已安装"文本识别"。
- **.NET 10 SDK**（开发/编译）：https://dotnet.microsoft.com/download

## 项目结构

```
TodoSnap/
├─ TodoSnap.sln
├─ TodoSnap.csproj
├─ app.manifest                 # Per-Monitor v2 DPI
├─ App.xaml / App.xaml.cs       # 入口：单实例、托盘、主题、热键、窗口装配
├─ MainWindow.xaml(.cs)         # 粘贴 / 提炼 / 添加 + 齿轮 → 设置
├─ FloatingWindow.xaml(.cs)     # 悬浮待办栏：缩放 / 吸附 / 贴边细条 / 添加反馈
├─ SettingsWindow.xaml(.cs)     # 设置主界面：外观 / 快捷键 / API / 系统
├─ Themes/
│  ├─ Dark.xaml                 # 深色主题画刷令牌
│  └─ Light.xaml                # 浅色主题画刷令牌
├─ Models/
│  ├─ TaskItem.cs
│  └─ AppConfig.cs              # 含主题/透明度/热键/浮窗尺寸与吸附边
├─ Services/
│  ├─ AnalysisService.cs        # OCR + 在线 Vision，含自动回退与 Key 写入
│  └─ DataService.cs            # JSON 读写（FileShare.None 加锁）
├─ Helpers/
│  ├─ ImageHelper.cs            # BitmapSource ⇄ SoftwareBitmap / PNG
│  ├─ WindowInterop.cs          # 悬浮窗 NOACTIVATE，不抢焦点
│  ├─ ThemeManager.cs           # 运行时切换主题字典
│  ├─ HotkeyService.cs          # 全局热键（RegisterHotKey / WM_HOTKEY）
│  ├─ TrayIconFactory.cs        # 运行时绘制托盘图标（无 .ico 也能跑）
│  └─ StartupHelper.cs          # 开机自启（注册表 Run 键）
├─ Resources/
│  ├─ app.ico                   # 已生成；可用 gen_icon.py 重新生成
│  └─ gen_icon.py
└─ apikey.txt.example           # 在线模式配置示例（仅作模板，新版优先写到 AppData）
```

## 编译与运行

```bash
# 在 TodoSnap 目录下
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

## 发布为单个 .exe

> **关于体积**：计划要求安装包 < 5 MB。该体积只有在
> **框架依赖（framework-dependent）** 模式下可达（前提：目标机已安装
> ".NET 10 Desktop Runtime"）。**自包含（self-contained）** 的单文件 WPF 约
> 80 MB（压缩后），无法 < 5 MB。两种命令都给出，按需选择。
>
> **关于裁剪**：WPF 与 `PublishTrimmed=true` 不完全兼容，容易在运行时抛
> `MissingMethod`/XAML 反射异常，因此默认 **不启用裁剪**。

**A. 框架依赖单文件（最小体积，约 1–3 MB；需目标机装 .NET 10 Desktop Runtime）**

```bash
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

**B. 自包含单文件（免安装运行时，体积大，约 80 MB）**

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:EnableCompressionInSingleFile=true
```

产物位于：
`bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/TodoSnap.exe`

## 使用流程

1. 用系统快捷键 `Shift + Win + S` 截图（图片进入剪贴板）。
2. 按全局热键 `Alt + \`` 或点托盘图标 → 主窗口弹出。
3. 按 `Ctrl+V` 粘贴，等待"OCR 识别中 / AI 分析中"，结果填入编辑框，可修改。
4. 回车或点「添加待办」→ 待办出现在侧边悬浮窗。
5. 点击待办左侧圆圈 → 淡出消失。
6. 拖浮窗到任意屏幕边缘 → 自动收为贴边细条带数字徽标；点细条 → 平滑展开还原。

## 设置主界面

主窗口右上角齿轮 ⚙ 或托盘右键 → "设置"打开：

- **外观**：浅色主题开关；背景透明度（0.6–1.0），只影响背景层，文字始终清晰。
- **快捷键**：点"录制" → 按下新组合键即生效；冲突或被占用时红字提示并保留原键。
- **在线 AI**：API Key / Endpoint / Model 三个输入框，留空 Endpoint/Model 走默认。
- **系统**：开机自启、显示待办栏、重置浮窗位置/尺寸。

## 在线模式（可选）

两种配置方式，任选其一：

1. **推荐**：在"设置 → 在线 AI"里填入 Key 并保存（写到 `%AppData%\TodoSnap\apikey.txt`）。
2. 兼容旧版：复制 `apikey.txt.example` 为 `apikey.txt`，放在 `TodoSnap.exe` 同目录。

存在且非空即启用在线分析，5 秒超时或任何错误自动回退到离线 OCR。

## 已知取舍 / 说明

- 主窗口与设置窗口采用「半透明 + 圆角」实现轻量玻璃质感（计划允许的简化方案），未使用
  第三方 Acrylic 库，以保持零依赖。
- 透明度只作用于"背景层"，文字与控件始终全不透明，避免可读性下降。
- 完成的待办默认直接从 `tasks.json` 删除；`TaskItem.Done` 字段已保留以便将来
  改成"标记完成"而非删除。
- 托盘图标在运行时绘制，因此即使缺少 `app.ico` 也能正常运行；存在 `app.ico`
  时优先使用它（更清晰、多分辨率）。
