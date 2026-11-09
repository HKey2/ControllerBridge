namespace ControllerBridge;

// Profile IDs retain physical positions in both modes. This affects display
// only; switching the host mode must never rewrite saved or unsaved profiles.
internal static class OutputPresentation
{
    internal static uint HostMode { get; set; }
    internal static uint SelectedHostMode { get; set; }
    internal static string ModeLabel => SelectedHostMode == 2 ? $"Auto ({(XInput ? "X" : "S")})" : XInput ? "X" : "S";
    internal static bool XInput => HostMode == 1;
    private static readonly string[] switchTargets = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "−", "+", L.T("L押込", "L stick"), L.T("R押込", "R stick"), "HOME", "CAPTURE", "↑", "→", "↓", "←"];
    private static readonly string[] xboxTargets = ["X", "A", "B", "Y", "LB", "RB", "LT", "RT", "Back", "Start", L.T("L押込", "L stick"), L.T("R押込", "R stick"), "HOME / Guide", "CAPTURE (S)", "↑", "→", "↓", "←"];
    internal static string[] Targets => XInput ? xboxTargets : switchTargets;
    internal static string ControllerName => XInput ? "Xbox 360" : "Switch";
}
