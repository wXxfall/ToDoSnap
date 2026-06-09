# 任务：创建 TodoSnap 应用

你是 C# 和 WPF 专家。请为我开发一个名为 **TodoSnap** 的 Windows 桌面应用，要求极致轻量、单文件运行。应用的核心功能是：将系统截图自动提炼为一句待办事项，并以悬浮窗形式展示，支持一键完成并消除。

## 一、总体目标
- 应用名：TodoSnap
- 核心流程：
  1. 用户使用 `Shift+Win+S` 截图（图片进入系统剪贴板）
  2. 在 TodoSnap 主窗口按 `Ctrl+V` 粘贴截图
  3. 应用自动分析截图内容，生成一句待办描述
  4. 用户确认/修改后，点击添加，该待办出现在屏幕右侧的悬浮小窗中
  5. 在悬浮窗点击事项前的圆形按钮，事项消失（淡出动画）
- 运行形态：系统托盘常驻，主窗口可隐藏，悬浮窗始终置顶显示
- 极致轻量：安装包 < 5 MB，内存占用 < 50 MB，无额外依赖，数据存本地 JSON

## 二、技术栈要求
- **.NET 8** 或 **.NET 9**，使用 **WPF** 框架
- 利用 Windows 内置 OCR（`Windows.Globalization.Ocr`）实现离线文字识别
- 可选集成 OpenAI / Azure OpenAI Vision API（在线时使用，离线自动回退到 OCR）
- 数据存储使用 **本地 JSON 文件**（路径：`%AppData%\TodoSnap\tasks.json`）
- 打包方式：生成单个可执行文件（`dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true`），最终交付一个 `.exe`
- 不得引入大型第三方库（如 Tesseract），只使用 .NET 内置能力和系统 API

## 三、功能需求详解

### 1. 主窗口（粘贴与提炼）
- 窗口样式：`WindowStyle="None"`, `AllowsTransparency="True"`, 圆角 `WindowChrome`, 可拖拽移动
- 尺寸：约 380x220，背景半透明毛玻璃效果（使用 `AcrylicBrush` 或 `BlurEffect`）
- 布局：
  - 顶部区域：图片预览框（显示当前粘贴的截图缩略图），未粘贴时显示提示文字“在此粘贴截图 (Ctrl+V)”
  - 中间区域：`TextBox` 或 `RichTextBox`，用于显示和编辑待办描述，绑定到 `TaskDescription`
  - 底部按钮：`添加待办`（默认按钮，回车触发）、`取消`（清空内容）
- 粘贴行为：
  - 监听 `Ctrl+V` 或拖放图片文件
  - 从剪贴板获取图片：`Clipboard.GetImage()`
  - 将图片显示在预览区，并自动触发分析

### 2. 智能分析模块
实现一个服务类 `AnalysisService`，根据当前网络状态和用户配置自动选择分析方式。

#### 离线 OCR 模式（默认，无网络回退）
- 调用 `Windows.Globalization.Ocr.OcrEngine`，对粘贴的图片进行文字识别
- 代码片段参考：
  ```csharp
  using Windows.Globalization.Ocr;
  using Windows.Graphics.Imaging;
  // 将 System.Drawing.Bitmap 转为 Windows.Graphics.Imaging.SoftwareBitmap
  // 然后 var engine = OcrEngine.TryCreateFromUserProfileLanguages();
  // var result = await engine.RecognizeAsync(softwareBitmap);
  // 拼接所有识别文字，取前 60 个字符，去掉换行，作为待办描述
  ```
  > 注意处理 DPI 和图片格式转换（可使用 BitmapDecoder）

#### 在线 AI 模式（可选，有网络且已配置 API Key）

- 配置方式：在应用同目录下读取 `apikey.txt`，若存在且内容非空，则启用在线模式
- 使用 `HttpClient` 调用 OpenAI Vision API（或兼容接口）
- Prompt 设计：

  ```text
  你是一个任务提炼助手。请用一句简洁的中文概括图片中需要处理的事项，直接返回这句话，不要任何前缀或解释。如果图片中没有明确待办，返回"未识别到待办"。
  ```

- 图片转为 Base64，构建请求体：

  ```json
  {
    "model": "gpt-4o-mini",
    "messages": [
      {
        "role": "user",
        "content": [
          {"type": "text", "text": "你是一个任务提炼助手..."},
          {"type": "image_url", "image_url": {"url": "data:image/png;base64,..."}}
        ]
      }
    ],
    "max_tokens": 50,
    "temperature": 0.3
  }
  ```

- 设置超时 5 秒，若请求失败或超时，自动降级为离线 OCR
- 网络状态检测：尝试 HEAD 请求 `https://api.openai.com`，超时 2 秒判断为离线

分析触发

    粘贴图片后自动执行

    分析过程中，预览区下方显示“分析中...”并禁用添加按钮

    分析完成后，将结果填充到编辑框中，用户可修改

3. 悬浮待办栏（核心）

    创建一个独立窗口，作为悬浮待办列表

    样式：

        WindowStyle="None", Topmost="True", ShowInTaskbar="False", ResizeMode="NoResize"

        宽度 260，高度自适应，最多同时显示 5 条，超出显示“+N 更多”可展开

        背景为半透明深色（#CC222222），圆角 10，可拖拽移动位置，并记住位置（保存到 JSON 配置）

        初始位置：屏幕右侧中间，距离右边缘 20px，垂直居中

    待办项模板：

        左侧：圆形完成按钮，直径 18，默认灰色边框，鼠标悬停变绿，点击触发完成

        右侧：待办文字，TextBlock 限制最大宽度，超过一行省略（TextTrimming="CharacterEllipsis"）

        每一项高度约 36，有简单的淡入淡出动画（Storyboard 或 DoubleAnimation）

    点击完成按钮：

        该事项从 tasks.json 中移除（或标记 done: true 但立即隐藏）

        列表项执行 FadeOut 动画（0.3 秒），动画结束后从绑定的集合中删除

        列表自动重新排列

    数据绑定：使用 ObservableCollection<TaskItem>，从 JSON 文件反序列化，并在文件变更时通知刷新（可通过文件监控或每次操作后重新加载）

    底部“+N 更多”点击展开全部待办（可切换为完整列表视图，或展开悬浮窗高度）

4. 系统托盘

    托盘图标（可从资源嵌入一个简单的勾选图标）

    右键菜单：

        显示主窗口

        显示/隐藏待办栏

        退出

    左键单击托盘图标：显示主窗口

    关闭主窗口时，隐藏而不是退出，只通过托盘菜单退出

5. 数据持久化

    任务模型 TaskItem：

    ```csharp
    public class TaskItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool Done { get; set; } = false; // 完成状态，用于未来扩展
    }
    ```

    存储位置：Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\TodoSnap\\tasks.json"

    操作：添加时追加，完成时删除（或标记 done 并从列表中隐藏），读取时反序列化

    使用 System.Text.Json 进行序列化，文件加锁避免多实例冲突（lock 或 FileStream 使用 FileShare.None）

6. 配置持久化

    保存悬浮窗位置、是否启用在线分析等设置到同一个 AppData 文件夹下的 config.json

    可选功能：是否开机自启（通过添加快捷方式到启动文件夹实现）

四、代码结构与文件要求

请输出一个完整的 Visual Studio 解决方案，包含以下关键文件：

    TodoSnap.csproj：项目文件，目标框架 net8.0-windows10.0.17763.0，启用 Windows 兼容性，引用 System.Drawing.Common（用于剪贴板图像处理）

    App.xaml / App.xaml.cs：应用入口，初始化托盘，创建主窗口和悬浮窗

    MainWindow.xaml / MainWindow.xaml.cs：粘贴与提炼主窗口

    FloatingWindow.xaml / FloatingWindow.xaml.cs：悬浮待办栏窗口

    Models/TaskItem.cs

    Services/AnalysisService.cs：分析服务，封装 OCR 和 AI 调用

    Services/DataService.cs：JSON 读写服务

    Helpers/ImageHelper.cs：剪贴板图片与 SoftwareBitmap 的转换

    嵌入的图标资源（.ico）

五、界面要求细节

    主窗口背景模糊效果示例：

    ```xml
    <Window.Background>
        <VisualBrush>
            <VisualBrush.Visual>
                <Grid Background="Transparent"/>
            </VisualBrush.Visual>
        </VisualBrush>
    </Window.Background>
    ```

    或利用 AcrylicWindow 第三方实现，但尽量少依赖。简单的半透明效果即可。

    使用 WindowChrome 实现无边框拖拽和缩放，WindowChrome.GlassFrameThickness="-1"。

    按钮样式扁平，无传统边框，悬停时背景高亮。

    所有文本使用系统默认字体，统一字号 14。

六、注意事项

    务必处理 DPI 缩放，在高分屏下保证显示正常

    Windows OCR 需要图片为 SoftwareBitmap，且像素格式为 Bgra8 或 Gray8，转换时注意

    主窗口接收粘贴事件应使用 CommandBinding 或重写 OnKeyDown，保证 Ctrl+V 被拦截

    悬浮窗必须始终在其它窗口之上，但不应抢走焦点（点击待办项时不要让悬浮窗激活，可以设置 ShowActivated="False" 和合适的 Focusable）

    为了保证极致轻量，发布前使用 PublishTrimmed=true 进行裁剪

    应用启动时检测是否已有实例在运行，避免多个实例（可使用 Mutex）

七、期望输出

请生成所有必需的源代码文件，并提供完整的项目文件夹结构。代码应具备清晰的注释，尤其是 OCR 和 AI 调用部分。最后，给出打包为单个 .exe 的命令行指令。

现在开始实现这个应用，确保满足所有极致轻量化和智能提炼的要求。