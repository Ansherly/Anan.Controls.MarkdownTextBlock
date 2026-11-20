using System;
using System.Threading.Tasks;
using System.Globalization;
using ColorCode;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Anan.Controls;

/// <summary>
///     代码块控件:基于 ColorCode 做语法高亮,等宽字体 + 可横向滚动,背景色由外部注入。
/// </summary>
/// <remarks>
///     横向滚动只允许拖动滚动条;滚轮(含倾斜滚轮 / Shift+滚轮)只做竖直滚动,全程不产生横向滚动。
///     滚轮必须在 RichTextBlock 一级就吃掉:模板禁用了内层 ScrollViewer 的纵向滚动,
///     放行的话它会把竖直滚轮折算成横向滚动(那是它唯一可滚的方向)。
///     滚动统一用带缓动的 ChangeView 并累加目标位置,连续滚轮既不会被合成一格,也不会跳变卡手。
/// </remarks>
public sealed partial class CodeBlock : Control
{
    private const string ViewerPartName = "PART_Viewer";
    private const string LanguageTagPartName = "PART_LanguageTag";
    private const string ScrollerPartName = "PART_Scroller";
    private const string CopyButtonPartName = "PART_CopyButton";
    private const string CopyIconPartName = "PART_CopyIcon";

    /// <summary>
    ///     滚轮转发步长:WinUI ScrollViewer 的原生步长为固定 3 行 = 48 DIP/格(与字号无关)。
    ///     转发时必须用同一数值,否则鼠标停在代码块上时的滚动速度会与其它区域不一致。
    /// </summary>
    private const double WheelStepPerNotch = 48.0;

    private TextBlock? _languageTag;
    private RichTextBlock? _viewer;
    private ScrollViewer? _scroller;
    private Button? _copyButton;
    private FontIcon? _copyIcon;
    private readonly PointerEventHandler _viewerWheelHandler;
    private readonly PointerEventHandler _scrollerWheelHandler;

    /// <summary>最近一次被驱动的外层滚动容器;为空表示还没滚过。</summary>
    private ScrollViewer? _wheelScroller;

    /// <summary>已承诺的竖直目标位置(动画进行中不能拿 VerticalOffset 当基准)。</summary>
    private double _wheelTargetOffset;

    /// <summary>本次滚轮事件是否已由内容级入口处理,避免同一事件被两个入口各滚一次。</summary>
    private bool _viewerHandledWheel;

    /// <summary>复制按钮的常态图标(Segoe Fluent Icons: Copy)。</summary>
    private const string CopyGlyph = "\uE8C8";

    /// <summary>复制成功后的反馈图标(Segoe Fluent Icons: CheckMark)。</summary>
    private const string CopiedGlyph = "\uE73E";

    /// <summary>复制反馈的代次,用于丢弃过期的"还原图标"回调。</summary>
    private int _copyFeedbackGeneration;

    public CodeBlock()
    {
        DefaultStyleKey = typeof(CodeBlock);

        // 每个实例只创建一次委托,OnApplyTemplate 里用它做幂等的摘除/挂载
        _viewerWheelHandler = OnViewerPointerWheelChanged;
        _scrollerWheelHandler = OnScrollerPointerWheelChanged;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    ///     调试用:输出内部 ScrollViewer 的视口与可滚动范围,用于排查滚动相关问题。
    /// </summary>
    public string GetScrollDebugInfo()
    {
        if (_scroller is null) return "(无 ScrollViewer)";

        return $"viewport={_scroller.ViewportWidth:0}x{_scroller.ViewportHeight:0} " +
               $"content={_scroller.ExtentWidth:0}x{_scroller.ExtentHeight:0} " +
               $"scrollable={_scroller.ScrollableWidth:0}x{_scroller.ScrollableHeight:0}";
    }

    #region 依赖属性

    public static readonly DependencyProperty CodeProperty =
        DependencyProperty.Register(
            nameof(Code),
            typeof(string),
            typeof(CodeBlock),
            new(string.Empty, OnCodeChanged));

    public static readonly DependencyProperty LanguageNameProperty =
        DependencyProperty.Register(
            nameof(LanguageName),
            typeof(string),
            typeof(CodeBlock),
            new("plaintext", OnLanguageChanged));

    /// <summary>代码文本。</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>语言标识(单行 id,如 csharp / python / plaintext)。</summary>
    public string LanguageName
    {
        get => (string)GetValue(LanguageNameProperty);
        set => SetValue(LanguageNameProperty, value);
    }

    #endregion

    #region 生命周期

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _viewer = GetTemplateChild(ViewerPartName) as RichTextBlock;
        _languageTag = GetTemplateChild(LanguageTagPartName) as TextBlock;
        _scroller = GetTemplateChild(ScrollerPartName) as ScrollViewer;
        _copyButton = GetTemplateChild(CopyButtonPartName) as Button;
        _copyIcon = GetTemplateChild(CopyIconPartName) as FontIcon;

        // 模板可能被重新应用:先摘再挂,避免同一个按钮上重复注册
        if (_copyButton is not null)
        {
            _copyButton.Click -= OnCopyClick;
            _copyButton.Click += OnCopyClick;
        }

        // 模板可能被重新应用:先摘再挂,保证同一个元素上不会重复注册滚轮处理器
        if (_viewer is not null)
        {
            _viewer.RemoveHandler(PointerWheelChangedEvent, _viewerWheelHandler);
            _viewer.AddHandler(PointerWheelChangedEvent, _viewerWheelHandler, handledEventsToo: false);

            // 主题切了要重染：ColorCode 的高亮色是构造 formatter 时定下的
            _viewer.ActualThemeChanged -= OnViewerActualThemeChanged;
            _viewer.ActualThemeChanged += OnViewerActualThemeChanged;
        }

        if (_scroller is not null)
        if (_scroller is not null)
        {
            _scroller.RemoveHandler(PointerWheelChangedEvent, _scrollerWheelHandler);
            // 内层 ScrollViewer 会吞竖直滚轮(折算成横向),所以兜底入口必须能看到已处理的事件
            _scroller.AddHandler(PointerWheelChangedEvent, _scrollerWheelHandler, handledEventsToo: true);
        }

        RenderCode();
    }

    /// <summary>系统/应用主题切换时重染：高亮色是 formatter 构造时写死的。</summary>
    private void OnViewerActualThemeChanged(FrameworkElement sender, object args) => RenderCode();

    /// <summary>
    ///     滚轮入口(内容级)。必须在内层 ScrollViewer 之前把事件吃掉:它的纵向滚动是禁用的、横向是 Auto,
    ///     竖直滚轮会被折算到唯一可滚的方向(横向)上,放行就会被吃掉并变成横向滚动。
    /// </summary>
    private void OnViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        _viewerHandledWheel = true;
        ScrollWheelToAncestor(e);
    }

    /// <summary>
    ///     兜底入口:鼠标落在内层 ScrollViewer 自己的区域(底部留白 / 横向滚动条)上时事件源不是 RichTextBlock,
    ///     内容级入口收不到,这里再统一处理一次;用标记保证同一次事件只滚一次。
    /// </summary>
    private void OnScrollerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_viewerHandledWheel)
        {
            _viewerHandledWheel = false;
            return;
        }

        ScrollWheelToAncestor(e);
    }

    /// <summary>
    ///     把滚轮位移折算成外层滚动容器的竖直滚动(带缓动,观感与原生滚轮一致)。
    ///     横向滚轮(倾斜滚轮 / Shift+滚轮)同样只折算成竖直滚动,不产生任何横向滚动。
    /// </summary>
    /// <remarks>
    ///     连续滚轮要按“已承诺的目标位置”累加:动画进行中 <see cref="ScrollViewer.VerticalOffset" />
    ///     返回的还是中间值,拿它当基准会把后面几格合掉,观感上像滚轮被吃。
    ///     滚动停稳(ViewChanged 的 IsIntermediate=false)后基准会对齐到实际位置,
    ///     所以用户自己拖滚动条 / 键盘翻页之后基准不会过期。
    /// </remarks>
    private void ScrollWheelToAncestor(PointerRoutedEventArgs e)
    {
        e.Handled = true;

        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        if (delta == 0) return;

        var ancestor = FindAncestorScrollViewer();
        if (ancestor is null) return;

        var baseOffset = ReferenceEquals(ancestor, _wheelScroller) ? _wheelTargetOffset : ancestor.VerticalOffset;
        var target = Math.Clamp(baseOffset - delta / 120.0 * WheelStepPerNotch, 0, ancestor.ScrollableHeight);

        AttachWheelScroller(ancestor);
        _wheelTargetOffset = target;
        ancestor.ChangeView(null, target, null, disableAnimation: false);
    }

    /// <summary>记住被驱动的外层滚动容器,用它停稳后的位置校准下一次滚轮的基准。</summary>
    private void AttachWheelScroller(ScrollViewer scroller)
    {
        if (ReferenceEquals(_wheelScroller, scroller)) return;

        DetachWheelScroller();
        _wheelScroller = scroller;
        _wheelTargetOffset = scroller.VerticalOffset;
        scroller.ViewChanged += OnWheelScrollerViewChanged;
    }

    private void DetachWheelScroller()
    {
        if (_wheelScroller is null) return;

        _wheelScroller.ViewChanged -= OnWheelScrollerViewChanged;
        _wheelScroller = null;
    }

    /// <summary>停稳后把基准对齐到实际位置(用户拖滚动条 / 键盘翻页也会走到这里)。</summary>
    private void OnWheelScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        if (sender is ScrollViewer scroller) _wheelTargetOffset = scroller.VerticalOffset;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachWheelScroller();

    /// <summary>
    ///     最近一个“还能竖直滚动”的外层滚动容器;从控件自身向上找,所以不会命中代码块内部的 ScrollViewer,
    ///     没有纵向可滚动范围的容器(内容已全部显示 / 禁用了纵向滚动)也会被跳过,效果接近框架的滚动链。
    /// </summary>
    private ScrollViewer? FindAncestorScrollViewer()
    {
        var parent = VisualTreeHelper.GetParent(this);
        while (parent is not null)
        {
            if (parent is ScrollViewer { ScrollableHeight: > 0 } ancestor) return ancestor;
            parent = VisualTreeHelper.GetParent(parent);
        }

        return null;
    }

    private static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeBlock codeBlock) codeBlock.RenderCode();
    }

    private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeBlock codeBlock) codeBlock.RenderCode();
    }

    #endregion

    #region 渲染

    private void RenderCode()
    {
        if (_viewer is null) return;

        var languageName = string.IsNullOrWhiteSpace(LanguageName) ? "plaintext" : LanguageName.Trim();
        var language = languageName.Equals("plaintext", StringComparison.OrdinalIgnoreCase)
            ? null
            : Languages.FindById(languageName);

        if (_languageTag is not null)
        {
            // 未指定语言 / 纯文本时也把语言名显示出来(默认 plaintext),
            // 否则标签会整块消失 —— 右上角只剩一个复制按钮,看不出语言
            var displayName = language is null ? languageName.ToLowerInvariant() : GetLanguageDisplayName(language, languageName);
            _languageTag.Text = displayName;
            _languageTag.Visibility = string.IsNullOrEmpty(displayName) ? Visibility.Collapsed : Visibility.Visible;
        }

        if (language is null)
        {
            // 未知语言 / 纯文本:等宽字体直出,不做高亮
            _viewer.Blocks.Clear();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = Code ?? string.Empty });
            _viewer.Blocks.Add(paragraph);
            return;
        }

        // ColorCode 的 formatter 是纯追加式的:渲染前必须清空已有内容,
        // 否则模板重应用/重复渲染时高亮内容会不断叠加
        _viewer.Blocks.Clear();
        // ColorCode 默认构造用的是浅色主题配色，关键字会变成深蓝，在深色底上几乎看不见；
        // 这里按控件当前实际主题取配色。
        var formatter = new RichTextBlockFormatter(
            _viewer.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);
        formatter.FormatRichTextBlock(Code ?? string.Empty, language, _viewer);
    }

    private static string GetLanguageDisplayName(ILanguage language, string languageName)
    {
        if (!string.IsNullOrEmpty(language.Name)) return language.Name;

        return languageName.ToLowerInvariant() switch
        {
            "cpp" or "c++" => "C++",
            "csharp" or "cs" => "C#",
            "javascript" or "js" => "JavaScript",
            "typescript" or "ts" => "TypeScript",
            "python" or "py" => "Python",
            "java" => "Java",
            "html" => "HTML",
            "css" => "CSS",
            "sql" => "SQL",
            "php" => "PHP",
            "ruby" or "rb" => "Ruby",
            "go" => "Go",
            "rust" => "Rust",
            "swift" => "Swift",
            "kotlin" => "Kotlin",
            "dart" => "Dart",
            "markdown" or "md" => "Markdown",
            "yaml" or "yml" => "YAML",
            "json" => "JSON",
            "xml" => "XML",
            "bash" or "sh" => "Bash",
            "powershell" or "ps" => "PowerShell",
            _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(languageName)
        };
    }

    #endregion

    #region 复制

    /// <summary>
    ///     把代码原文写入剪贴板,并给出 1.2 秒的「已复制」图标反馈。
    /// </summary>
    /// <remarks>
    ///     复制的是 <see cref="Code" /> 原文(不含高亮样式);<see cref="Clipboard.Flush" /> 让内容在进程退出后依然可用。
    /// </remarks>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        var code = Code ?? string.Empty;
        if (code.Length == 0) return;

        var package = new DataPackage();
        package.SetText(code);
        Clipboard.SetContent(package);

        // 某些会话(如无剪贴板代理的桌面)不支持 Flush,失败不影响本次复制
        try { Clipboard.Flush(); } catch { /* 忽略 */ }

        ShowCopiedFeedback();
    }

    /// <summary>把按钮图标临时换成对勾,稍后还原。</summary>
    private async void ShowCopiedFeedback()
    {
        if (_copyIcon is null) return;

        var generation = ++_copyFeedbackGeneration;
        _copyIcon.Glyph = CopiedGlyph;

        await Task.Delay(1200);

        // 期间又复制过、或模板被重新应用,则交给更新的那次处理
        if (generation == _copyFeedbackGeneration && _copyIcon is not null)
            _copyIcon.Glyph = CopyGlyph;
    }

    #endregion
}
