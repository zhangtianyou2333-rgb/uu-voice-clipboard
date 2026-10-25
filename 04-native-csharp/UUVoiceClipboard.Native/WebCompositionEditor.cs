using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace UUVoiceClipboard.Native;

// Only the input surface uses WebView2. The existing WinForms shell, F12
// entry, completion policy, clipboard transport and Mac protocol stay native.
internal sealed class WebCompositionEditor : Panel, ICompositionEditor
{
    private readonly WebView2 web = new() { Dock = DockStyle.Fill };
    private readonly Label loadNotice = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        Text = "正在加载输入区…", ForeColor = RelayUiTheme.Muted,
        BackColor = RelayUiTheme.Surface, Padding = new Padding(16)
    };
    private readonly System.Windows.Forms.Timer readyTimer = new() { Interval = 15000 };
    private readonly WebEditorMessagePolicy messages = new();
    private readonly List<Stream> resourceStreams = new();
    private string currentText = "";
    private string runtimeVersion = "pending";
    private bool initializing, ready, browserFocused, pendingFocus;
    private bool initializationQueued;
    private bool inputActive, browserReadOnly = true;
    private string loadPhase = "pending", lastError = "none";
    private int focusRequests, focusSkipped, pointerEvents;
    private int pageRequests, navigationStarted, navigationBlocked, navigationCompleted, messagePackets, messageRejected;
    private string navigationStatus = "pending", domState = "pending";
    private int scrollTop, scrollHeight, clientHeight, scrollRequests, scrollMoves;
    private bool hasInput;
    private string lastInputViewport = "";
    public Control Control => this;
    public CompositionEditorSupport Composition { get; }
    private bool IsPanelForeground => FindForm() is { IsHandleCreated: true } form
        && NativeMethods.GetForegroundWindow() == form.Handle;
    public bool EditorFocused => browserFocused && Visible && IsPanelForeground;
    public string Mode => "webview2_textarea";
    internal event Action<string>? Failure;
    internal event Action? FinishRequested;
    internal event Action? CloseRequested;

    internal WebCompositionEditor()
    {
        Composition = new(this);
        BackColor = RelayUiTheme.Surface;
        web.DefaultBackgroundColor = RelayUiTheme.Surface;
        Controls.Add(web);
        Controls.Add(loadNotice);
        loadNotice.BringToFront();
        readyTimer.Tick += (_, _) =>
        {
            readyTimer.Stop();
            if (!ready && !IsDisposed) Fail("ready_timeout");
        };
        Disposed += (_, _) =>
        {
            readyTimer.Dispose();
            foreach (var stream in resourceStreams) stream.Dispose();
        };
    }

    [AllowNull]
    public override string Text
    {
        get => currentText;
        set
        {
            if (string.IsNullOrEmpty(value)) Clear();
            else throw new InvalidOperationException("Browser text is owned by the input method.");
        }
    }

    private string ViewportState => $"web_viewport_epoch={messages.Epoch} web_scroll_top={scrollTop} web_scroll_height={scrollHeight} web_client_height={clientHeight} "
        + $"ime_active_scroll_requests={scrollRequests} ime_active_scroll_moves={scrollMoves}";

    internal string Diagnostics => $"viewport=browser_native web_ready={ready} web_runtime={runtimeVersion} web_epoch={messages.Epoch} "
        + $"web_phase={loadPhase} web_error={lastError} web_active={inputActive} web_readonly={browserReadOnly} "
        + $"web_focus_requests={focusRequests} web_focus_skipped={focusSkipped} web_pointer_events={pointerEvents} "
        + $"web_width={web.ClientSize.Width} web_height={web.ClientSize.Height} "
        + $"web_page_requests={pageRequests} web_navigation_started={navigationStarted} web_navigation_blocked={navigationBlocked} "
        + $"web_navigation_completed={navigationCompleted} web_navigation_status={navigationStatus} "
        + $"web_messages={messagePackets} web_source_rejected={messageRejected} web_dom={domState} "
        + (hasInput || lastInputViewport.Length == 0 ? $"web_viewport_snapshot=current {ViewportState} "
            : $"web_viewport_snapshot=last_input {lastInputViewport} ")
        + "ime_follow_mode=textarea_animation_frame";

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        QueueInitialization();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        QueueInitialization();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        QueueInitialization();
    }

    private void QueueInitialization()
    {
        // Handle creation can occur before the parent is shown/laid out and
        // before a running UI message loop. Start from a posted UI callback.
        if (initializing || initializationQueued || IsDisposed || !IsHandleCreated
            || !Visible || ClientSize.Width < 32 || ClientSize.Height < 32) return;
        initializationQueued = true;
        BeginInvoke((Action)(() =>
        {
            initializationQueued = false;
            if (initializing || IsDisposed || !Visible || ClientSize.Width < 32 || ClientSize.Height < 32) return;
            initializing = true;
            readyTimer.Start();
            _ = InitializeAsync();
        }));
    }

    private async Task InitializeAsync()
    {
        try
        {
            loadPhase = "runtime";
            CoreWebView2Environment.SetLoaderDllFolderPath(AppContext.BaseDirectory);
            web.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "UUVoiceClipboard", "WebView2"),
                IsInPrivateModeEnabled = true
            };
            await web.EnsureCoreWebView2Async();
            if (IsDisposed) return;
            var core = web.CoreWebView2;
            runtimeVersion = core.Environment.BrowserVersionString;
            core.Settings.IsScriptEnabled = true;
            core.Settings.IsWebMessageEnabled = true;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsBuiltInErrorPageEnabled = false;
            // This is an embedded offline input field, never a browser for
            // arbitrary pages or an additional microphone/clipboard client.
            core.NavigationStarting += (_, e) =>
            {
                navigationStarted++;
                e.Cancel = !WebEditorDocumentPolicy.IsEditor(e.Uri);
                if (e.Cancel) navigationBlocked++;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += Receive;
            core.ProcessFailed += (_, e) => Fail($"WebView2 {e.ProcessFailedKind}");
            core.NavigationCompleted += (_, e) =>
            {
                navigationCompleted++;
                navigationStatus = e.IsSuccess ? "success" : e.WebErrorStatus.ToString();
                if (!e.IsSuccess) Fail($"navigation_{e.WebErrorStatus}");
                else
                {
                    if (!ready) loadPhase = "bridge";
                    _ = InspectDocumentAsync();
                }
            };
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                "UUVoiceClipboard.Native.Assets.voice-editor.html")
                ?? throw new InvalidOperationException("Missing embedded editor.");
            using var reader = new StreamReader(stream);
            var pageBytes = System.Text.Encoding.UTF8.GetBytes(reader.ReadToEnd());
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All,
                CoreWebView2WebResourceRequestSourceKinds.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (WebEditorDocumentPolicy.IsEditor(e.Request.Uri))
                {
                    pageRequests++;
                    var body = new MemoryStream(pageBytes, writable: false);
                    resourceStreams.Add(body);
                    e.Response = core.Environment.CreateWebResourceResponse(body, 200, "OK",
                        "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
                }
                else
                    e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Blocked", "");
            };
            loadPhase = "page";
            readyTimer.Start();
            core.Navigate(WebEditorDocumentPolicy.Uri);
        }
        catch (Exception e)
        {
            if (!IsDisposed) Fail($"{e.GetType().Name} hr=0x{e.HResult:X8}");
        }
    }

    private async Task InspectDocumentAsync()
    {
        // Only bootstrap flags/geometry are read. No input value, composition
        // data, window title or transcript is queried by this diagnostic.
        try
        {
            var result = await web.CoreWebView2.ExecuteScriptAsync("(() => { const e = document.getElementById('editor'); const b = !!(window.chrome && window.chrome.webview); if (typeof window.uuEditorReadyPing === 'function') window.uuEditorReadyPing(); return {editor: !!e, bridge: b, script: typeof window.uuEditorReadyPing === 'function', width: e ? e.clientWidth : 0, height: e ? e.clientHeight : 0}; })()");
            if (IsDisposed) return;
            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;
            domState = $"editor_{root.GetProperty("editor").GetBoolean()}_bridge_{root.GetProperty("bridge").GetBoolean()}_script_{root.GetProperty("script").GetBoolean()}_size_{root.GetProperty("width").GetInt32()}x{root.GetProperty("height").GetInt32()}";
        }
        catch (Exception e)
        {
            if (!IsDisposed) domState = $"probe_error_0x{e.HResult:X8}";
        }
    }

    private void Fail(string reason)
    {
        if (IsDisposed) return;
        readyTimer.Stop();
        loadPhase = "failed";
        lastError = reason.Replace(' ', '_');
        ready = false;
        browserFocused = false;
        loadNotice.Text = "输入区加载失败\n请从托盘复制诊断信息；可切换旧输入框后重启。";
        loadNotice.Visible = true;
        loadNotice.BringToFront();
        // Keep composition protection if the renderer fails during speech;
        // incomplete cached text must never become an automatic paste.
        Composition.ObserveBrowserComposition(true);
        Failure?.Invoke(reason);
    }

    private void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (IsDisposed) return;
        messagePackets++;
        if (!WebEditorDocumentPolicy.IsEditor(e.Source))
        {
            messageRejected++;
            return;
        }
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var message = document.RootElement;
            var type = message.GetProperty("type").GetString();
            if (type == "ready")
            {
                if (ready) return;
                ready = true;
                readyTimer.Stop();
                loadPhase = "ready";
                lastError = "none";
                loadNotice.Visible = false;
                Post("reset");
                if (pendingFocus) TryFocusEditor();
                return;
            }
            if (!messages.Accept(message.GetProperty("epoch").GetInt32(), message.GetProperty("sequence").GetInt64()))
                return;
            browserFocused = message.GetProperty("focused").GetBoolean();
            browserReadOnly = message.GetProperty("readOnly").GetBoolean();
            if (type == "pointer") pointerEvents++;
            if (browserFocused) pendingFocus = false;
            scrollTop = message.GetProperty("scrollTop").GetInt32();
            scrollHeight = message.GetProperty("scrollHeight").GetInt32();
            clientHeight = message.GetProperty("clientHeight").GetInt32();
            scrollRequests = message.GetProperty("scrollRequests").GetInt32();
            scrollMoves = message.GetProperty("scrollMoves").GetInt32();
            var composing = message.GetProperty("composing").GetBoolean();
            if (composing) hasInput = true;
            // Begin protection before raising a preedit TextChanged. At end,
            // publish the final value before telling the finish policy it is
            // committed. The next input event can still extend the quiet time.
            if (composing) Composition.ObserveBrowserComposition(true);
            if (message.TryGetProperty("text", out var text))
            {
                var value = text.GetString() ?? "";
                if (value.Length > 0) hasInput = true;
                if (value != currentText)
                {
                    currentText = value;
                    OnTextChanged(EventArgs.Empty);
                }
            }
            Composition.ObserveBrowserComposition(composing);
            if (type == "finish" && EditorFocused) FinishRequested?.Invoke();
            if (type == "close" && Visible && !composing) CloseRequested?.Invoke();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            Fail("WebView2 invalid_editor_message");
        }
    }

    private void Post(string type)
    {
        if (!ready || IsDisposed || web.CoreWebView2 is null) return;
        try
        {
            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, epoch = messages.Epoch, active = inputActive }));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            Fail($"WebView2 command_failed hr=0x{e.HResult:X8}");
        }
    }

    public void Clear()
    {
        // Preserve geometry only, so diagnostics copied after automatic hide
        // describe the long input that just ended instead of an empty reset.
        if (hasInput) lastInputViewport = ViewportState;
        hasInput = false;
        messages.Reset();
        inputActive = false;
        browserReadOnly = true;
        pendingFocus = false;
        browserFocused = false;
        currentText = "";
        Composition.ResetCompositionTracking();
        scrollTop = scrollHeight = clientHeight = scrollRequests = scrollMoves = 0;
        Post("reset");
        OnTextChanged(EventArgs.Empty);
    }

    public void FocusEditor()
    {
        // An open session must accept a real click even when Windows refuses
        // automatic focus. Do not tie editability to Form.ActiveForm/web.Focus.
        inputActive = true;
        pendingFocus = true;
        Post("activate");
        TryFocusEditor();
    }

    private void TryFocusEditor()
    {
        if (!ready || !Visible || !IsPanelForeground)
        {
            focusSkipped++;
            return;
        }
        focusRequests++;
        web.Focus();
        Post("focus");
    }
}
