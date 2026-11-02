namespace ControllerBridge;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            L.Initialize(ref args);
            ApplicationConfiguration.Initialize();
#pragma warning disable WFO5001
            Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
            if (args.Length != 0)
                throw new ArgumentException(L.T("起動オプションは --language ja または --language en を指定してください。", "Supported launch options: --language ja or --language en."));
            using Mutex mutex = new(true, @"Local\ControllerBridge.Configurator", out bool first);
            if (!first)
            {
                MessageBox.Show(L.T("ControllerBridgeは既に起動しています。先に開いた画面を使用してください。", "ControllerBridge is already running. Use its existing window."), "ControllerBridge");
                return 2;
            }
            using MainForm form = new();
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "ControllerBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
