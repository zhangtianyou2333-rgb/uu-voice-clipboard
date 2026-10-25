using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

internal sealed class MainForm : Form
{
    private readonly bool startHidden;
    private readonly ICompositionEditor textEditor;
    private readonly Control editorViewport;
    private Control finalText => textEditor.Control;
    private CompositionEditorSupport composition => textEditor.Composition;
    private readonly RelayFooterStatus statusLabel = new();
    private readonly Icon appIcon = RelayAppIcon.Load();
    private readonly CloseGlyphButton closeButton = new();
    private readonly RoundedActionButton clearButton = new();
    private readonly RoundedActionButton copyButton = new();
    private readonly RoundedPanel confirmedPanel = new(RelayUiTheme.CornerRadius);
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly System.Windows.Forms.Timer finalizeTimer = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer foregroundTimer = new() { Interval = 180 };
    private VoiceSessionWatcher watcher = null!;
    private PasteDispatcher dispatcher = null!;
    private bool autoPaste = UserPreferences.GetBool("MemberAutoForward", fallback: true);
    private bool finalizing;
    private bool suppressInputEvents;
    private bool hiddenForPaste;
    private bool exiting;
    private CancellationTokenSource? relayCancellation;
    private int registeredHotkeyCount;
    private bool f12Registered;
    private bool ctrlAltF12Registered;
    private bool ctrlAltSpaceRegistered;
    private bool ctrlShiftSpaceRegistered;
    private int f12RegisterError;
    private int ctrlAltF12RegisterError;
    private int ctrlAltSpaceRegisterError;
    private int ctrlShiftSpaceRegisterError;
    private long lastHotkeyRepairTick;
    private int wmHotkeyMessages;
    private int modalDepth;
    private KeyboardEntry? keyboardEntry;
    private string hotkeyStatus = "";
    private long lastHotkeyTick;
    private string lastEntrySource = "none";
    private readonly NativeDiagnostics diagnostics = new();
    private readonly PanelSessionPolicy panelSession = new();
    private readonly FinishRequestPolicy finishRequest = new();
    private bool firstTextSeen;
    private string? lastFocusObservation;
    private long lastCompositionEndTick;
    private string chromeStatus = "chrome=pending";

    internal MainForm(bool startHidden)
    {
        this.startHidden = startHidden;
        textEditor = new WebCompositionEditor();
        editorViewport = (WebCompositionEditor)textEditor;
        Text = "UU 语音中转";
        Icon = appIcon;
        Disposed += (_, _) => appIcon.Dispose();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(600, 560);
        MinimumSize = new Size(520, 430);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = RelayUiTheme.Shell;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                HideAndClear();
            }
        };

        BuildUi();
        FormClosing += HandleFormClosing;
        closeButton.Click += (_, _) => HideAndClear();
        clearButton.Click += (_, _) => ClearInputAndClipboard();
        copyButton.Click += (_, _) => RequestFinish("button");
        finalText.TextChanged += HandleTextChanged;
        finalText.KeyDown += HandleTextKeyDown;
        composition.CompositionStateChanged += (_, _) => HandleCompositionChanged();
        composition.RightAltMessage += edge =>
            diagnostics.Record($"editor_right_alt_message edge={edge} foreground={NativeMethods.GetForegroundWindow() == Handle} editor_focused={textEditor.EditorFocused}");
        if (textEditor is WebCompositionEditor browserEditor)
        {
            browserEditor.FinishRequested += () => RequestFinish("ctrl_enter");
            browserEditor.CloseRequested += HideAndClear;
            browserEditor.Failure += reason =>
            {
                diagnostics.Record($"editor_failed {reason}");
                SetStatus("输入区未就绪：请检查 WebView2，或复制诊断信息");
            };
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style |= NativeWindowChrome.WS_CAPTION | NativeWindowChrome.WS_THICKFRAME;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        chromeStatus = NativeWindowChrome.Apply(this);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _ = Handle;
        dispatcher = new PasteDispatcher();
        watcher = new VoiceSessionWatcher();
        finalizeTimer.Tick += (_, _) => FinalizeWhenQuiet();
        foregroundTimer.Tick += (_, _) =>
        {
            RepairGlobalHotkeysIfNeeded();
            if (Visible && panelSession.IsOpen)
                ObserveInputFocus();
        };
        finalizeTimer.Start();
        foregroundTimer.Start();
        BuildTrayMenu();
        RegisterGlobalHotkeys();
        StartupManager.Ensure();

        if (startHidden)
        {
            Hide();
            watcher.PanelVisible = false;
        }
        else
        {
            var generation = panelSession.Generation;
            BeginInvoke((Action)(() =>
            {
                if (!exiting && generation == panelSession.Generation)
                    StartNewInput();
            }));
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeWindowChrome.WM_NCCALCSIZE)
        {
            // Retain native frame styles for DWM, with our existing client UI
            // occupying the whole window instead of a system title bar.
            m.Result = IntPtr.Zero;
            return;
        }
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            wmHotkeyMessages++;
            diagnostics.Record($"wm_hotkey wparam={m.WParam.ToInt64()}");
            DispatchPanelEntry(KeyboardEntryAction.F12, $"wm_hotkey_{m.WParam.ToInt64()}", panelSession.Generation);
            return;
        }

        base.WndProc(ref m);
        if (m.Msg == NativeWindowChrome.WM_DWMCOMPOSITIONCHANGED && IsHandleCreated)
            chromeStatus = NativeWindowChrome.Apply(this);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            Padding = new Padding(28, 24, 28, 24),
            Margin = Padding.Empty,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        // AutoSize must include the buttons' DPI-aware preferred height.
        // A fixed 44px child row clipped 55px buttons at 125 percent DPI.
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var titleRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Location = Point.Empty,
            BackColor = RelayUiTheme.Shell
        };
        var titleAccent = new RelayAlignedLabel
        {
            AutoSize = true,
            Text = "UU",
            Font = new Font(Font.FontFamily, 21F, FontStyle.Bold),
            ForeColor = RelayUiTheme.Lime,
            Margin = new Padding(0, 0, 8, 0)
        };
        var title = new RelayAlignedLabel
        {
            AutoSize = true,
            Text = "语音中转",
            Font = new Font(Font.FontFamily, 21F, FontStyle.Bold),
            ForeColor = RelayUiTheme.Text,
            Margin = Padding.Empty
        };
        titleRow.Controls.Add(titleAccent);
        titleRow.Controls.Add(title);
        var route = new RelayAlignedLabel
        {
            AutoSize = true,
            Text = "Windows → 接收电脑   |   豆包语音 · 跨设备输入",
            ForeColor = RelayUiTheme.Muted,
            Font = new Font(Font.FontFamily, 9F),
            Padding = Padding.Empty,
            Location = new Point(0, 51)
        };
        closeButton.Size = new Size(42, 42);
        closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        closeButton.Location = new Point(header.Width - closeButton.Width, 0);
        header.Layout += (_, _) => closeButton.Location = new Point(
            Math.Max(0, header.ClientSize.Width - closeButton.Width), 0);
        header.Controls.Add(titleRow);
        header.Controls.Add(route);
        header.Controls.Add(closeButton);
        header.MouseDown += BeginWindowDrag;
        titleRow.MouseDown += BeginWindowDrag;
        titleAccent.MouseDown += BeginWindowDrag;
        title.MouseDown += BeginWindowDrag;
        route.MouseDown += BeginWindowDrag;

        confirmedPanel.Dock = DockStyle.Fill;
        confirmedPanel.Margin = Padding.Empty;
        confirmedPanel.BackColor = RelayUiTheme.Surface;
        confirmedPanel.Padding = new Padding(22, 20, 22, 14);

        finalText.Dock = DockStyle.Fill;
        if (finalText is TextBoxBase nativeEditor)
        {
            nativeEditor.Multiline = true;
            nativeEditor.WordWrap = true;
            nativeEditor.BorderStyle = BorderStyle.None;
        }
        // Keep long recognition results inside the panel. New speech text is
        // scrolled to the latest line in HandleTextChanged.
        finalText.BackColor = RelayUiTheme.Surface;
        finalText.ForeColor = RelayUiTheme.Text;
        finalText.Font = new Font(Font.FontFamily, 11.5F);
        finalText.Margin = Padding.Empty;
        finalText.Padding = new Padding(0, 6, 0, 0);
        finalText.TabStop = true;
        editorViewport.Dock = DockStyle.Fill;
        editorViewport.Margin = Padding.Empty;
        confirmedPanel.Controls.Add(editorViewport);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Padding = new Padding(0, 16, 0, 0),
            ColumnCount = 5,
            RowCount = 1,
            BackColor = RelayUiTheme.Shell
        };
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 20));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        // A self-sizing two-line control avoids the nested panel's default
        // 100px preferred height expanding this AutoSize row and both buttons.
        statusLabel.AutoSize = true;
        statusLabel.Text = "正在等待语音输入";
        statusLabel.ForeColor = RelayUiTheme.Recognition;
        statusLabel.Font = new Font(Font.FontFamily, 9F);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Margin = Padding.Empty;
        clearButton.Text = "清空";
        UpdateFinishButton();
        StyleActionButton(clearButton, false);
        StyleActionButton(copyButton, true);
        footer.Controls.Add(statusLabel, 0, 0);
        footer.Controls.Add(clearButton, 2, 0);
        footer.Controls.Add(copyButton, 4, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(confirmedPanel, 0, 1);
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
    }

    private void StyleActionButton(RoundedActionButton button, bool primary)
    {
        button.Primary = primary;
        button.ShortcutText = "";
        button.AutoSize = true;
        // TableLayoutPanel centers a non-stretched AutoSize control vertically
        // at its DPI-aware preferred height, even if another cell is taller.
        button.Dock = DockStyle.None;
        button.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        button.Margin = Padding.Empty;
        button.Padding = Padding.Empty;
        button.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
    }

    private void BuildTrayMenu()
    {
        var start = new ToolStripMenuItem("打开语音输入框（F12）");
        start.Click += (_, _) => QueueF12Fallback("tray");
        var toggle = new ToolStripMenuItem("自动转发到接收助手")
        {
            Checked = autoPaste,
            CheckOnClick = true,
            ToolTipText = "接收电脑需要运行粘贴助手；关闭时只复制纯文本"
        };
        toggle.CheckedChanged += (_, _) =>
        {
            autoPaste = toggle.Checked;
            UserPreferences.SetBool("MemberAutoForward", autoPaste);
            UpdateFinishButton();
        };
        trayMenu.Items.Add(start);
        trayMenu.Items.Add(toggle);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("复制诊断信息（不含输入文字）", null, (_, _) => CopyDiagnostics()));
        trayMenu.Items.Add(new ToolStripMenuItem("关于 V1.12.0 · 会员预览版", null, (_, _) =>
            ShowModalMessage(
                $"Windows 发送端 · WebView2 输入框{Environment.NewLine}F12 打开或找回；面板前台时请求完成。{Environment.NewLine}右 Alt 启停豆包，F12 不停止录音。{Environment.NewLine}接收端：现有 Mac 助手；Windows 助手需按教程实现。{Environment.NewLine}本地交付不代表远端文字已经插入。{Environment.NewLine}{Environment.NewLine}{hotkeyStatus}",
                "UU 语音中转 V1.12.0")));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitApplication()));
        trayIcon.Text = "UU 语音中转 V1.12.0";
        trayIcon.Icon = appIcon;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => QueueF12Fallback("tray_double_click");
    }

    private void RegisterGlobalHotkeys()
    {
        TryRegisterMissingHotkeys();

        // Keep a dedicated F12 fallback even when RegisterHotKey succeeds.
        // A successful system registration alone does not verify delivery.
        // This hook consumes F12, so WM_HOTKEY/Raw F12 can remain zero even
        // when our callback received it. Right Alt is passed through.
        keyboardEntry ??= new KeyboardEntry(_ => QueueF12Fallback(), observeRightAlt: false);
        UpdateHotkeyStatus();
    }

    private void RepairGlobalHotkeysIfNeeded()
    {
        if (exiting || !IsHandleCreated)
            return;

        var now = Environment.TickCount64;
        if (now - lastHotkeyRepairTick < 2000)
            return;

        lastHotkeyRepairTick = now;
        if (!f12Registered || !ctrlAltF12Registered || !ctrlAltSpaceRegistered || !ctrlShiftSpaceRegistered)
            TryRegisterMissingHotkeys();
    }

    private void TryRegisterMissingHotkeys()
    {
        if (!f12Registered)
        {
            f12Registered = NativeMethods.RegisterHotKey(Handle, 1,
                NativeMethods.MOD_NOREPEAT, NativeMethods.VK_F12);
            if (!f12Registered)
                f12RegisterError = Marshal.GetLastWin32Error();
        }

        if (!ctrlAltF12Registered)
        {
            ctrlAltF12Registered = NativeMethods.RegisterHotKey(
                    Handle,
                    2,
                    NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
                    NativeMethods.VK_F12);
            if (!ctrlAltF12Registered)
                ctrlAltF12RegisterError = Marshal.GetLastWin32Error();
        }

        if (!ctrlAltSpaceRegistered)
        {
            ctrlAltSpaceRegistered = NativeMethods.RegisterHotKey(
                    Handle,
                    3,
                    NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
                    NativeMethods.VK_SPACE);
            if (!ctrlAltSpaceRegistered)
                ctrlAltSpaceRegisterError = Marshal.GetLastWin32Error();
        }

        if (!ctrlShiftSpaceRegistered)
        {
            ctrlShiftSpaceRegistered = NativeMethods.RegisterHotKey(
                    Handle,
                    4,
                    NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                    NativeMethods.VK_SPACE);
            if (!ctrlShiftSpaceRegistered)
                ctrlShiftSpaceRegisterError = Marshal.GetLastWin32Error();
        }

        registeredHotkeyCount = (f12Registered ? 1 : 0) + (ctrlAltF12Registered ? 1 : 0);
        UpdateHotkeyStatus();
    }

    private void UpdateHotkeyStatus()
    {
        hotkeyStatus = string.Join(Environment.NewLine,
            $"F12：系统热键 {(f12Registered ? "已注册" : $"未注册（错误 {f12RegisterError}）")}",
            $"Ctrl+Alt+F12：系统热键 {(ctrlAltF12Registered ? "已注册" : $"未注册（错误 {ctrlAltF12RegisterError}）")}",
            $"Ctrl+Alt+Space：{(ctrlAltSpaceRegistered ? "已注册" : $"未注册（错误 {ctrlAltSpaceRegisterError}）")}",
            $"Ctrl+Shift+Space：{(ctrlShiftSpaceRegistered ? "已注册" : $"未注册（错误 {ctrlShiftSpaceRegisterError}）")}",
            $"F12 专用低层回退：{(keyboardEntry?.Available == true ? "已启用" : "未启用")}");
    }

    private bool EntryBlocked => finalizing || modalDepth > 0 || trayMenu.Visible;

    private void QueueF12Fallback(string source = "f12_low_level")
    {
        if (exiting || IsDisposed || !IsHandleCreated)
            return;

        var generation = panelSession.Generation;
        diagnostics.Record($"{source}_callback");
        if (EntryBlocked)
        {
            diagnostics.Record($"entry_rejected source={source} reason=busy_or_modal");
            return;
        }
        diagnostics.Record($"entry_queued source={source} generation={generation}");
        BeginInvoke((Action)(() =>
        {
            if (!exiting && !IsDisposed)
                DispatchPanelEntry(KeyboardEntryAction.F12, source, generation);
        }));
    }

    private void DispatchPanelEntry(KeyboardEntryAction key, string source, int generation, bool uuForeground = false)
    {
        var context = new PanelEntryContext(panelSession.IsOpen, Visible,
            NativeMethods.GetForegroundWindow() == Handle, EntryBlocked,
            generation == panelSession.Generation, uuForeground);
        var decision = PanelEntryPolicy.Decide(key, context);
        diagnostics.Record($"entry_dispatched source={source} open={context.IsOpen} visible={context.IsVisible} foreground={context.IsForeground} action={decision.Action} reason={decision.Reason}");
        if (decision.Action == PanelEntryAction.Ignore)
            return;

        var now = Environment.TickCount64;
        if (now - lastHotkeyTick < 400)
        {
            diagnostics.Record($"entry_rejected source={source} reason=duplicate_within_400ms");
            return;
        }
        lastHotkeyTick = now;
        if (decision.Action == PanelEntryAction.Finish)
        {
            RequestFinish(source);
            return;
        }
        if (decision.Action == PanelEntryAction.Restore)
        {
            // Do not Begin(), Capture(), clear text or reset IME/finish state.
            // This is the same input session, only brought back to the user.
            lastEntrySource = $"{source}_restore";
            ShowInputPanel();
            diagnostics.Record($"panel_restored source={source} generation={panelSession.Generation} foreground={NativeMethods.GetForegroundWindow() == Handle} editor_focused={textEditor.EditorFocused}");
            return;
        }
        StartNewInput(source);
        diagnostics.Record($"entry_applied source={source} action=StartNew generation={panelSession.Generation}");
    }

    private void StartNewInput(string source = "manual")
    {
        if (finalizing)
            return;

        diagnostics.Begin(source);
        panelSession.Begin();
        lastEntrySource = source;
        firstTextSeen = false;
        lastCompositionEndTick = 0;
        lastFocusObservation = null;
        var sourceWindow = NativeMethods.GetForegroundWindow();
        var sourceThread = NativeMethods.GetWindowThreadProcessId(sourceWindow, out _);
        if (sourceThread != 0)
            diagnostics.Record($"source_input_layout=0x{NativeMethods.GetKeyboardLayout(sourceThread).ToInt64():X}");
        watcher.ResetForManualStart();
        hiddenForPaste = false;
        ClearInputOnly();
        ShowInputPanel();
        diagnostics.Record("input_compatibility raw_enabled=False raw_registered=False");
        SetStatus("等待豆包语音文字");
        diagnostics.Record("panel_shown");
        diagnostics.Record($"editor_mode={textEditor.Mode}");
    }

    private void ShowInputPanel()
    {
        watcher.PanelVisible = true;
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
        var foreground = NativeMethods.GetForegroundWindow();
        var otherThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var ownThread = NativeMethods.GetCurrentThreadId();
        var attached = otherThread != 0 && otherThread != ownThread
            && NativeMethods.AttachThreadInput(ownThread, otherThread, true);
        try
        {
            NativeMethods.BringWindowToTop(Handle);
            NativeMethods.SetForegroundWindow(Handle);
            NativeMethods.SetFocus(finalText.Handle);
        }
        finally
        {
            if (attached) NativeMethods.AttachThreadInput(ownThread, otherThread, false);
        }
        textEditor.FocusEditor();
        ObserveInputFocus();
        diagnostics.Record($"panel_focus_result foreground={NativeMethods.GetForegroundWindow() == Handle} editor_focused={textEditor.EditorFocused}");
    }

    private void HandleTextChanged(object? sender, EventArgs e)
    {
        if (suppressInputEvents || !panelSession.IsOpen || finalText.Text.Length == 0)
            return;

        watcher.NotifyTextChanged();
        diagnostics.Record("text_changed");
        SetStatus(finishRequest.Pending ? "等待豆包确认最后文字" : "正在接收豆包文字");
        composition.ScrollToLatest();
        if (!firstTextSeen)
        {
            firstTextSeen = true;
            diagnostics.Record("first_text");
        }
    }

    private void HandleTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            RequestFinish("ctrl_enter");
        }
    }

    private void UpdateFinishButton() => copyButton.Text = autoPaste ? "完成并转发" : "完成并复制";

    private void RequestFinish(string source)
    {
        if (finalizing || !panelSession.IsOpen)
        {
            diagnostics.Record($"finish_rejected source={source} reason=busy_or_closed");
            return;
        }
        if (string.IsNullOrWhiteSpace(finalText.Text) && !composition.IsComposing)
        {
            finishRequest.Reset();
            diagnostics.Record($"finish_rejected source={source} reason=empty_text");
            SetStatus("没有可复制的文字");
            return;
        }

        finishRequest.Request(composition.IsComposing);
        diagnostics.Record($"finish_requested source={source} composing={composition.IsComposing}");
        if (finishRequest.ShouldFinish(!string.IsNullOrWhiteSpace(finalText.Text), composition.IsComposing,
            watcher.LastTextChangeTick, Environment.TickCount64))
        {
            diagnostics.Record("finalize_triggered reason=explicit_ui_committed");
            _ = FinalizeTextAsync(force: true);
        }
        else
        {
            SetStatus("等待豆包确认最后文字");
        }
    }

    private void HandleCompositionChanged()
    {
        if (!panelSession.IsOpen || suppressInputEvents)
            return;
        var wasPending = finishRequest.Pending;
        var now = Environment.TickCount64;
        finishRequest.CompositionChanged(composition.IsComposing, now);
        diagnostics.Record(composition.IsComposing ? "ime_composition_begin" : "ime_composition_end");
        if (!composition.IsComposing)
            lastCompositionEndTick = now;
        if (wasPending && !finishRequest.Pending)
        {
            diagnostics.Record("finish_cancelled_new_composition");
            SetStatus("正在接收豆包文字");
        }
        else if (finishRequest.Pending && !composition.IsComposing)
        {
            SetStatus(autoPaste ? "文字已确认，准备转发" : "文字已确认，准备复制");
        }
    }

    private void FinalizeWhenQuiet()
    {
        if (!Visible || !panelSession.IsOpen || finalizing || composition.IsComposing)
            return;

        if (finishRequest.Pending)
        {
            if (finishRequest.ShouldFinish(!string.IsNullOrWhiteSpace(finalText.Text), composition.IsComposing,
                watcher.LastTextChangeTick, Environment.TickCount64))
            {
                diagnostics.Record("finalize_triggered reason=explicit_ui_after_commit");
                _ = FinalizeTextAsync(force: true);
            }
            return;
        }
        if (!watcher.ShouldFinalize(finalText.Text, composition.IsComposing))
        {
            if (finalText.Text.Length > 0 && watcher.LastTextChangeTick > 0
                && Environment.TickCount64 - watcher.LastTextChangeTick >= 900)
                SetStatus("文字已上屏，等待自动转发");
            return;
        }
        diagnostics.Record($"finalize_triggered reason={watcher.FinalizationReason} explicit={watcher.ExplicitEndRequested}");
        _ = FinalizeTextAsync(force: false);
    }

    private async Task FinalizeTextAsync(bool force)
    {
        if (finalizing || !panelSession.IsOpen)
            return;

        if (composition.IsComposing)
        {
            SetStatus("等待豆包确认文字");
            diagnostics.Record("finalize_blocked_ime_composition");
            return;
        }

        var text = finalText.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("没有可复制的文字");
            return;
        }

        finalizing = true;
        var generation = panelSession.Generation;
        diagnostics.Record($"finalize_begin force={force} explicit={watcher.ExplicitEndRequested}");
        var now = Environment.TickCount64;
        if (watcher.LastTextChangeTick > 0)
            diagnostics.Record($"last_text_to_finalize_ms={now - watcher.LastTextChangeTick}");
        if (lastCompositionEndTick > 0)
            diagnostics.Record($"last_ime_end_to_finalize_ms={now - lastCompositionEndTick}");
        finishRequest.Reset();
        clearButton.Enabled = false;
        copyButton.Enabled = false;
        SetStatus(force ? "正在复制并同步到 UU" : "识别结束，正在复制并同步到 UU");

        try
        {
            using var cancellation = new CancellationTokenSource();
            relayCancellation = cancellation;
            var result = await dispatcher.RelayAsync(
                text,
                autoPaste,
                SetStatus,
                HideForPaste,
                stage => diagnostics.Record(stage),
                cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!panelSession.Accepts(generation))
                return;
            diagnostics.Record($"relay_done clipboard={result.ClipboardCopied} handoff={result.ReceiverHandoff}");
            if (result.ReceiverHandoff)
            {
                watcher.MarkSessionFinished();
                panelSession.Close();
                // Handoff is a local clipboard event, not a remote paste acknowledgement.
                ClearInputOnly();
                Hide();
                watcher.PanelVisible = false;
                trayIcon.ShowBalloonTip(1600, "UU 语音中转", result.Message, ToolTipIcon.Info);
            }
            else
            {
                if (hiddenForPaste)
                {
                    hiddenForPaste = false;
                    ShowInputPanel();
                }

                SetStatus(result.Message);
                // A failed attempt retains text for an explicit retry, not a
                // recurring timer-driven clipboard write or paste request.
                watcher.MarkSessionFinished();
            }
        }
        catch (OperationCanceledException)
        {
            diagnostics.Record("relay_cancelled");
            // Closing the panel is an explicit cancellation. Do not restore
            // the panel or show a stale success/failure message afterwards.
        }
        finally
        {
            relayCancellation = null;
            clearButton.Enabled = true;
            copyButton.Enabled = true;
            finalizing = false;
        }
    }

    private void HideForPaste()
    {
        hiddenForPaste = true;
        watcher.MarkSessionFinished();
        watcher.PanelVisible = false;
        Hide();
    }

    private void HideAndClear()
    {
        diagnostics.Record("panel_close_requested");
        panelSession.Close();
        relayCancellation?.Cancel();
        watcher.MarkSessionFinished();
        watcher.PanelVisible = false;
        hiddenForPaste = false;
        Hide();
        // Closing must not initiate a clipboard sync or activate a target.
        ClearInputOnly();
        diagnostics.Record("panel_hidden_no_clipboard_change");
    }

    private void ClearInputAndClipboard()
    {
        ClearInputOnly();
        ClipboardRelay.Clear();
        SetStatus("正在等待语音输入");
        textEditor.FocusEditor();
    }

    private void ClearInputOnly()
    {
        suppressInputEvents = true;
        try
        {
            textEditor.Clear();
            composition.ResetCompositionTracking();
            finishRequest.Reset();
            lastCompositionEndTick = 0;
        }
        finally
        {
            suppressInputEvents = false;
        }
    }

    private void SetStatus(string message)
    {
        if (!IsDisposed && statusLabel.Text != message)
            statusLabel.Text = message;
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !IsHandleCreated)
            return;

        NativeMethods.ReleaseCapture();
        NativeMethods.SendMessage(
            Handle,
            NativeMethods.WM_NCLBUTTONDOWN,
            (IntPtr)NativeMethods.HTCAPTION,
            IntPtr.Zero);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        return RelayUiTheme.RoundedPath(bounds, radius);
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (exiting)
            return;

        e.Cancel = true;
        HideAndClear();
    }

    private void ExitApplication()
    {
        exiting = true;
        panelSession.Close();
        relayCancellation?.Cancel();
        keyboardEntry?.Dispose();
        watcher?.Dispose();
        finalizeTimer.Stop();
        foregroundTimer.Stop();
        if (IsHandleCreated)
        {
            if (f12Registered) NativeMethods.UnregisterHotKey(Handle, 1);
            if (ctrlAltF12Registered) NativeMethods.UnregisterHotKey(Handle, 2);
            if (ctrlAltSpaceRegistered) NativeMethods.UnregisterHotKey(Handle, 3);
            if (ctrlShiftSpaceRegistered) NativeMethods.UnregisterHotKey(Handle, 4);
        }

        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        Application.Exit();
    }

    private DialogResult ShowModalMessage(string text, string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Information,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        modalDepth++;
        try { return MessageBox.Show(this, text, caption, buttons, icon, defaultButton); }
        finally { modalDepth--; }
    }

    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(diagnostics.Snapshot(BuildDiagnosticsState()), TextDataFormat.UnicodeText);
            trayIcon.ShowBalloonTip(1800, "UU 语音中转", "诊断信息已复制；不包含识别正文、窗口标题或剪贴板内容。", ToolTipIcon.Info);
        }
        catch (ExternalException)
        {
            trayIcon.ShowBalloonTip(2500, "UU 语音中转", "诊断信息复制失败：Windows 剪贴板正在被其他程序占用。", ToolTipIcon.Warning);
        }
    }

    private string BuildDiagnosticsState()
    {
        return $"version=1.12.0 entry={lastEntrySource} entry_mode=f12_compat right_alt_listener=none raw_enabled=False window_auto_open=false "
            + $"f12_registered={registeredHotkeyCount}/2 f12_system={f12Registered} f12_error={f12RegisterError} ctrl_alt_f12={ctrlAltF12Registered} ctrl_alt_f12_error={ctrlAltF12RegisterError} ctrl_alt_space={ctrlAltSpaceRegistered} ctrl_alt_space_error={ctrlAltSpaceRegisterError} ctrl_shift_space={ctrlShiftSpaceRegistered} ctrl_shift_space_error={ctrlShiftSpaceRegisterError} wm_hotkey={wmHotkeyMessages} hook_f12={keyboardEntry?.F12Events ?? 0} f12_fallback_hook={keyboardEntry?.Available == true} "
            + $"panel_open={panelSession.IsOpen} panel_visible={Visible} finalizing={finalizing} modal={modalDepth > 0} composing={composition.IsComposing} finish_pending={finishRequest.Pending} "
            + $"editor={textEditor.Mode} "
            + $"panel_foreground={NativeMethods.GetForegroundWindow() == Handle} editor_focused={textEditor.EditorFocused} "
            + $"auto_forward={autoPaste} transport=tagged_clipboard receiver_ack=unavailable "
            + $"footer=status_left_compact title=uu_accent finish_badge=none ui_dpi={DeviceDpi} footer_height={clearButton.Parent?.ClientSize.Height ?? 0} clear_height={clearButton.Height} clear_bottom={clearButton.Bottom} copy_height={copyButton.Height} copy_bottom={copyButton.Bottom} button_preferred_height={copyButton.GetPreferredSize(Size.Empty).Height} status_width={statusLabel.Width} status_height={statusLabel.Height} status_preferred_height={statusLabel.GetPreferredSize(Size.Empty).Height} "
            + $"{chromeStatus} {((WebCompositionEditor)editorViewport).Diagnostics} {composition.LayoutDiagnostics}";
    }

    private void ObserveInputFocus()
    {
        var state = $"focus_observation panel_foreground={NativeMethods.GetForegroundWindow() == Handle} "
            + $"editor_focused={textEditor.EditorFocused} native_editor_focus={HasNativeEditorFocus()} input_layout=0x{NativeMethods.GetKeyboardLayout(0).ToInt64():X}";
        if (state == lastFocusObservation)
            return;

        lastFocusObservation = state;
        diagnostics.Record(state);
    }

    private bool HasNativeEditorFocus()
    {
        var info = new NativeMethods.GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        return NativeMethods.GetGUIThreadInfo(NativeMethods.GetCurrentThreadId(), ref info)
            && info.hwndFocus == finalText.Handle;
    }

    private sealed class RoundedPanel : Panel
    {
        private readonly int radius;

        internal RoundedPanel(int radius)
        {
            this.radius = radius;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            RelayUiTheme.PaintSurface(e.Graphics, ClientSize, Parent?.BackColor ?? RelayUiTheme.Shell, graphics =>
            {
                using var path = CreatePath(ClientRectangle, (int)(radius * DeviceDpi / 96F));
                using var brush = new SolidBrush(BackColor);
                graphics.FillPath(brush, path);
                using var pen = new Pen(RelayUiTheme.Border, DeviceDpi / 96F);
                graphics.DrawPath(pen, path);
            });
        }

        private static GraphicsPath CreatePath(Rectangle bounds, int radius)
        {
            return RelayUiTheme.RoundedPath(bounds, radius);
        }
    }

    private sealed class CloseGlyphButton : Control
    {
        private bool hot;

        internal CloseGlyphButton()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);
            BackColor = RelayUiTheme.Shell;
            Cursor = Cursors.Hand;
            TabStop = false;
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.Opaque, true);
            AccessibleName = "取消并关闭面板";
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            RelayUiTheme.PaintSurface(e.Graphics, ClientSize, BackColor, PaintClose);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        private void PaintClose(Graphics graphics)
        {
            using var background = new SolidBrush(hot ? Color.FromArgb(42, 47, 50) : RelayUiTheme.Surface);
            using var path = CreateRoundedPath(new Rectangle(1, 1, Width - 2, Height - 2),
                (int)(RelayUiTheme.CornerRadius * DeviceDpi / 96F));
            graphics.FillPath(background, path);
            using var outline = new Pen(RelayUiTheme.Border, DeviceDpi / 96F);
            graphics.DrawPath(outline, path);

            using var pen = new Pen(hot ? RelayUiTheme.Lime : RelayUiTheme.Muted, 1.8F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var cx = ClientSize.Width / 2F;
            var cy = ClientSize.Height / 2F;
            var half = 6F * DeviceDpi / 96F;
            pen.Width = 1.8F * DeviceDpi / 96F;
            graphics.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
            graphics.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            return RelayUiTheme.RoundedPath(bounds, radius);
        }
    }
}
