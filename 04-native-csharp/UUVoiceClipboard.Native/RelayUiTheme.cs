using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

// Visual-only controls. The native editor, keyboard and relay policies remain
// in their original classes; these controls never observe or inject input.
internal static class RelayUiTheme
{
    internal static readonly Color Shell = Color.FromArgb(10, 12, 13);
    internal static readonly Color Surface = Color.FromArgb(25, 28, 30);
    internal static readonly Color Border = Color.FromArgb(45, 49, 52);
    internal static readonly Color Text = Color.FromArgb(243, 245, 239);
    internal static readonly Color Muted = Color.FromArgb(151, 158, 162);
    internal static readonly Color Lime = Color.FromArgb(151, 245, 57);
    internal static readonly Color Recognition = Color.FromArgb(133, 202, 151);
    // Match the restrained Windows 11 frame rounding, rather than mixing
    // large cards, pill buttons and a circular close control.
    internal const int CornerRadius = 8;

    internal static void PaintSurface(Graphics destination, Size size, Color background, Action<Graphics> paint)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        const int samples = 2;
        using var bitmap = new Bitmap(size.Width * samples, size.Height * samples, PixelFormat.Format32bppPArgb);
        bitmap.SetResolution(destination.DpiX, destination.DpiY);
        using (var canvas = Graphics.FromImage(bitmap))
        {
            canvas.ScaleTransform(samples, samples);
            Smooth(canvas);
            canvas.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(background);
            canvas.FillRectangle(brush, new Rectangle(Point.Empty, size));
            paint(canvas);
        }
        // An opaque, independently rendered surface respects the destination
        // paint clip. No Clear() on WinForms' shared buffer or GDI button adapter.
        destination.InterpolationMode = InterpolationMode.HighQualityBicubic;
        destination.PixelOffsetMode = PixelOffsetMode.HighQuality;
        destination.DrawImage(bitmap, new Rectangle(Point.Empty, size),
            0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel);
    }

    internal static void DrawCenteredText(Graphics graphics, string text, Font font, Rectangle bounds, Color color)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    internal static void Smooth(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    internal static GraphicsPath RoundedPath(Rectangle bounds, int radius) =>
        RoundedPath(new RectangleF(bounds.X + 0.5F, bounds.Y + 0.5F,
            Math.Max(1, bounds.Width - 1F), Math.Max(1, bounds.Height - 1F)), radius);

    internal static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 1)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RelayStateLabel : Label
{
    internal RelayStateLabel()
    {
        AutoSize = false;
        TabStop = false;
        TextAlign = ContentAlignment.MiddleLeft;
        ForeColor = RelayUiTheme.Recognition;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Keep the state value unchanged for the existing finish guards;
        // the ellipsis is display-only, not a simulated recording animation.
        var displayText = Text == "正在识别" ? "正在识别…" : Text;
        var textBounds = new Rectangle(Padding.Left, Padding.Top,
            Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
        TextRenderer.DrawText(e.Graphics, displayText, Font, textBounds, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
            | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
    }
}

// WinForms Label adds font-dependent overhang padding by default. A shared
// no-padding renderer gives title, route and footer the same visible origin.
internal sealed class RelayAlignedLabel : Label
{
    private const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.SingleLine
        | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    internal RelayAlignedLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var measured = TextRenderer.MeasureText(Text, Font, Size.Empty, Flags);
        return new Size(measured.Width + Padding.Horizontal, measured.Height + Padding.Vertical);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bounds = new Rectangle(Padding.Left, Padding.Top,
            Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
        var horizontal = TextAlign switch
        {
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight => TextFormatFlags.Right,
            ContentAlignment.TopCenter or ContentAlignment.MiddleCenter or ContentAlignment.BottomCenter => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left
        };
        var vertical = TextAlign switch
        {
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight => TextFormatFlags.Bottom,
            ContentAlignment.MiddleLeft or ContentAlignment.MiddleCenter or ContentAlignment.MiddleRight => TextFormatFlags.VerticalCenter,
            _ => TextFormatFlags.Top
        };
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor,
            Flags | horizontal | vertical | TextFormatFlags.PreserveGraphicsClipping
            | TextFormatFlags.PreserveGraphicsTranslateTransform
            | (AutoEllipsis ? TextFormatFlags.EndEllipsis : 0));
    }
}

// Both lines have an intrinsic combined height. This control must not inherit
// a Panel/TableLayoutPanel's default size when the footer measures its row.
internal sealed class RelayFooterStatus : Control
{
    internal RelayFooterStatus()
    {
        BackColor = RelayUiTheme.Shell;
        ForeColor = RelayUiTheme.Recognition;
        TabStop = false;
        AccessibleRole = AccessibleRole.StaticText;
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public override Size GetPreferredSize(Size proposedSize) =>
        new(0, (int)Math.Round(44 * DeviceDpi / 96F));

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName = $"当前状态：{Text}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96F;
        var lineHeight = (int)Math.Round(20 * scale);
        var statusTop = (int)Math.Round(22 * scale);
        var headingBounds = new Rectangle(0, 0, Width, lineHeight);
        var statusBounds = new Rectangle(0, statusTop, Width, Math.Max(0, Height - statusTop));
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding
            | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping
            | TextFormatFlags.PreserveGraphicsTranslateTransform;
        using var headingFont = new Font(Font.FontFamily, Font.SizeInPoints + 0.5F, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, "当前状态", headingFont, headingBounds, RelayUiTheme.Muted, flags);
        TextRenderer.DrawText(e.Graphics, Text, Font, statusBounds, ForeColor, flags);
    }
}

internal sealed class RoundedActionButton : Control
{
    private bool hot;
    private bool pressed;
    internal bool Primary { get; set; }
    internal string ShortcutText { get; set; } = "";

    internal RoundedActionButton()
    {
        BackColor = RelayUiTheme.Shell;
        Cursor = Cursors.Hand;
        TabStop = false;
        AccessibleRole = AccessibleRole.PushButton;
        // Clicking an action must not steal the IME editor's focus and end an
        // unconfirmed phrase before RequestFinish can apply its guards.
        SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.Opaque | ControlStyles.StandardClick, true);
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
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        pressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        pressed = false;
        Invalidate();
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        RelayUiTheme.PaintSurface(e.Graphics, ClientSize, BackColor, PaintButton);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96F;
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetResolution(DeviceDpi, DeviceDpi);
        using var graphics = Graphics.FromImage(bitmap);
        var width = MeasureCaption(graphics) + 28 * scale;
        if (Primary && ShortcutText.Length > 0) width += 46 * scale;
        return new Size((int)Math.Ceiling(width), (int)Math.Round(44 * scale));
    }

    private float MeasureCaption(Graphics graphics) =>
        graphics.MeasureString(Text, Font, int.MaxValue, StringFormat.GenericTypographic).Width + 2 * DeviceDpi / 96F;

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize) Parent?.PerformLayout(this, nameof(Text));
        Invalidate();
    }

    private void PaintButton(Graphics graphics)
    {
        var scale = DeviceDpi / 96F;
        using var path = RelayUiTheme.RoundedPath(ClientRectangle, (int)(RelayUiTheme.CornerRadius * scale));
        var fill = Primary
            ? !Enabled ? Color.FromArgb(89, 111, 65)
                : pressed && hot ? Color.FromArgb(125, 213, 39)
                : hot ? Color.FromArgb(169, 255, 81) : RelayUiTheme.Lime
            : hot && Enabled ? Color.FromArgb(42, 47, 50) : RelayUiTheme.Surface;
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        if (!Primary)
        {
            using var pen = new Pen(RelayUiTheme.Border, scale);
            graphics.DrawPath(pen, path);
        }

        var inset = (int)Math.Round(14 * scale);
        var captionWidth = (int)Math.Ceiling(MeasureCaption(graphics));
        var textBounds = new Rectangle(inset, 0, Math.Max(0, Width - inset * 2), Height);
        var foreground = !Enabled ? Color.FromArgb(177, 185, 172)
            : Primary ? RelayUiTheme.Shell : RelayUiTheme.Text;
        if (Primary && ShortcutText.Length > 0)
        {
            var shortcutWidth = (int)Math.Round(36 * scale);
            var shortcutBounds = new Rectangle(inset + captionWidth + (int)Math.Round(10 * scale),
                (Height - (int)(26 * scale)) / 2, shortcutWidth, (int)(26 * scale));
            using var shortcutPath = RelayUiTheme.RoundedPath(shortcutBounds, (int)(RelayUiTheme.CornerRadius * scale));
            using var shortcutBrush = new SolidBrush(Color.FromArgb(Enabled ? 24 : 12, RelayUiTheme.Shell));
            graphics.FillPath(shortcutBrush, shortcutPath);
            using var shortcutFont = new Font(Font.FontFamily, 9F, FontStyle.Bold);
            RelayUiTheme.DrawCenteredText(graphics, ShortcutText, shortcutFont, shortcutBounds, foreground);
            textBounds.Width = captionWidth;
        }
        RelayUiTheme.DrawCenteredText(graphics, Text, Font, textBounds, foreground);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override AccessibleObject CreateAccessibilityInstance() => new ActionAccessibleObject(this);

    private sealed class ActionAccessibleObject : ControlAccessibleObject
    {
        private readonly RoundedActionButton button;
        internal ActionAccessibleObject(RoundedActionButton button) : base(button) => this.button = button;
        public override string DefaultAction => "按下";
        public override void DoDefaultAction()
        {
            if (button.IsDisposed || !button.Enabled || !button.Visible) return;
            if (button.InvokeRequired)
                button.BeginInvoke((Action)DoDefaultAction);
            else
                button.OnClick(EventArgs.Empty);
        }
    }
}
