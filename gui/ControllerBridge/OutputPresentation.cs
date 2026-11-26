namespace ControllerBridge;

// Canonical output names identify fixed assignment IDs. The user-selected
// profile label set changes presentation only, independently of the host mode.
internal static class OutputPresentation
{
    internal static uint HostMode { get; set; }
    internal static uint SelectedHostMode { get; set; }
    internal static string ModeLabel => SelectedHostMode == 4 ? (HostMode == 4 ? "GC" : "GC (S)") : SelectedHostMode == 2 ? $"Auto ({(XInput ? "X" : "S")})" : SelectedHostMode == 3 ? (HostMode == 3 ? "Pro" : "Pro (S)") : XInput ? "X" : "S";
    internal static bool XInput => HostMode == 1;
    private static readonly string[] targets = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "−", "+", L.T("L押込", "L stick"), L.T("R押込", "R stick"), "HOME", "CAPTURE", "↑", "→", "↓", "←"];
    internal static string[] Targets => targets;
    internal static string ControllerName => HostMode == 4 ? "GameCube" : XInput ? "Xbox 360" : "Switch";
}
