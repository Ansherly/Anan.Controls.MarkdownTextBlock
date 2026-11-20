using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Anan.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Anan.Controls.Primitives;

/// <summary>
///     应用内通用的图片加载器:本地 / 网络统一入口,带弱引用内存缓存。
/// </summary>
/// <remarks>
///     移植自 CC98 的 <c>Controls/Primitives/SmartImageLoader</c>,去掉了对宿主应用的耦合:
///     原实现走 <c>App.Current.GetService&lt;ApiService&gt;()</c> 取网络字节、用 <c>UrlEx</c> 判断地址类型、
///     原实现走 <c>App.Current.GetService&lt;ApiService&gt;()</c> 取网络字节、用 <c>UrlEx</c> 判断地址类型、
///     并用 .NET 9 的 <c>System.Threading.Lock</c>;这里改为自带 <see cref="HttpClient" />、
///     内置地址判断,锁改回 <see langword="object" />(net8.0 目标没有 <c>Lock</c> 类型)。
///     <para>
///         返回类型是 <see cref="ImageSource" />,可同时承载位图(<c>BitmapImage</c>)与矢量图
///         (<c>SvgImageSource</c>)—— 具体类型由 <see cref="ImageSourceFactory" /> 按内容嗅探决定,本类不关心。
///     </para>
/// </remarks>
public class SmartImageLoader : IImageLoader
{
    // 单例实例
    private static SmartImageLoader? _instance;
    private static readonly object LockObject = new();

    private static readonly ConcurrentDictionary<bool, SmartImageLoader> Instances = new();

    private static readonly ConcurrentDictionary<string, WeakReference<ImageSource?>> Cache = new();

    // 缓存访问计数:周期性清理已被 GC 的死条目,防止静态缓存无界增长
    private static int _cacheAccessCount;
    private const int PurgeInterval = 64;

    /// <summary>网络图片下载用的共享客户端(重定向 / 超时交给它托管)。</summary>
    private static readonly HttpClient Http = CreateHttpClient();

    public SmartImageLoader()
    {
    }

    public SmartImageLoader(bool lowRes)
    {
        LowRes = lowRes;
    }

    // 公共属性
    public bool LowRes { get; set; }

    public static SmartImageLoader Default
    {
        get
        {
            if (_instance == null)
                lock (LockObject)
                {
                    _instance ??= new();
                }

            return _instance;
        }
    }

    public async Task<ImageSource?> LoadImage(string src)
    {
        if (string.IsNullOrEmpty(src))
            return null;

        // 每 N 次访问清理一次死条目(目标已被 GC 的弱引用),避免字典条目无限堆积
        if (Interlocked.Increment(ref _cacheAccessCount) % PurgeInterval == 0)
        {
            PurgeDeadEntries();
        }

        var cacheKey = (LowRes ? "lr:" : "hr:") + src;

        if (Cache.TryGetValue(cacheKey, out var weak) && weak.TryGetTarget(out var cached) && cached != null)
            return cached;

        ImageSource? result = null;

        try
        {
            if (IsWebUrl(src)) result = await LoadWebImageAsync(src, LowRes);
            else if (IsLocalPath(src)) result = await LoadLocalImageAsync(src, LowRes);
        }
        catch
        {
            result = null;
        }

        // 缓存
        if (result != null)
        {
            Cache.AddOrUpdate(
                cacheKey,
                new WeakReference<ImageSource?>(result),
                (k, old) => new(result));
        }

        return result;
    }

    /// <summary>
    /// 移除目标已被 GC 回收的死缓存条目。
    /// </summary>
    private static void PurgeDeadEntries()
    {
        foreach (var kv in Cache)
        {
            if (!kv.Value.TryGetTarget(out var target) || target == null)
            {
                Cache.TryRemove(kv.Key, out _);
            }
        }
    }

    public static SmartImageLoader GetInstance(bool lowRes)
    {
        return Instances.GetOrAdd(lowRes, l => new(l));
    }

    public static void ClearCache()
    {
        Cache.Clear();
    }

    #region 地址判断

    /// <summary>是否为 http(s) 地址。</summary>
    private static bool IsWebUrl(string src)
    {
        return src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
               || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     是否为本地可加载的地址:ms-appx / ms-appdata 资源、绝对路径、或以 ./ ../ 开头的相对路径。
    /// </summary>
    private static bool IsLocalPath(string src)
    {
        if (src.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase)
            || src.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase))
            return true;

        if (src.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(src)) return false;

        try
        {
            return Path.IsPathRooted(src) || src.StartsWith("./") || src.StartsWith("../") || src.StartsWith(@"..\");
        }
        catch (ArgumentException)
        {
            // 含非法路径字符(如 < > |)时不是本地路径
            return false;
        }
    }

    #endregion

    #region 加载

    /// <summary>
    ///     下载网络图片并按内容嗅探解码(SVG 走 <see cref="Microsoft.UI.Xaml.Media.Imaging.SvgImageSource" />,其余走位图)。
    /// </summary>
    private static async Task<ImageSource?> LoadWebImageAsync(string url, bool lowRes)
    {
        var bytes = await Http.GetByteArrayAsync(url);

        // 取不到内容(网络异常 / 非 2xx)时返回 null,由调用方跳过设置图片,
        // 而不是拿着 null 去解码(那样只会抛 NullReferenceException)
        if (bytes is null || bytes.Length == 0) return null;

        // 网络图片的后缀经常不可靠(带查询串 / CDN 无后缀),这里一律按内容嗅探
        return await ImageSourceFactory.FromBytesAsync(bytes, lowRes);
    }

    /// <summary>
    ///     加载本地图片(物理路径 / <c>ms-appx</c> / <c>ms-appdata</c> 资源)。
    /// </summary>
    private static async Task<ImageSource?> LoadLocalImageAsync(string path, bool lowRes)
    {
        if (path.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase))
        {
            return ImageSourceFactory.FromUri(new Uri(path), lowRes);
        }

        if (File.Exists(path))
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
            using IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read);

            if (ImageSourceFactory.LooksLikeSvg(path))
                return await ImageSourceFactory.FromStreamAsync(fileStream, lowRes);

            var bitmapImage = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            if (lowRes)
            {
                bitmapImage.DecodePixelHeight = 64;
                bitmapImage.DecodePixelWidth = 64;
            }

            await bitmapImage.SetSourceAsync(fileStream);
            return bitmapImage;
        }

        // 尝试作为 ms-appx 资源加载(TipPopup / Picture 的占位图等)
        try
        {
            return ImageSourceFactory.FromUri(new Uri($"ms-appx:///Assets/{path.TrimStart('/', '\\')}"), lowRes);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // 个别图床要求带 UA / Referer,否则直接 403
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        return client;
    }

    #endregion
}
