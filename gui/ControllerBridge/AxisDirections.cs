namespace ControllerBridge;

internal static class AxisDirections
{
    // UI polarity is right/up positive. The existing profile representation
    // stores left/up on side 0, so only Y needs translating at the UI boundary.
    // The USB/profile output representation is down-positive. User-facing
    // coordinates and normalized input are up-positive; this is not a device quirk.
    internal static bool ProfileInvert(int axis, bool invert) => invert ^ ((axis & 1) != 0);
    internal static string Describe(int axis, AxisBinding binding)
    {
        string text = binding.ToString();
        if (binding.Mode != 1 || binding.Analog.Gyro) return text;
        string suffix = L.T(" (反転)", " (inverted)");
        if (text.EndsWith(suffix, StringComparison.Ordinal)) text = text[..^suffix.Length];
        return text + (ProfileInvert(axis, (binding.Analog.Flags & 4) != 0) ? suffix : "");
    }
    internal static int ProfileSide(int axis, int displaySide) => (axis & 1) == 0 ? displaySide : 1 - displaySide;
    internal static string Name(int axis, int displaySide) => (axis & 1, displaySide) switch
    {
        (0, 0) => L.T("左", "Left"), (0, _) => L.T("右", "Right"),
        (_, 0) => L.T("下", "Down"), _ => L.T("上", "Up")
    };
    internal static string Label(int axis, int displaySide, bool compact = false)
    {
        string sign = displaySide == 0 ? "−" : "+";
        if (!compact) return sign + " " + Name(axis, displaySide);
        return sign + ((axis & 1, displaySide) switch { (0, 0) => "←", (0, _) => "→", (_, 0) => "↓", _ => "↑" });
    }
}
