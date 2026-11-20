namespace Anan.Controls.MarkdownTextBlock.Common;

/// <summary>
///     渲染上下文属性键(由控件在渲染前填充)。
/// </summary>
public static class RenderPropertyKeys
{
    public const string FontSize = "FontSize";
    public const string Foreground = "Foreground";
    public const string BoldFontFamily = "BoldFontFamily";
    public const string CodeBackground = "CodeBackground";
    public const string QuoteBackground = "QuoteBackground";
    public const string QuoteBorderBrush = "QuoteBorderBrush";
    public const string ImageMaxWidth = "ImageMaxWidth";
    public const string ImageMaxHeight = "ImageMaxHeight";
    public const string HideImage = "HideImage";
    /// <summary>由 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" /> 推导的分隔线 / 边框画刷(代码块、行内代码用)。</summary>
    public const string BorderBrush = "BorderBrush";

    /// <summary>由前景色推导的更浅填充画刷(HTML 块背景等)。</summary>
    public const string SubtleBrush = "SubtleBrush";

    /// <summary>等宽字体(行内代码 / 代码块)。</summary>
    public const string MonoFontFamily = "MonoFontFamily";

    /// <summary>代码块字号。</summary>
    public const string CodeFontSize = "CodeFontSize";

    /// <summary>链接前景色(<see langword="null" /> 表示不覆盖,交由主题决定)。</summary>
    public const string LinkForeground = "LinkForeground";

    /// <summary>表格边框画刷。</summary>
    public const string TableBorderBrush = "TableBorderBrush";

    /// <summary>表头填充画刷。</summary>
    public const string TableHeaderBackground = "TableHeaderBackground";

    /// <summary>分割线画刷。</summary>
    public const string HorizontalRuleBrush = "HorizontalRuleBrush";

    /// <summary>图片拉伸方式(<see cref="Microsoft.UI.Xaml.Media.Stretch" />)。</summary>
    public const string ImageStretch = "ImageStretch";

    /// <summary>H1 的前景色(为空时继承 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" />)。</summary>
    public const string H1Foreground = "H1Foreground";

    /// <summary>H2 的前景色(为空时继承 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" />)。</summary>
    public const string H2Foreground = "H2Foreground";

    /// <summary>H3 的前景色(为空时继承 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" />)。</summary>
    public const string H3Foreground = "H3Foreground";

    /// <summary>H4 的前景色(为空时继承 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" />)。</summary>
    public const string H4Foreground = "H4Foreground";

    /// <summary>H5 的前景色(为空时继承 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" />)。</summary>
    public const string H5Foreground = "H5Foreground";

    /// <summary>H6 的前景色(为空时继承 <see cref="Microsoft.UI.Xaml.Controls.Control.Foreground" />)。</summary>
    public const string H6Foreground = "H6Foreground";

    /// <summary>粗体(**…**、&lt;b&gt;、&lt;strong&gt;)的前景色。</summary>
    public const string BoldForeground = "BoldForeground";

    /// <summary>所有标题的兜底前景色(H1–H6 未单独指定时使用)。</summary>
    public const string HeadingForeground = "HeadingForeground";

    /// <summary>按标题层级(1–6)取对应的前景色键(不含兜底)。</summary>
    /// <param name="level">标题层级,超出 1–6 时按最近的边界处理。</param>
    public static string HeadingForegroundKey(int level) => level switch
    {
        1 => H1Foreground,
        2 => H2Foreground,
        3 => H3Foreground,
        4 => H4Foreground,
        5 => H5Foreground,
        _ => H6Foreground
    };
}
