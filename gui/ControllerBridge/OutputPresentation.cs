namespace ControllerBridge;

// Output names identify saved assignments and stay fixed in every host mode.
// Mode changes must never rename, reorder or rewrite saved or unsaved mappings.
internal static class OutputPresentation
{
    internal static uint HostMode { get; set; }
    internal static uint SelectedHostMode { get; set; }
    internal static string ModeLabel => SelectedHostMode == 2 ? $"Auto ({(XInput ? "X" : "S")})" : SelectedHostMode == 3 ? (HostMode == 3 ? "Pro" : "Pro (S)") : XInput ? "X" : "S";
    internal static bool XInput => HostMode == 1;
    private static readonly string[] targets = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "−", "+", L.T("L押込", "L stick"), L.T("R押込", "R stick"), "HOME", "CAPTURE", "↑", "→", "↓", "←"];
    internal static string[] Targets => targets;
    internal static string ControllerName => XInput ? "Xbox 360" : "Switch";
}
