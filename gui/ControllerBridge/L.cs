using System.Globalization;

namespace ControllerBridge;

internal static partial class L
{
    public static bool Japanese => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja";
    public static string FontFamily => Japanese ? "Yu Gothic UI" : "Segoe UI";
    public static string T(string ja, string en) => Japanese ? ja : en;
    public static string F(string ja, string en, params object?[] values) => string.Format(CultureInfo.CurrentCulture, T(ja, en), values);
    public static void Initialize(ref string[] args)
    {
        // Normal launches follow the OS UI language. This opt-in switch lets the
        // preview and verification tools exercise both layouts without changing Windows.
        int index = Array.IndexOf(args, "--language");
        if (index < 0 || index + 1 >= args.Length) return;
        string language = args[index + 1];
        if (language is not ("ja" or "en")) throw new ArgumentException("--language must be ja or en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language == "ja" ? "ja-JP" : "en-US");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        args = args.Where((_, i) => i != index && i != index + 1).ToArray();
    }
}
