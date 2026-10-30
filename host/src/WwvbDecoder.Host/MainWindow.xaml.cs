using System.Diagnostics.CodeAnalysis;
using System.IO;
#if DEBUG
using System.Net.Http;
#endif

namespace WwvbDecoder.Host;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private const string VirtualHostName = "app.local";
    private const string UiFolderName = "wwwroot";

#if DEBUG
    [SuppressMessage(
        "csharpsquid",
        "S1075",
        Justification = "Must match server.port in ui/vite.config.ts."
    )]
    private const string DevServerUrl = "http://localhost:5173/";
#endif

    public MainWindow()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await Wv.EnsureCoreWebView2Async(null);

#if DEBUG
        // Prefer the Vite dev server (hot reload) when it's running.
        if (await IsDevServerRunningAsync())
        {
            Wv.CoreWebView2.Navigate(DevServerUrl);
            return;
        }
#endif

        // Otherwise serve the built UI copied next to the exe.
        string uiFolder = Path.Combine(AppContext.BaseDirectory, UiFolderName);
        Wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHostName,
            uiFolder,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow
        );
        Wv.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html");
    }

#if DEBUG
    private static async Task<bool> IsDevServerRunningAsync()
    {
        // ReSharper disable once ShortLivedHttpClient
        using HttpClient http = new();
        http.Timeout = TimeSpan.FromMilliseconds(500);
        try
        {
            using HttpResponseMessage response = await http.GetAsync(DevServerUrl);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
#endif
}
