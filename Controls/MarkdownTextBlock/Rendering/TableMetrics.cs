using Microsoft.UI.Xaml;

namespace Anan.Controls.MarkdownTextBlock.Rendering;

/// <summary>
///     表格边框度量:最外框四条边用粗线,单元格之间的分隔线用细线。
/// </summary>
/// <remarks>
///     实现要点:单元格只画「右 + 下」两条边,最右列 / 最底行不再画,
///     外框由外层 <see cref="Microsoft.UI.Xaml.Controls.Border" /> 统一提供。这样相邻两格不会各画一条边导致线宽翻倍,
///     四条外框线也不会和格线叠加 —— 最终外框恰好 <see cref="OuterBorderThickness" />,
///     内部格线恰好 <see cref="InnerBorderThickness" />,任意 DPI 下都是整像素。
/// </remarks>
internal static class TableMetrics
{
    /// <summary>表格最外框的线宽(四条边)。</summary>
    public const double OuterBorderThickness = 2;

    /// <summary>单元格之间的内部分隔线宽。</summary>
    public const double InnerBorderThickness = 1;

    /// <summary>按单元格位置给出边框厚度:只画右 / 下,末列末行留空交给外框。</summary>
    /// <param name="isLastColumn">是否位于最后一列(算上跨列)。</param>
    /// <param name="isLastRow">是否位于最后一行。</param>
    public static Thickness CellBorderThickness(bool isLastColumn, bool isLastRow)
    {
        return new Thickness(
            0,
            0,
            isLastColumn ? 0 : InnerBorderThickness,
            isLastRow ? 0 : InnerBorderThickness);
    }
}
