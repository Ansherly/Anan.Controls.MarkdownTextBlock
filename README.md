# MarkdownTextBlock

WinUI 3 的 Markdown 渲染控件。Markdig 解析成 AST,再由可替换的渲染策略翻译成
`StackPanel` 中交替排列的 `RichTextBlock`(文本流)与块级 UI 元素(图片 / 代码块 / 引用 / 表格 / 公式)。

- 目标框架:`net10.0-windows10.0.19041.0`,Windows App SDK 2.7
- 依赖全部为稳定版;AOT / 裁剪友好(`IsAotCompatible`)
- 控件模板通过库内的 `Themes/Generic.xaml` **自动加载**,不需要在 `App.xaml` 里合并任何资源字典

## 安装

```bash
dotnet add package Anan.Controls.MarkdownTextBlock --version 1.0.0
```

## 最小用法

XAML(命名空间 `Anan.Controls.MarkdownTextBlock`):

```xml
<Page
    xmlns:md="using:Anan.Controls.MarkdownTextBlock">
    <md:MarkdownTextBlock MarkdownText="{x:Bind ViewModel.Content, Mode=OneWay}"
                          LinkClicked="OnLinkClicked"
                          ImageClicked="OnImageClicked" />
</Page>
```

事件处理(控件本身**不做导航**,是否打开浏览器由宿主决定):

```csharp
private void OnLinkClicked(object? sender, LinkClickedEventArgs e)
{
    if (Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)) _ = Launcher.LaunchUriAsync(uri);
}
```

## 支持的语法

**块级**:标题 H1–H6、段落、引用(可嵌套)、有序/无序列表(可嵌套)、GFM 任务列表、
围栏与缩进代码块(语法高亮)、GFM 表格(列对齐)、分割线、HTML 块、块级公式 `$$…$$`。

**行内**:粗体 / 斜体 / 粗斜体 / 删除线、行内代码、链接、裸 URL 与裸邮箱自动链接、
HTML 实体、`<br>`、行内公式 `$…$`。

**HTML 白名单**:`b` `strong` `i` `em` `u` `s` `strike` `del` `code` `a` `img` `br` `p`;
白名单外的标签按纯文本原样显示(不静默丢内容)。

## 主要属性

| 属性 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `MarkdownText` | `string` | `""` | 要渲染的原文,变更即重建 |
| `FontFamily` / `FontSize` / `Foreground` | | 继承 / 14 | 正文排版(变更即重建) |
| `BoldFontFamily` | `FontFamily?` | `null` | 粗体字体 |
| `MonoFontFamily` | `FontFamily?` | `null` | 等宽字体,为空用 `Consolas` |
| `CodeFontSize` | `double` | `13` | 代码块字号(与正文独立) |
| `CodeBackground` | `Brush?` | 半透明灰 | 代码块 / 行内代码背景 |
| `QuoteBackground` / `QuoteBorderBrush` | `Brush?` | 推导 | 引用块 |
| `LinkForeground` | `Brush?` | `null` | 链接色;HTML 显式 `color` 优先 |
| `TableBorderBrush` / `TableHeaderBackground` | `Brush?` | 推导 | 表格(外框 2 DIP、内线 1 DIP) |
| `HorizontalRuleBrush` | `Brush?` | 推导 | 分割线 |
| `H1ForegroundBrush` … `H6ForegroundBrush` | `Brush?` | `null` | 分级标题色 |
| `HeadingForegroundBrush` | `Brush?` | `null` | 标题**兜底**色(优先级:H* > Heading > Foreground) |
| `BoldForegroundBrush` | `Brush?` | `null` | 粗体色 |
| `ImageMaxWidth` / `ImageMaxHeight` | `double` | `+∞` | 图片尺寸上限 |
| `ImageMaxWidthOverride` | `double?` | `null` | 临时覆盖宽度上限(导出图片等) |
| `ImageStretch` | `Stretch` | `Uniform` | 图片拉伸方式 |
| `HideImage` | `bool` | `false` | 无图模式(显示「点击显示图片」) |
| `ParagraphSpacing` | `double` | `8` | 段落基础间距 |
| `LineHeight` | `double` | `0` | 行高(绝对 DIP);`0` 用字体默认,`>0` 时为固定行高(内部配合 `BlockLineHeight`) |
| `TextAlignment` | `TextAlignment` | `Left` | 正文块级对齐(段落 / 标题 / 列表内容都跟随) |
| `HeadingFontFamily` | `FontFamily?` | `null` | 标题专用字体,为空时沿用 `BoldFontFamily` |
| `CharacterSpacing` | `int` | `0` | **继承自 `Control`,单位 1/1000 em**;渲染时会下发给每个段落 |
| `FontWeight` / `FontStyle` / `FontStretch` | | 继承 | **继承自 `Control`**,同样会下发到段落(标题 / 粗体另按自身规则覆盖) |
| `IsSelectionEnabled` | `bool` | `true` | 文本可选中 |

> `CharacterSpacing` / `FontWeight` / `FontStyle` / `FontStretch` / `IsTextScaleFactorEnabled` 沿用 `Control` 自带的属性:
> 控件不会隐藏它们,而是在创建每个文本块时把它们下发到 `Paragraph` 上 —— 因此 XAML 里直接写 `CharacterSpacing="150"` 即生效。

## 事件

| 事件 | 参数 | 触发时机 |
| --- | --- | --- |
| `LinkClicked` | `LinkClickedEventArgs { Url, Text, IsAutolink, Handled }` | Markdown 链接 / 自动链接 / HTML `<a>` |
| `ImageClicked` | `ImageClickedEventArgs { Source, Alt, Handled }` | Markdown 图片 / HTML `<img>` |

## 静态成员与实例方法

| 成员 | 说明 |
| --- | --- |
| `Pipeline`(静态) | Markdig 管线,默认 `UseAdvancedExtensions() + UseAutoLinks()` |
| `ImageLoader`(静态) | 图片加载器,默认 `SmartImageLoader.Default`;渲染时读取,可整体替换 |
| `RenderStrategies`(静态) | 节点类型 → 渲染策略的只读字典(进程级) |
| `GetRenderSummary()` | 渲染树结构摘要(诊断用) |
| `GetImageUrls()` | 按文档顺序返回所有图片地址(供图片画廊) |

## 图片加载

默认 `SmartImageLoader`:支持 `http(s)`、`ms-appx`、`ms-appdata`、绝对路径与 `./` 相对路径,
内容嗅探区分位图与 SVG,**弱引用缓存**(重复地址不重复解码,GC 回收后自动失效)。

接入自己的管线:

```csharp
MarkdownTextBlock.ImageLoader = myLoader; // 实现 IImageLoader
```

## 代码块

- 语法高亮(ColorCode),横向可滚动;滚轮统一转发给外层滚动容器
- 右上角复制按钮:复制代码原文,图标短暂变为对勾
- 未标注语言时语言标签显示 `plaintext`

## 表格

最外框四条边 2 DIP(粗)、内部格线 1 DIP(细)。单元格只画「右 + 下」两条边、末列末行留空,
外框由一层 `Border` 提供 —— 不会出现相邻边框叠加导致的粗细不均,任意 DPI 下都是整像素。

## 已知限制

- 全量重建(无虚拟化):长文档请自行做输入防抖
- 行内代码用 `InlineUIContainer` 承载背景,不参与文本选择
- 表格列为等宽,超宽表格依赖外层横向滚动
- 仅提供中文界面文案(复制按钮提示、渲染错误信息)
