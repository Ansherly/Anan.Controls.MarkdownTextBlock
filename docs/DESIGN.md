# MarkdownTextBlock(WinUI 3)

基于 **Markdig** 的 WinUI 3 Markdown 渲染控件,架构参考 `CC98\Controls\UbbTextBlock`。

- MarkdownTextBlock 命名空间:`CC98.Controls.MarkdownTextBlock`,目录 `Controls/MarkdownTextBlock`
- CodeBlock 命名空间:`CC98.Controls`,目录 `Controls/CodeBlock`(独立控件,UBB / Markdown 可共用)
- 依赖:`Markdig 0.38.0`、`ColorCode.WinUI 2.0.15`

![行内语法与标题](../../../docs/screenshots/01-行内与标题.png)

---

## 1. 架构

```
Markdown 原文
   │  Markdig.Markdown.Parse(text, Pipeline)          ← 语法分析由 Markdig 完成
   ▼
MarkdownDocument(AST)
   │  RenderContext.RenderNode(node) → RenderStrategies[node.GetType()]
   ▼
PART_RootPanel (StackPanel)
   { RichTextBlock, Picture, CodeBlock, Border(引用/分割线), Grid(表格/列表项), ... }
```

核心思想与 UbbTextBlock 一致:**不把整个文档塞进一个 RichTextBlock**,而是渲染成 StackPanel 中交替排列的文本流与块级 UI,
从而绕开 WinUI 中"块级元素无法放进 Inlines"的限制。

### 渲染四原语(`RenderContext`)

| 原语 | 作用 |
| --- | --- |
| `StartNewTextBlock()` / `FinalizeCurrentTextBlock()` | 开启 / 收尾一个 RichTextBlock(含缩进与段落间距) |
| `BeginInlineContainer(Inline)` / `EndInlineContainer()` | 嵌套行内容器(Bold / Italic / Span / Hyperlink)压栈与挂载 |
| `AddInline(Inline)` | 向当前内联容器或段落追加行内元素 |
| `PushContainer(Panel)` / `PopContainer()` | 切入 / 退出嵌套块级容器(引用块、列表项) |
| `AddToContainer(UIElement)` | 追加块级元素(会先收尾未完成的文本块) |
| `CreateChildContext(Panel)` | 创建隔离的子上下文(表格单元格,共享属性袋) |

### 文件结构

```
Controls/
├─ CodeBlock/                            独立控件(命名空间 CC98.Controls,可被 UBB / Markdown 共用)
│  ├─ CodeBlock.cs                       ColorCode 高亮 + 横向滚动 + 滚轮转发
│  └─ Themes/CodeBlock.xaml              CodeBlock 控件模板(在 App.xaml 合并)
├─ LatexBlock/
│  └─ LatexBlock.cs                      LaTeX 公式控件(CSharpMath + SkiaSharp),行内 / 块级公式都用它
├─ Picture/
│  ├─ Picture.xaml(.cs)                  图片控件:异步加载 / Shimmer 占位 / 无图模式的「点击显示」按钮
│  ├─ IImageLoader.cs                    图片加载抽象(控件与图片共用)
│  ├─ DefaultImageLoader.cs              简单的远程 + 本地加载(弱引用缓存)
│  └─ ImageSourceFactory.cs              按扩展名 / 内容嗅探产出 BitmapImage 或 SvgImageSource
├─ Primitives/
│  └─ SmartImageLoader.cs                应用内通用加载器(弱引用缓存),MarkdownTextBlock 默认用它
└─ MarkdownTextBlock/
   ├─ MarkdownTextBlock.cs               主控件:依赖属性 / 事件 / 策略表 / 渲染入口 / 调试摘要
   ├─ Themes/MarkdownTextBlock.xaml      控件模板(仅 MarkdownTextBlock)
   ├─ Common/
   │  ├─ RenderContext.cs                渲染上下文与四原语
   │  ├─ IRenderStrategy.cs              策略接口
   │  ├─ RenderHelper.cs                 文本收集 / 语言识别 / 标题缩放 / 独立图片判定
   │  ├─ RenderPropertyKeys.cs           渲染属性袋键名
   │  ├─ HtmlTagMapper.cs                HTML 白名单状态机 + 实体解码 + 属性解析
   │  ├─ HtmlTagFacts.cs                 HTML 标签 / 属性事实表
   │  ├─ HtmlStyle.cs                    由 HTML 标签推导的样式(颜色、字号、对齐…)
   │  ├─ HtmlInlineFormatting.cs         行内 CSS 样式解析(字号 / 字重 / 颜色)
   │  ├─ HtmlAlignment.cs                CSS text-align → TextAlignment
   │  └─ Events/                         LinkClickedEventArgs、ImageClickedEventArgs
   └─ Rendering/
      ├─ BlockRenderStrategies.cs        标题/段落/引用/列表/代码块/公式/分割线/表格/HTML 块
      ├─ InlineRenderStrategies.cs       文本/强调/行内代码/链接/自动链接/换行/实体/HTML/任务列表/行内公式
      ├─ ElementFactory.cs               链接、图片、行内代码、任务勾选框、公式的 XAML 工厂
      ├─ HtmlBlockRenderer.cs            HTML 块的排版渲染
      └─ BrushHelper.cs                  由前景色推导主题自适应的浅色画刷

---

## 2. 公开 API

### 依赖属性

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `MarkdownText` | `string` | `""` | 要渲染的 Markdown 原文,变更即重建 |
| `ImageMaxWidth` | `double` | `+∞` | 图片最大宽度 |
| `ImageMaxWidthOverride` | `double?` | `null` | 图片宽度上限的临时覆盖(导出图片等场景让图片铺满可用宽度);生效值 = min(本值, 可用宽度) |
| `ImageMaxHeight` | `double` | `+∞` | 图片最大高度 |
| `ImageStretch` | `Stretch` | `Uniform` | 图片拉伸方式(`Uniform` 保持比例完整显示 / `UniformToFill` 裁切填满 / `Fill` 拉伸变形) |
| `QuoteBackground` | `Brush?` | 半透明灰 | 引用块背景(仅最外层引用块生效) |
| `QuoteBorderBrush` | `Brush?` | 由 `Foreground` 推导 | 引用块左侧竖条画刷(所有嵌套层级共用) |
| `CodeBackground` | `Brush?` | 半透明灰 | 代码块 / 行内代码背景 |
| `LinkForeground` | `Brush?` | `null` | 链接前景色,`null` 时跟随主题;HTML 里显式 `color` 优先 |
| `TableBorderBrush` | `Brush?` | 由 `Foreground` 推导 | 表格边框(Markdown 与 HTML 表格共用) |
| `TableHeaderBackground` | `Brush?` | 由 `Foreground` 推导 | 表头背景填充 |
| `HorizontalRuleBrush` | `Brush?` | 由 `Foreground` 推导 | 分割线(`---` / `<hr>`) |
| `BoldFontFamily` | `FontFamily?` | `null` | 粗体字体,为空时使用系统默认粗体 |
| `H1ForegroundBrush` | `Brush?` | `null` | H1 标题前景色,`null` 时继承 `Foreground` |
| `H2ForegroundBrush` | `Brush?` | `null` | H2 标题前景色,`null` 时继承 `Foreground` |
| `H3ForegroundBrush` | `Brush?` | `null` | H3 标题前景色,`null` 时继承 `Foreground` |
| `H4ForegroundBrush` | `Brush?` | `null` | H4 标题前景色,`null` 时继承 `Foreground` |
| `H5ForegroundBrush` | `Brush?` | `null` | H5 标题前景色,`null` 时继承 `Foreground` |
| `H6ForegroundBrush` | `Brush?` | `null` | H6 标题前景色,`null` 时继承 `Foreground` |
| `HeadingForegroundBrush` | `Brush?` | `null` | **所有标题的兜底色**:`H1ForegroundBrush`…`H6ForegroundBrush` 未单独设置时使用;优先级 `H*ForegroundBrush` > `HeadingForegroundBrush` > `Foreground` |
| `BoldForegroundBrush` | `Brush?` | `null` | 粗体(`**…**` / `<b>` / `<strong>`)前景色,`null` 时继承 `Foreground` |
| `MonoFontFamily` | `FontFamily?` | `null` | 等宽字体(行内代码 / 代码块),为空时用 `Consolas` |
| `CodeFontSize` | `double` | `13` | 代码块字号,与正文 `FontSize` 相互独立 |
| `FontFamily` | `FontFamily?` | 继承 | **重新注册**,变更即重建 |
| `FontSize` | `double` | `14` | **重新注册**,变更即重建 |
| `Foreground` | `Brush?` | 继承 | **重新注册**,变更即重建 |
| `HideImage` | `bool` | `false` | 无图模式:不加载图片,只显示占位(与 UbbTextBlock 的 `HideImage` 对齐) |
| `IsSelectionEnabled` | `bool` | `true` | 文本是否可选中 |
| `ParagraphSpacing` | `double` | `8` | 段落之间的基础间距(引用 / 列表 / 单元格内部按结构另行收紧) |

> `FontFamily` / `FontSize` / `Foreground` 沿用 UbbTextBlock 的做法用 `new` 隐藏基类属性并重新注册,
> 以获得变更回调。XAML 中设置的即是本控件声明的属性。

### 事件

| 事件 | 参数 | 触发时机 |
| --- | --- | --- |
| `LinkClicked` | `LinkClickedEventArgs { Url, Text, IsAutolink, Handled }` | Markdown 链接、GFM 自动链接、HTML `<a>` |
| `ImageClicked` | `ImageClickedEventArgs { Source, Alt, Handled }` | Markdown 图片、HTML `<img>` |

控件本身**不做任何导航**,是否用系统浏览器打开由宿主决定。

### 静态成员

| 成员 | 说明 |
| --- | --- |
| `Pipeline` | Markdig 管线,默认 `UseAdvancedExtensions().UseAutoLinks()` |
| `ImageLoader` | 图片加载器(静态,进程级)。默认 `SmartImageLoader.Default`,渲染图片时会读取它,宿主可整体替换成自己的管线 |
| `RenderStrategies` | 节点类型 → 渲染策略的只读字典,可替换单个策略实现自定义渲染 |

### 实例方法

| 方法 | 说明 |
| --- | --- |
| `GetRenderSummary()` | 渲染树的结构摘要(测试台的 `MDTB_DIAG` 用它做回归比对) |
| `GetImageUrls()` | 按文档顺序返回本次渲染出的所有图片地址(供宿主做图片画廊预览) |

### 用法

```xml
<md:MarkdownTextBlock
    MarkdownText="{x:Bind ViewModel.Content, Mode=OneWay}"
    FontSize="14"
    ImageMaxWidth="480"
    QuoteBackground="{ThemeResource QuoteBrush}"
    QuoteBorderBrush="{ThemeResource QuoteBorderBrush}"
    CodeBackground="{ThemeResource CodeBrush}"
    LinkClicked="OnLinkClicked"
    ImageClicked="OnImageClicked" />
```

```csharp
private void OnLinkClicked(object? sender, LinkClickedEventArgs e)
{
    if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri))
        _ = Launcher.LaunchUriAsync(uri);
}
```

---

## 3. 支持的语法

**块级**:标题 H1–H6(字号梯度 2.0 / 1.7 / 1.4 / 1.2 / 1.1 / 1.0)、段落、引用(可嵌套)、
有序/无序列表(可嵌套,项目符号按层级 • ◦ ▪)、GFM 任务列表(只读勾选框)、围栏与缩进代码块(语法高亮)、
GFM 表格(表头填充、单元格边框、按列对齐)、分割线、HTML 块。

**行内**:`**粗体**`、`*斜体*`、`***粗斜体***`、`~~删除线~~`、`` `行内代码` ``、
`[链接](url)`、裸 URL / 裸邮箱自动链接、`<url>` 自动链接、HTML 实体、`<br>`。
**公式**:`$a^2+b^2=c^2$`(行内)与 `$$…$$`(块级)交给 `LatexBlock`(CSharpMath + SkiaSharp)渲染,字号跟随 `FontSize`。

**HTML 白名单**:`b` `strong` `i` `em` `u` `s` `strike` `del` `code` `a` `img` `br` `p`。
白名单以外的标签**按纯文本原样显示**(不静默丢内容),HTML 块内部按纯文本处理。

---

## 4. 图片加载

图片由 `Picture` 控件 + `IImageLoader` 抽象完成。控件渲染图片时从静态 `ImageLoader` 取加载器,
默认是 `Controls/Primitives/SmartImageLoader.Default`(弱引用内存缓存、`HttpClient` 下载、内容嗅探区分位图 / SVG);
宿主给 `MarkdownTextBlock.ImageLoader` 赋值即可整体替换(接入自己的下载 / 缓存 / 鉴权)。

`SmartImageLoader` 支持的地址形式:

| 形式 | 处理方式 |
| --- | --- |
| `http://` / `https://` | `HttpClient` 下载字节后按内容嗅探解码(SVG → `SvgImageSource`,其余 → `BitmapImage`) |
| `ms-appx:///Assets/x.png` | 交给 `ImageSourceFactory.FromUri`,位图 / SVG 都支持 |
| `ms-appdata:///…` | 同上 |
| `C:\…\x.png` | `StorageFile` 读取后解码 |
| `./x.png` / `../x.png` | 按当前目录解析后同绝对路径 |
| 其它相对形式(`Assets/x.png`) | 兜底按 `ms-appx:///Assets/…` 尝试 |

> 渲染侧**不再**把 `~/…` 自动展开成 `ms-appx:///…`,需要调用方自己归一化。

加载中显示 `Shimmer` 占位;`Hide`(由控件的 `HideImage` 填充)为真时不加载,改为「点击显示图片」按钮;
加载失败则不显示图片(没有「图标 + alt」兜底)。`Src` 快速变化时用代次计数丢弃过期结果。

图片若独占段落按块级渲染,否则以行内形式渲染;两种情况的尺寸规则一致:HTML `width` / `height` 属性按固定尺寸生效,
其余受 `ImageMaxWidth` / `ImageMaxHeight`(及 `ImageMaxWidthOverride`)约束。

> 说明:`SmartImageLoader` 与 `DefaultImageLoader` 都用**弱引用**缓存 —— 同一地址重复出现时不重复解码,
> 图片被 GC 回收后条目失效、下次重新加载,不会把整页图片常驻内存(`DefaultImageLoader.ClearCache()` 可手动清空)。

---

## 5. 渲染行为约定

- **软换行按换行渲染**(保留用户输入的折行),而非折叠为空格 —— 更贴合论坛/聊天场景。
- **嵌套引用**只有最外层使用 `QuoteBackground`,内层仅保留左侧竖条;竖条颜色统一取 `QuoteBorderBrush`(未设置时由 `Foreground` 推导)。
- **列表项**使用"标记列 + 内容列"Grid 布局,标记与内容对齐,嵌套层级通过容器宽度自然缩进。
- **表格**列为等宽(`*`),单元格内容各自独立渲染,`TableColumnDefinition.Alignment` 映射为左/中/右对齐。
- **表格边框**分两级:最外框四条边 2 DIP(粗),内部格线 1 DIP(细)。实现上单元格只画「右 + 下」两条边、末列末行留空,外框由一层 `Border` 统一提供 —— 相邻两格不会各画一条边导致线宽翻倍,任意 DPI 下都是整像素。
- **代码块**右上角有「复制」按钮(无边框 `Button` + `FontIcon`,复制成功后图标变为对勾 1.2 秒);未标注语言的围栏块(以及缩进代码块)语言标签显示 `plaintext`。
- **代码块滚动**:模板结构参考 `CC98\Controls\CodeBlock`(`Grid` + `Auto`/`*` 行 + `ScrollViewer` + 不换行的 `RichTextBlock`,可横向滚动)。
  需要留意 WinUI 的一个已知行为([microsoft-ui-xaml#10172](https://github.com/microsoft/microsoft-ui-xaml/issues/10172)):
  **嵌套的 `ScrollViewer` 只要横向有可滚动内容,就会消费鼠标纵向滚轮且不再向外传递**
  (实测:`ScrollableWidth=10, ScrollableHeight=0` 时滚轮被吞;把横向溢出去掉后滚轮恢复由外层处理)。
  因此 `CodeBlock` 只在"横向确实溢出、且自身纵向不需要滚动"时,按外层 `ScrollViewer` 的原生步长
  (**48 DIP/格**)与动画补一次转发;其余情况完全交给框架,不做任何干预。
  横向滚轮(Shift + 滚轮 / 倾斜滚轮)始终由代码块自己横向滚动。
- **容错**:单个顶层节点渲染异常会被捕获并在原位显示红色错误文本,不影响其余内容。

---

## 6. 已知限制与后续可做

1. 行内代码使用 `InlineUIContainer` 承载带背景的 `Border`(WinUI 的 `Span` 不支持背景色),因此**行内代码不参与文本选择与复制**。
2. 表格列为等宽,未按内容自适应;超宽表格依赖外层横向滚动。
3. 脚注引用/页内锚点按普通文本渲染,未做跳转。
4. 长文档为全量重建(无虚拟化);调用方输入建议做防抖(测试台用 400ms)。
5. 数学公式已由 `LatexBlock` 承担;Emoji、脚注定义区等未专门处理的扩展按普通内容渲染。
6. 图片点击/链接点击的事件链路已在代码层实现并通过编译,但**未能在自动化环境中通过合成鼠标输入完成端到端验证**(受 Windows 前台激活限制,详见测试台说明),建议在 VS 中手动点击确认。

---

## 7. 测试台(MainWindow)

左侧输入 Markdown 原文(400ms 防抖),右侧实时渲染,底部回显 `LinkClicked` / `ImageClicked` 事件;工具栏可调 `FontSize`、`ImageMaxWidth` 与三套配色预设。

调试用环境变量:

| 变量 | 作用 |
| --- | --- |
| `MDTB_SAMPLE` | 启动时载入指定示例(名称或序号) |
| `MDTB_TEXT` | 直接指定要渲染的原文(字面 `
` 视为换行),用于核对特定语法片段 |
| `MDTB_PRESET` | 启动时的配色预设序号(0 跟随主题 / 1 暖色 / 2 冷色 / 3 高对比) |
| `MDTB_FONT_SIZE` | 启动时的正文字号(用于检查字号缩放) |
| `MDTB_DIAG=<文件路径>` | 周期性把渲染结构摘要 + 布局/滚动偏移写入文件(控件提供 `GetRenderSummary()`) |
| `MDTB_OPEN_LINKS=1` | 点击链接时交给系统浏览器打开(默认只记日志) |

启动时的未处理异常会写到 `%TEMP%\MarkodwnTextBlockTest.crash.log`,便于排查 XAML/启动崩溃。

自动化辅助脚本(需要 `pillow`):

| 脚本 | 作用 |
| --- | --- |
| `tools/snapshot.py <名称> [示例] [预设序号]` | **非侵入式**截图:只置顶窗口、不动鼠标不抢焦点,适合日常视觉核对 |
| `tools/capture_window.py [前缀] [滚动次数]` | 启动应用并分段截图(会注入滚轮,用于回归检查渲染效果) |
| `tools/measure_scroll.py [文本区Y] [代码块Y] [预滚动格数] [X]` | 对比滚动区与代码块区的滚轮步长(读取 `MDTB_DIAG` 输出里的 `offset`) |
| `tools/click_test.py <x> <y> [前缀]` | 在指定逻辑坐标点击并截图(受系统前台激活策略影响,可能无法命中控件) |

构建(直接运行 exe 需自包含运行时):

```bash
dotnet build MarkodwnTextBlockTest.csproj -p:Platform=x64
# 非打包方式直接运行:
dotnet build MarkodwnTextBlockTest.csproj -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:SelfContained=true
```

> ⚠️ **两种构建会共用同一输出目录**:先用自包含方式构建、再用普通方式构建(或反之)会让输出目录里的
> `resources.pri` 与 Windows App SDK 运行时文件不匹配,应用启动即抛
> `XamlParseException: Cannot locate resource from 'ms-appx:///Microsoft.UI.Xaml/Themes/themeresources.xaml'`
> (退出码 `0xC000027B`)。遇到这种情况删掉 `bin/<Platform>/<Config>`、`obj/<Platform>/<Config>` 重新构建即可。
