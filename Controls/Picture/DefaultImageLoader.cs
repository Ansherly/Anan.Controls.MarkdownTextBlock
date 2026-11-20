using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Anan.Controls;

/// <summary>
///     默认图片加载器:把绝对 URI 直接交给系统解码。
/// </summary>
/// <remarks>
///     不做网络请求,只适合本地 / 资源地址或已由系统缓存的远程地址;
///     应用内的常规图片加载请用 <c>SmartImageLoader</c>。
///     <para>
///         自 2026-09-19 起支持 SVG:按扩展名判断(<see cref="ImageSourceFactory.LooksLikeSvg(string)" />),
///         <c>.svg</c> 走 <see cref="SvgImageSource" />,其余仍走 <see cref="BitmapImage" />。
///         URL 后缀不可靠时请改用 <see cref="ImageSourceFactory.FromBytesAsync" /> 走内容嗅探。
///     </para>
///     <para>
///         解码结果按「地址 + 是否低分辨率」做<b>弱引用</b>缓存:同一张图在页面上重复出现时不再重复解码,
///         而图片被 GC 回收后条目自动失效、下次重新加载 —— 不会像强引用缓存那样把整页图片常驻内存。
///     </para>
/// </remarks>
public class DefaultImageLoader : IImageLoader
{
    /// <summary>弱引用缓存:目标被回收后条目失效(见 <see cref="PurgeDeadEntries" />)。</summary>
    private static readonly ConcurrentDictionary<(string Src, bool LowRes), WeakReference<ImageSource?>> Cache = new();

    /// <summary>每 N 次访问清理一次死条目,避免字典条目无界增长。</summary>
    private const int PurgeInterval = 64;

    private static int _cacheAccessCount;

    /// <summary>是否按低分辨率解码。</summary>
    public bool LowRes { get; set; }

    public async Task<ImageSource?> LoadImage(string src)
    {
        if (string.IsNullOrEmpty(src)) return null;

        if (Interlocked.Increment(ref _cacheAccessCount) % PurgeInterval == 0) PurgeDeadEntries();

        var key = (src, LowRes);
        if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached) && cached is not null)
            return cached;

        ImageSource? result = null;

        try
        {
            if (Uri.TryCreate(src, UriKind.Absolute, out var uri))
                result = await Task.FromResult(ImageSourceFactory.FromUri(uri, LowRes));
        }
        catch
        {
            result = null;
        }

        if (result is not null)
            Cache.AddOrUpdate(key, new WeakReference<ImageSource?>(result), (_, _) => new WeakReference<ImageSource?>(result));

        return result;
    }

    /// <summary>移除目标已被 GC 回收的死缓存条目。</summary>
    private static void PurgeDeadEntries()
    {
        foreach (var entry in Cache)
            if (!entry.Value.TryGetTarget(out var target) || target is null)
                Cache.TryRemove(entry.Key, out _);
    }

    /// <summary>清空缓存(例如需要强制重新解码时)。</summary>
    public static void ClearCache() => Cache.Clear();
}
