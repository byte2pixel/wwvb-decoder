using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
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

    /// <summary>What the window was before <c>F11</c> made it full screen, or null when it is not.</summary>
    private (WindowState State, WindowStyle Style, ResizeMode Resize)? _beforeFullScreen;

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

    /// <summary>
    /// Takes <c>F11</c> for full screen. The key arrives here whether the page or the window
    /// has the keyboard: WebView2 raises the keys it calls accelerators, the function keys
    /// among them, as WPF key events on its control.
    /// </summary>
    /// <param name="e">The key.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key != Key.F11 || e.IsRepeat)
        {
            return;
        }

        e.Handled = true;

        // WebView2's browser process waits for this handler to return, and a window that
        // changes its size calls into that process. So the change is made after the handler.
        Dispatcher.BeginInvoke(ToggleFullScreen);
    }

    private void ToggleFullScreen()
    {
        (WindowState State, WindowStyle Style, ResizeMode Resize) next =
            _beforeFullScreen ?? (WindowState.Maximized, WindowStyle.None, ResizeMode.NoResize);
        _beforeFullScreen = _beforeFullScreen is null
            ? (WindowState, WindowStyle, ResizeMode)
            : null;

        // The window is made normal first and takes its state last, so that it is sized again
        // for the border it then has. Maximized with no border and no resize grip, it covers
        // the whole monitor it is on, the taskbar too. Maximized with a border, it stops at
        // the taskbar.
        WindowState = WindowState.Normal;
        WindowStyle = next.Style;
        ResizeMode = next.Resize;
        WindowState = next.State;
    }

    private async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WwvbDecoder",
                "WebView2"
            );
            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                    null,
                    userData
                );
                await Wv.EnsureCoreWebView2Async(environment);
                Wv.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

                // The page's keys work from the start, with no click on the page first.
                Wv.CoreWebView2.NavigationCompleted += (_, _) => Wv.Focus();
#if !DEBUG
                // A desktop app, not a browser: F5 and Ctrl+R load nothing again, F12 opens
                // no tools, and the right mouse button opens no menu. The keys still reach
                // OnPreviewKeyDown. Debug keeps them all, for work on the page.
                Wv.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
                Wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
#endif
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                Trace.TraceError("Failed to initialize WebView2: {0}", ex);
                string message =
                    "Failed to initialize WebView2. Please ensure it is installed. https://developer.microsoft.com/microsoft-edge/webview2/\r\n\r\n"
                    + ex.Message;
                Fail(message);
                return;
            }

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
            // check for index.html to avoid confusing errors when the UI is not built
            if (!File.Exists(Path.Combine(uiFolder, "index.html")))
            {
                Fail($"UI was not found in {uiFolder}.");
                return;
            }

            Wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                uiFolder,
                CoreWebView2HostResourceAccessKind.Allow
            );
            Wv.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html");
        }
        catch (Exception ex)
        {
            Trace.TraceError("Failed to initialize WebView2: {0}", ex);
            Fail($"The app could not start: {ex.Message}");
        }
    }

    private static void Fail(string message)
    {
        MessageBox.Show(message, "WWVB decoder", MessageBoxButton.OK, MessageBoxImage.Error);
        Application.Current.Shutdown(1);
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
