using System.Threading;
using System.Windows.Forms;

namespace UUVoiceClipboard.Native;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "Local\\UUVoiceClipboard.Native", out var createdNew);
        if (!createdNew)
        {
            if (!args.Contains("--startup"))
                MessageBox.Show("已有旧版或另一个 UU 语音中转正在运行。请先右键托盘图标选择“退出”，再启动此 V1.12.0。关闭面板的叉号只会隐藏，不会退出程序。", "UU 语音中转 V1.12.0", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        var startHidden = args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(startHidden));
    }
}
