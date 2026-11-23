namespace ControllerBridge;

internal sealed partial class MainForm
{
    private readonly ControllerArrivalTracker controllerArrivals = new();
    private static string NoProfileSpace => L.T("プロファイル未作成のコントローラーが接続されましたが 空きスロットがありません",
        "A controller without a profile is connected, but no empty slot is available.");
    private bool HasInputProfile(Capabilities input) => persistedProfiles.Any(p => p.Matches(input)) || profiles.Any(p => p.Matches(input));
    private int FirstAvailableSlot => Array.FindIndex(profiles, p => !p.Enabled);
    private bool CanRegisterSlot(int slot) => slot >= 0 && slot < profiles.Length && !profiles[slot].Enabled;
    private bool RegistrationDestinationSaved(int slot) => CanRegisterSlot(slot) && !persistedProfiles[slot].Enabled && !dirty[slot];
    private bool CanRegisterInput => caps is { Connected: true, InvalidBinding: false } &&
        caps.ConnectionGeneration == generation && caps.FieldCount == fields.Count && fields.Any(f => f.Learnable);
    private bool InputNeedsSpace => CanRegisterInput && caps!.BankGeneration == bankGeneration && !HasInputProfile(caps) && FirstAvailableSlot < 0;

    private void HandleControllerArrival()
    {
        if (caps is not { Connected: true }) { controllerArrivals.Reset(); return; }
        if (bridge is null || !CanRegisterInput || caps.BankGeneration != bankGeneration) return;
        int slot = controllerArrivals.Observe(caps, HasInputProfile(caps), FirstAvailableSlot,
            !working && !connecting && !polling && !closing && !ModalEditorOpen);
        if (slot < 0 || slot == selected) return;
        // Selection only: the normal list event binds the empty editor and
        // scrolls it into view. No command or profile mutation is performed.
        slots.SelectedIndex = slot;
    }
}
