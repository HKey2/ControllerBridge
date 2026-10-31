using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 4 || !int.TryParse(args[1], out int dpi) || dpi is not (96 or 120 or 144 or 192) || args[3] is not ("ja" or "en"))
        {
            Console.Error.WriteLine("Usage: DpiChecks <ControllerBridge.dll> <96|120|144|192> <output-directory> <ja|en> [--transitions]");
            return 2;
        }
        string directory = Path.GetFullPath(args[2]); Directory.CreateDirectory(directory);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        try { LayoutSuite.Run(Path.GetFullPath(args[0]), dpi, directory, args[3], args.Contains("--transitions")); return 0; }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString());
            Console.Error.WriteLine(ex); return 1;
        }
    }
}
