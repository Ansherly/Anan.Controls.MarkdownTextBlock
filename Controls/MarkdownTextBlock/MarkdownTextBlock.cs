using System;
using System.Collections.Generic;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Anan.Controls.MarkdownTextBlock.Common;
using Anan.Controls.MarkdownTextBlock.Common.Events;
using Anan.Controls;
using Anan.Controls.Primitives;
using Anan.Controls.MarkdownTextBlock.Rendering;
using Windows.UI;
using Inline = Microsoft.UI.Xaml.Documents.Inline;

namespace Anan.Controls.MarkdownTextBlock;

/// <summary>
///     WinUI 3 的 Markdown 渲染控件。
///     架构参考 Anan.Controls.UbbTextBlock:Markdig 解析出 AST,再由策略字典渲染为
///     StackPanel 中交替排列的 RichTextBlock(文本流)与块级 UI 元素(图片 / 代码块 / 引用 / 表格)。
/// </summary>
public sealed partial class MarkdownTextBlock : Control
{
    private const string RootPanelPartName = "PART_RootPanel";

    private RenderContext? _context;
    private StackPanel? _rootPanel;

    public MarkdownTextBlock()
    {
        DefaultStyleKey = typeof(MarkdownTextBlock);
    }

    #region 渲染诊断(调试用)

    /// <summary>
    ///     渲染结果结构摘要:逐层列出生成的块级元素及其文本,便于排查渲染问题。
    /// </summary>
    public string GetRenderSummary()
    {
        if (_rootPanel is null) return "(控件模板尚未应用)";

        var builder = new System.Text.StringBuilder();
        DumpChildren(_rootPanel, builder, 0);
        return builder.ToString();
    }

    private static void DumpChildren(Panel panel, System.Text.StringBuilder builder, int depth)
    {
        var indent = new string(' ', depth * 2);

        foreach (var child in panel.Children)
            switch (child)
            {
                case RichTextBlock richTextBlock:
                {
                    var text = new System.Text.StringBuilder();
                    foreach (var block in richTextBlock.Blocks)
                        if (block is Paragraph paragraph)
                            foreach (var inline in paragraph.Inlines)
                                text.Append(DescribeInline(inline));

                    builder.AppendLine($"{indent}RichTextBlock  [{Truncate(text.ToString())}]");
                    break;
                }
                case CodeBlock codeBlock:
                    builder.AppendLine(
                        $"{indent}CodeBlock      [{codeBlock.LanguageName}] {Truncate(codeBlock.Code)} | {codeBlock.GetScrollDebugInfo()}");
                    break;
                case Picture picture:
                    builder.AppendLine($"{indent}Picture        {picture.Src}");
                    break;
                case Border border:
                    builder.AppendLine($"{indent}Border");
                    switch (border.Child)
                    {
                        case Panel nestedPanel:
                            DumpChildren(nestedPanel, builder, depth + 1);
                            break;
                        case CodeBlock codeBlock:
                            builder.AppendLine($"{indent}  CodeBlock    [{codeBlock.LanguageName}]");
                            break;
                    }

                    break;
                case Grid grid:
                    builder.AppendLine($"{indent}Grid ({grid.RowDefinitions.Count}行 × {grid.ColumnDefinitions.Count}列)");
                    foreach (var cell in grid.Children)
                        if (cell is TextBlock marker)
                            builder.AppendLine($"{indent}  Marker [{marker.Text}]");
                        else if (cell is Border { Child: Panel cellPanel })
                        {
                            builder.AppendLine($"{indent}  Cell");
                            DumpChildren(cellPanel, builder, depth + 2);
                        }

                    break;
                default:
                    builder.AppendLine($"{indent}{child.GetType().Name}");
                    break;
            }
    }

    /// <summary>
    ///     按文档顺序返回本次渲染出的所有图片地址。
    /// </summary>
    /// <remarks>
    ///     供宿主实现"图片画廊"使用:点击某张图时把整段内容里的图片一起交给预览器,
    ///     体验与 UbbTextBlock 的图片点击一致。返回的是控件实际渲染用的地址(已做完 ~/ 展开等归一化),
    ///     因此可以直接用 <c>IndexOf</c> 定位被点击的那一张。
    /// </remarks>
    public IReadOnlyList<string> GetImageUrls()
    {
        var urls = new List<string>();
        if (_rootPanel is null) return urls;

        CollectImageUrls(_rootPanel, urls);
        return urls;
    }

    private static void CollectImageUrls(DependencyObject element, List<string> urls)
    {
        if (element is Picture picture)
        {
            if (!string.IsNullOrWhiteSpace(picture.Src)) urls.Add(picture.Src);
            return; // 图片内部不会再有图片
        }

        var count = VisualTreeHelper.GetChildrenCount(element);
        for (var i = 0; i < count; i++) CollectImageUrls(VisualTreeHelper.GetChild(element, i), urls);
    }

    private static string DescribeInline(Inline inline)
    {
        switch (inline)
        {
            case Run run:
                return run.Text;
            case LineBreak:
                return "\\n";
            case InlineUIContainer container:
                return $"[UI:{container.Child?.GetType().Name}]";
            case Span span:
            {
                var builder = new System.Text.StringBuilder();
                foreach (var child in span.Inlines) builder.Append(DescribeInline(child));
                return builder.ToString();
            }
            default:
                return string.Empty;
        }
    }

    private static string Truncate(string text)
    {
        text = text.Replace("\r", string.Empty).Replace("\n", "\\n");
        return text.Length <= 80 ? text : text[..80] + "…";
    }

    #endregion

    #region 全局配置

    /// <summary>Markdig 管线(默认开启 GFM 高级扩展与自动链接)。</summary>
    public static MarkdownPipeline Pipeline { get; set; } = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoLinks()
        .Build();

    /// <summary>图片加载器(默认 <see cref="SmartImageLoader" />,宿主可替换)。</summary>
    /// <remarks>
    ///     渲染图片时会读取本属性,因此替换它即可接入宿主的图片管线(下载、缓存、鉴权等)。
    ///     注意:本属性是进程级静态配置,同一进程内所有 <see cref="MarkdownTextBlock" /> 共用。
    /// </remarks>
    public static IImageLoader ImageLoader { get; set; } = SmartImageLoader.Default;

    /// <summary>渲染策略表:Markdig 节点类型 → 渲染策略。可在进程内替换单个策略以自定义渲染。</summary>
    public static readonly IReadOnlyDictionary<Type, IRenderStrategy> RenderStrategies =
        new Dictionary<Type, IRenderStrategy>
        {
            // 块级
            [typeof(HeadingBlock)] = new HeadingRenderStrategy(),
            [typeof(ParagraphBlock)] = new ParagraphRenderStrategy(),
            [typeof(QuoteBlock)] = new QuoteRenderStrategy(),
            [typeof(ListBlock)] = new ListRenderStrategy(),
            [typeof(ListItemBlock)] = new ListItemRenderStrategy(),
            [typeof(FencedCodeBlock)] = new CodeBlockRenderStrategy(),
            [typeof(CodeBlock)] = new CodeBlockRenderStrategy(),
            [typeof(ThematicBreakBlock)] = new ThematicBreakRenderStrategy(),
            [typeof(HtmlBlock)] = new HtmlBlockRenderStrategy(),
            [typeof(MathBlock)] = new MathBlockRenderStrategy(),
            [typeof(Table)] = new TableRenderStrategy(),
            [typeof(TableRow)] = new TableRowRenderStrategy(),
            [typeof(TableCell)] = new TableCellRenderStrategy(),

            // 行内
            [typeof(LiteralInline)] = new LiteralRenderStrategy(),
            [typeof(EmphasisInline)] = new EmphasisRenderStrategy(),
            [typeof(CodeInline)] = new CodeInlineRenderStrategy(),
            [typeof(LinkInline)] = new LinkRenderStrategy(),
            [typeof(AutolinkInline)] = new AutolinkRenderStrategy(),
            [typeof(LineBreakInline)] = new LineBreakRenderStrategy(),
            [typeof(HtmlInline)] = new HtmlInlineRenderStrategy(),
            [typeof(HtmlEntityInline)] = new HtmlEntityRenderStrategy(),
            [typeof(MathInline)] = new MathInlineRenderStrategy(),
            [typeof(Markdig.Extensions.TaskLists.TaskList)] = new TaskListRenderStrategy()
        };

    #endregion

    #region 依赖属性

    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(
            nameof(MarkdownText),
            typeof(string),
            typeof(MarkdownTextBlock),
            new(string.Empty, OnRenderPropertyChanged));

    /// <summary>重新注册字体属性,以便变更时重建内容(与 UbbTextBlock 一致)。</summary>
    public new static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(
            nameof(FontSize),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(14.0, OnRenderPropertyChanged));

    public new static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(
            nameof(FontFamily),
            typeof(FontFamily),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    public new static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(
            nameof(Foreground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>粗体字体。为空时使用系统默认字体的粗体。</summary>
    public static readonly DependencyProperty BoldFontFamilyProperty =
        DependencyProperty.Register(
            nameof(BoldFontFamily),
            typeof(FontFamily),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>等宽字体(行内代码、代码块使用)。为空时使用 <c>Consolas</c>。</summary>
    public static readonly DependencyProperty MonoFontFamilyProperty =
        DependencyProperty.Register(
            nameof(MonoFontFamily),
            typeof(FontFamily),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>代码块字号。默认 13。</summary>
    /// <remarks>
    ///     与正文的 <see cref="FontSize" /> 相互独立:调整正文字号时,代码块不会跟着缩放。
    ///     默认值 13 与 CodeBlock 控件模板里的默认值一致。
    /// </remarks>
    public static readonly DependencyProperty CodeFontSizeProperty =
        DependencyProperty.Register(
            nameof(CodeFontSize),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(13.0, OnRenderPropertyChanged));

    /// <summary>代码块背景。</summary>
    public static readonly DependencyProperty CodeBackgroundProperty =
        DependencyProperty.Register(
            nameof(CodeBackground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>引用块背景(仅最外层引用块生效,嵌套引用透明)。</summary>
    public static readonly DependencyProperty QuoteBackgroundProperty =
        DependencyProperty.Register(
            nameof(QuoteBackground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>引用块左侧竖条画刷(为空时由 <see cref="Control.Foreground" /> 推导)。</summary>
    public static readonly DependencyProperty QuoteBorderBrushProperty =
        DependencyProperty.Register(
            nameof(QuoteBorderBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>链接前景色。<see langword="null" /> 表示不覆盖,交由主题的链接色决定。</summary>
    /// <remarks>
    ///     只影响普通链接;HTML 里显式声明了颜色(如 <c>&lt;a style="color:red"&gt;</c>)时以声明为准。
    /// </remarks>
    public static readonly DependencyProperty LinkForegroundProperty =
        DependencyProperty.Register(
            nameof(LinkForeground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>表格边框画刷(Markdown 表格与 HTML 表格共用)。<see langword="null" /> 时由 <see cref="Foreground" /> 推导。</summary>
    public static readonly DependencyProperty TableBorderBrushProperty =
        DependencyProperty.Register(
            nameof(TableBorderBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>表头背景画刷。<see langword="null" /> 时由 <see cref="Foreground" /> 推导出很浅的填充色。</summary>
    public static readonly DependencyProperty TableHeaderBackgroundProperty =
        DependencyProperty.Register(
            nameof(TableHeaderBackground),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>分割线画刷。<see langword="null" /> 时由 <see cref="Foreground" /> 推导。</summary>
    public static readonly DependencyProperty HorizontalRuleBrushProperty =
        DependencyProperty.Register(
            nameof(HorizontalRuleBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>H1 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty H1ForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(H1ForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>H2 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty H2ForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(H2ForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>H3 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty H3ForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(H3ForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>H4 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty H4ForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(H4ForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>H5 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty H5ForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(H5ForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>H6 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty H6ForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(H6ForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));
    /// <summary>粗体(<c>**…**</c>、<c>&lt;b&gt;</c>、<c>&lt;strong&gt;</c>)的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty BoldForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(BoldForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>所有标题的兜底前景色:H1–H6 未单独设置时使用。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public static readonly DependencyProperty HeadingForegroundBrushProperty =
        DependencyProperty.Register(
            nameof(HeadingForegroundBrush),
            typeof(Brush),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>图片最大宽度。</summary>
    public static readonly DependencyProperty ImageMaxWidthProperty =
        DependencyProperty.Register(
            nameof(ImageMaxWidth),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(double.PositiveInfinity, OnRenderPropertyChanged));

    /// <summary>
    ///     图片宽度上限的临时覆盖，<see langword="null" /> 表示不覆盖（沿用 <see cref="ImageMaxWidth" />）。
    /// </summary>
    /// <remarks>
    ///     用途：让特定调用点绕开「设置 - 图片最大宽度」。回复模板的图片宽度来自
    ///     <c>AppSettings.ImageMaxWidth</c>（默认 400），导出成图片时卡片里的图会显得很小，
    ///     所以导出期间由模板从这里喂一个更大的值，让图片按可用宽度铺满。
    ///     <para>
    ///         实际生效宽度 = min(本值, 可用宽度)。「可用宽度」这一层由 <c>Image</c> 测量时钳制；
    ///         渲染发生在布局之前，这里不要读 ActualWidth（那时还拿不到宽度）。
    ///     </para>
    /// </remarks>
    public static readonly DependencyProperty ImageMaxWidthOverrideProperty =
        DependencyProperty.Register(
            nameof(ImageMaxWidthOverride),
            typeof(double?),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>图片最大高度。</summary>
    public static readonly DependencyProperty ImageMaxHeightProperty =
        DependencyProperty.Register(
            nameof(ImageMaxHeight),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(double.PositiveInfinity, OnRenderPropertyChanged));

    /// <summary>图片拉伸方式,默认 <see cref="Stretch.Uniform" />。</summary>
    /// <remarks>
    ///     <see cref="Stretch.Uniform" /> 保持原始宽高比并完整显示;
    ///     <see cref="Stretch.UniformToFill" /> 仍保持比例但会裁掉超出部分;
    ///     <see cref="Stretch.Fill" /> 直接拉伸(会变形),通常只在 HTML 已用 width/height 固定尺寸时才合适。
    /// </remarks>
    public static readonly DependencyProperty ImageStretchProperty =
        DependencyProperty.Register(
            nameof(ImageStretch),
            typeof(Stretch),
            typeof(MarkdownTextBlock),
            new(Stretch.Uniform, OnRenderPropertyChanged));

    /// <summary>无图模式:为 true 时不加载图片,只显示占位(与 UbbTextBlock.HideImage 对齐)。</summary>
    public static readonly DependencyProperty HideImageProperty =
        DependencyProperty.Register(
            nameof(HideImage),
            typeof(bool),
            typeof(MarkdownTextBlock),
            new(false, OnRenderPropertyChanged));

    /// <summary>是否允许选中文本。</summary>
    public static readonly DependencyProperty IsSelectionEnabledProperty =
        DependencyProperty.Register(
            nameof(IsSelectionEnabled),
            typeof(bool),
            typeof(MarkdownTextBlock),
            new(true, OnRenderPropertyChanged));

    /// <summary>段落之间的垂直间距,默认 8。</summary>
    /// <remarks>
    ///     这是「基础节奏」:引用块 / 列表 / 表格单元格内部会按各自的结构收紧间距,不受本属性影响。
    /// </remarks>
    public static readonly DependencyProperty ParagraphSpacingProperty =
        DependencyProperty.Register(
            nameof(ParagraphSpacing),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(8.0, OnRenderPropertyChanged));

    /// <summary>行高(绝对 DIP),0 表示使用字体自身的行高。</summary>
    /// <remarks>
    ///     大于 0 时会同时把 RichTextBlock 的行堆叠策略设为 BlockLineHeight,
    ///     这样行高才是一个「固定值」——否则 WinUI 取的是「行高与内容高度的较大者」,看起来没有变化。
    /// </remarks>
    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(
            nameof(LineHeight),
            typeof(double),
            typeof(MarkdownTextBlock),
            new(0.0, OnRenderPropertyChanged));

    /// <summary>正文块级对齐(段落 / 标题 / 列表内容都跟随),默认 <see cref="TextAlignment.Left" />。</summary>
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(
            nameof(TextAlignment),
            typeof(TextAlignment),
            typeof(MarkdownTextBlock),
            new(TextAlignment.Left, OnRenderPropertyChanged));

    /// <summary>标题专用字体,为空时沿用 <see cref="BoldFontFamily" />。</summary>
    public static readonly DependencyProperty HeadingFontFamilyProperty =
        DependencyProperty.Register(
            nameof(HeadingFontFamily),
            typeof(FontFamily),
            typeof(MarkdownTextBlock),
            new(null, OnRenderPropertyChanged));

    /// <summary>Markdown 原文。</summary>
    public string MarkdownText
    {
        get => (string)GetValue(MarkdownTextProperty);
        set => SetValue(MarkdownTextProperty, value);
    }

    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public new FontFamily? FontFamily
    {
        get => GetValue(FontFamilyProperty) as FontFamily;
        set => SetValue(FontFamilyProperty, value);
    }

    public new Brush? Foreground
    {
        get => GetValue(ForegroundProperty) as Brush;
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily? BoldFontFamily
    {
        get => GetValue(BoldFontFamilyProperty) as FontFamily;
        set => SetValue(BoldFontFamilyProperty, value);
    }

    /// <summary>等宽字体(行内代码、代码块使用)。为空时使用 <c>Consolas</c>。</summary>
    public FontFamily? MonoFontFamily
    {
        get => GetValue(MonoFontFamilyProperty) as FontFamily;
        set => SetValue(MonoFontFamilyProperty, value);
    }

    /// <summary>代码块字号(默认 13,与正文 <see cref="FontSize" /> 相互独立)。</summary>
    public double CodeFontSize
    {
        get => (double)GetValue(CodeFontSizeProperty);
        set => SetValue(CodeFontSizeProperty, value);
    }

    public Brush? CodeBackground
    {
        get => GetValue(CodeBackgroundProperty) as Brush;
        set => SetValue(CodeBackgroundProperty, value);
    }

    public Brush? QuoteBackground
    {
        get => GetValue(QuoteBackgroundProperty) as Brush;
        set => SetValue(QuoteBackgroundProperty, value);
    }

    public Brush? QuoteBorderBrush
    {
        get => GetValue(QuoteBorderBrushProperty) as Brush;
        set => SetValue(QuoteBorderBrushProperty, value);
    }

    /// <summary>链接前景色。<see langword="null" /> 表示不覆盖,交由主题决定。</summary>
    public Brush? LinkForeground
    {
        get => GetValue(LinkForegroundProperty) as Brush;
        set => SetValue(LinkForegroundProperty, value);
    }

    /// <summary>表格边框画刷。<see langword="null" /> 时由 <see cref="Foreground" /> 推导。</summary>
    public Brush? TableBorderBrush
    {
        get => GetValue(TableBorderBrushProperty) as Brush;
        set => SetValue(TableBorderBrushProperty, value);
    }

    /// <summary>表头背景画刷。<see langword="null" /> 时由 <see cref="Foreground" /> 推导。</summary>
    public Brush? TableHeaderBackground
    {
        get => GetValue(TableHeaderBackgroundProperty) as Brush;
        set => SetValue(TableHeaderBackgroundProperty, value);
    }

    /// <summary>分割线画刷。<see langword="null" /> 时由 <see cref="Foreground" /> 推导。</summary>
    public Brush? HorizontalRuleBrush
    {
        get => GetValue(HorizontalRuleBrushProperty) as Brush;
        set => SetValue(HorizontalRuleBrushProperty, value);
    }
    /// <summary>H1 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? H1ForegroundBrush
    {
        get => GetValue(H1ForegroundBrushProperty) as Brush;
        set => SetValue(H1ForegroundBrushProperty, value);
    }

    /// <summary>H2 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? H2ForegroundBrush
    {
        get => GetValue(H2ForegroundBrushProperty) as Brush;
        set => SetValue(H2ForegroundBrushProperty, value);
    }

    /// <summary>H3 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? H3ForegroundBrush
    {
        get => GetValue(H3ForegroundBrushProperty) as Brush;
        set => SetValue(H3ForegroundBrushProperty, value);
    }

    /// <summary>H4 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? H4ForegroundBrush
    {
        get => GetValue(H4ForegroundBrushProperty) as Brush;
        set => SetValue(H4ForegroundBrushProperty, value);
    }

    /// <summary>H5 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? H5ForegroundBrush
    {
        get => GetValue(H5ForegroundBrushProperty) as Brush;
        set => SetValue(H5ForegroundBrushProperty, value);
    }

    /// <summary>H6 的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? H6ForegroundBrush
    {
        get => GetValue(H6ForegroundBrushProperty) as Brush;
        set => SetValue(H6ForegroundBrushProperty, value);
    }

    /// <summary>粗体的前景色。<see langword="null" /> 时继承 <see cref="Foreground" />。</summary>
    public Brush? BoldForegroundBrush
    {
        get => GetValue(BoldForegroundBrushProperty) as Brush;
        set => SetValue(BoldForegroundBrushProperty, value);
    }

    /// <summary>所有标题的兜底前景色(H1–H6 未单独设置时使用)。</summary>
    public Brush? HeadingForegroundBrush
    {
        get => GetValue(HeadingForegroundBrushProperty) as Brush;
        set => SetValue(HeadingForegroundBrushProperty, value);
    }

    public double ImageMaxWidth
    {
        get => (double)GetValue(ImageMaxWidthProperty);
        set => SetValue(ImageMaxWidthProperty, value);
    }

    /// <summary>图片宽度上限的临时覆盖（见 <see cref="ImageMaxWidthOverrideProperty" />）。</summary>
    public double? ImageMaxWidthOverride
    {
        get => GetValue(ImageMaxWidthOverrideProperty) as double?;
        set => SetValue(ImageMaxWidthOverrideProperty, value);
    }

    public double ImageMaxHeight
    {
        get => (double)GetValue(ImageMaxHeightProperty);
        set => SetValue(ImageMaxHeightProperty, value);
    }

    /// <summary>图片拉伸方式(默认 <see cref="Stretch.Uniform" />)。</summary>
    public Stretch ImageStretch
    {
        get => (Stretch)GetValue(ImageStretchProperty);
        set => SetValue(ImageStretchProperty, value);
    }

    /// <summary>无图模式(与 UbbTextBlock.HideImage 对齐)。</summary>
    public bool HideImage
    {
        get => (bool)GetValue(HideImageProperty);
        set => SetValue(HideImageProperty, value);
    }

    public bool IsSelectionEnabled
    {
        get => (bool)GetValue(IsSelectionEnabledProperty);
        set => SetValue(IsSelectionEnabledProperty, value);
    }

    /// <summary>段落之间的垂直间距(默认 8)。</summary>
    public double ParagraphSpacing
    {
        get => (double)GetValue(ParagraphSpacingProperty);
        set => SetValue(ParagraphSpacingProperty, value);
    }

    /// <summary>行高(绝对 DIP),0 表示使用字体自身的行高。</summary>
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty);
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>正文块级对齐,默认左对齐。</summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>标题专用字体,为空时沿用 <see cref="BoldFontFamily" />。</summary>
    public FontFamily? HeadingFontFamily
    {
        get => GetValue(HeadingFontFamilyProperty) as FontFamily;
        set => SetValue(HeadingFontFamilyProperty, value);
    }

    #endregion

    #region 事件

    /// <summary>链接点击(Markdown 链接 / 自动链接 / HTML &lt;a&gt;)。控件本身不做导航。</summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClicked;

    /// <summary>图片点击(Markdown 图片 / HTML &lt;img&gt;)。</summary>
    public event EventHandler<ImageClickedEventArgs>? ImageClicked;

    public void RaiseLinkClicked(string url, string text, bool isAutolink = false)
    {
        LinkClicked?.Invoke(this, new LinkClickedEventArgs(url ?? string.Empty, text ?? string.Empty, isAutolink));
    }

    public void RaiseImageClicked(string src, string alt)
    {
        ImageClicked?.Invoke(this, new ImageClickedEventArgs(src ?? string.Empty, alt ?? string.Empty));
    }

    #endregion

    #region 渲染

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _rootPanel = GetTemplateChild(RootPanelPartName) as StackPanel;
        RenderContent();
    }

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownTextBlock control) control.RenderContent();
    }

    /// <summary>重新解析并重建内容。</summary>
    public void RenderContent()
    {
        if (_rootPanel is null) return;

        _rootPanel.Children.Clear();

        var text = MarkdownText;
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            var document = Markdown.Parse(text, Pipeline);

            _context = new RenderContext
            {
                Control = this,
                Container = _rootPanel
            };

            FillProperties(_context);
            _context.ParagraphSpacing = ParagraphSpacing;

            // 逐个顶层节点渲染:单个节点出错不影响其余内容
            foreach (var node in document)
                try
                {
                    _context.RenderNode(node);
                }
                catch (Exception nodeException)
                {
                    _context.FinalizeCurrentTextBlock();
                    _rootPanel.Children.Add(CreateErrorText($"{node.GetType().Name} 渲染失败:{nodeException.Message}"));
                }

            _context.FinalizeCurrentTextBlock();
        }
        catch (Exception ex)
        {
            _rootPanel.Children.Add(CreateErrorText($"Markdown 渲染失败:{ex.Message}"));
        }
    }

    private static TextBlock CreateErrorText(string message)
    {
        return new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C))
        };
    }

    /// <summary>
    ///     填充本次渲染使用的属性袋。
    /// </summary>
    private void FillProperties(RenderContext context)
    {
        var properties = context.Properties;

        properties[RenderPropertyKeys.FontSize] = FontSize;
        properties[RenderPropertyKeys.Foreground] = Foreground;
        properties[RenderPropertyKeys.BoldFontFamily] = BoldFontFamily;
        // 覆盖优先：导出图片时模板会喂一个更大的值，让图片按可用宽度铺满
        // （实际宽度仍被可用宽度钳制，见 ImageMaxWidthOverride）
        properties[RenderPropertyKeys.ImageMaxWidth] = ImageMaxWidthOverride ?? ImageMaxWidth;
        properties[RenderPropertyKeys.ImageMaxHeight] = ImageMaxHeight;
        properties[RenderPropertyKeys.HideImage] = HideImage;
        properties[RenderPropertyKeys.CodeFontSize] = CodeFontSize;
        properties[RenderPropertyKeys.LinkForeground] = LinkForeground;
        properties[RenderPropertyKeys.ImageStretch] = ImageStretch;
        properties[RenderPropertyKeys.H1Foreground] = H1ForegroundBrush;
        properties[RenderPropertyKeys.H2Foreground] = H2ForegroundBrush;
        properties[RenderPropertyKeys.H3Foreground] = H3ForegroundBrush;
        properties[RenderPropertyKeys.H4Foreground] = H4ForegroundBrush;
        properties[RenderPropertyKeys.H5Foreground] = H5ForegroundBrush;
        properties[RenderPropertyKeys.H6Foreground] = H6ForegroundBrush;
        properties[RenderPropertyKeys.BoldForeground] = BoldForegroundBrush;
        properties[RenderPropertyKeys.HeadingForeground] = HeadingForegroundBrush;

        properties[RenderPropertyKeys.TableBorderBrush] =
            TableBorderBrush ?? BrushHelper.CreateSubtleBrush(Foreground, 0x40);

        properties[RenderPropertyKeys.TableHeaderBackground] =
            TableHeaderBackground ?? BrushHelper.CreateFillBrush(Foreground);

        properties[RenderPropertyKeys.HorizontalRuleBrush] =
            HorizontalRuleBrush ?? BrushHelper.CreateSubtleBrush(Foreground, 0x40);

        properties[RenderPropertyKeys.CodeBackground] =
            CodeBackground ?? new SolidColorBrush(Color.FromArgb(0x1F, 0x88, 0x88, 0x88));

        properties[RenderPropertyKeys.QuoteBackground] =
            QuoteBackground ?? new SolidColorBrush(Color.FromArgb(0x14, 0x88, 0x88, 0x88));

        properties[RenderPropertyKeys.QuoteBorderBrush] =
            QuoteBorderBrush ?? BrushHelper.CreateSubtleBrush(Foreground, 0x99);

        properties[RenderPropertyKeys.BorderBrush] =
            BrushHelper.CreateSubtleBrush(Foreground, 0x40);

        properties[RenderPropertyKeys.SubtleBrush] =
            BrushHelper.CreateSubtleBrush(Foreground, 0x40);

        properties[RenderPropertyKeys.MonoFontFamily] =
            MonoFontFamily ?? new FontFamily(DefaultMonoFontFamilyName);
    }

    /// <summary>等宽字体默认值(<see cref="MonoFontFamily" /> 为空时使用)。</summary>
    private const string DefaultMonoFontFamilyName = "Consolas";

    #endregion
}
