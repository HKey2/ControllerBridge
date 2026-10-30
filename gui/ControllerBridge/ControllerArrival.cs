namespace ControllerBridge;

// A generation is scoped to a bridge connection. MainForm resets the tracker
// when that bridge is lost; VID/PID alone never identifies an attachment.
internal sealed record InputConnection(uint Generation, ushort Vid, ushort Pid, ushort Revision, uint DescriptorHash, byte Protocol)
{
    internal static InputConnection From(Capabilities c) => new(c.ConnectionGeneration, c.Vid, c.Pid, c.Revision, c.DescriptorHash, c.InputProtocol);
    internal bool Matches(Capabilities c) => c.Connected && this == From(c);
    internal void WriteRegistrationGuard(byte[] begin)
    {
        Wire.W32(begin, 26, Generation); Wire.W32(begin, 30, DescriptorHash);
        Wire.W16(begin, 34, Vid); Wire.W16(begin, 36, Pid); Wire.W16(begin, 38, Revision); begin[40] = Protocol;
    }
}

internal sealed class ControllerArrivalTracker
{
    private InputConnection? handled;
    internal void Reset() => handled = null;
    internal int Observe(Capabilities? caps, bool hasProfile, int emptySlot, bool allowed)
    {
        if (caps is not { Connected: true }) { Reset(); return -1; }
        InputConnection current = InputConnection.From(caps);
        if (handled == current) return -1;
        if (hasProfile) { handled = current; return -1; }
        // Keep the arrival pending while an editor is open, drafts exist, the
        // snapshot is stale, or no usable slot is available.
        if (!allowed || emptySlot < 0) return -1;
        handled = current;
        return emptySlot;
    }
}
