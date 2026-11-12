using System.Diagnostics;
using System.IO;
using Microsoft.Web.WebView2.Core;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;
#if DEBUG
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
#endif

namespace WwvbDecoder.Host;

/// <summary>
/// Hosts the React page in WebView2 and routes its commands to the <see cref="SourceController"/>.
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

    private readonly SourceController _controller;

    /// <summary>
    /// Initializes the window and starts the WebView2 control.
    /// </summary>
    /// <param name="controller">Handles every command the page sends.</param>
    public MainWindow(SourceController controller)
    {
        _controller = controller;
        InitializeComponent();
        _ = InitializeAsync();
    }

    /// <summary>
    /// Posts one serialized message to the page. Must be called on the dispatcher thread.
    /// Dropped while the control is still starting up; the page asks for state with
    /// <c>hello</c> once it has loaded.
    /// </summary>
    /// <param name="json">The message, already serialized.</param>
    public void PostWebMessage(string json) => Wv.CoreWebView2?.PostWebMessageAsJson(json);

    private async Task InitializeAsync()
    {
        await Wv.EnsureCoreWebView2Async(null);
        Wv.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

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
            CoreWebView2HostResourceAccessKind.Allow
        );
        // The folder mapping serves files with no cache headers, so Chromium applies
        // heuristic freshness and can show the previous bundle after a rebuild.
        // Drop the disk cache before every navigation; the files are local.
        await Wv.CoreWebView2.Profile.ClearBrowsingDataAsync(
            CoreWebView2BrowsingDataKinds.DiskCache
        );
        Wv.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html");
    }

    private async void OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e
    )
    {
        string json = e.WebMessageAsJson;
        if (!CommandJson.TryParse(json, out Command? command))
        {
            Trace.TraceWarning("Ignored web message: {0}", json);
            return;
        }

        try
        {
            await _controller.HandleAsync(command);
        }
        catch (Exception ex)
        {
            // The contract says the host never throws into the page.
            Trace.TraceError("Command {0} failed: {1}", command, ex);
        }
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
