using System.Windows;
using WwvbDecoder.Core;
using WwvbDecoder.Host.Bridge;
using WwvbDecoder.Host.Services;

namespace WwvbDecoder.Host;

/// <summary>
/// Composition root: builds the pipeline, the bridge, the controller, and the window.
/// </summary>
public partial class App
{
    private WebMessageBridge? _bridge;
    private SourceController? _controller;

    /// <inheritdoc/>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        TimeProvider clock = TimeProvider.System;
        var pipeline = new DecoderPipeline(clock);

        // The bridge posts into the window, which needs the controller, which needs the bridge.
        // Closing over a local assigned later breaks that cycle.
        MainWindow? window = null;
        _bridge = new WebMessageBridge(
            pipeline,
            clock,
            invokeOnUi: action => Dispatcher.BeginInvoke(action),
            postJson: json => window?.PostWebMessage(json)
        );
        _controller = new SourceController(
            pipeline,
            new HostEnvironment(clock, HostEnvironment.FindCapturesDirectory()),
            _bridge
        );

        window = new MainWindow(_controller);
        MainWindow = window;
        window.Show();
    }

    /// <inheritdoc/>
    protected override void OnExit(ExitEventArgs e)
    {
        // Off the dispatcher, so the controller's awaits cannot deadlock against this thread.
        SourceController? controller = _controller;
        if (controller is not null)
        {
            Task.Run(() => controller.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(2));
        }
        _bridge?.Dispose();
        base.OnExit(e);
    }
}
