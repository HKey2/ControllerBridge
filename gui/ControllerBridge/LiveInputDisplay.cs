namespace ControllerBridge;

// A relative delta can return to zero before one GUI poll finishes. Keep the
// last motion visible briefly; input delivery and stored field values stay live.
internal sealed class LiveInputDisplay
{
    private InputField? field;
    private int value;
    private long motionUntil;
    private bool relativeMotion;
    private bool zeroPending;

    public void Reset()
    {
        field = null; value = 0; motionUntil = 0; relativeMotion = false; zeroPending = false;
    }

    public void Observe(InputField source, int current, long now)
    {
        bool relative = (source.HidFlags & 4) != 0;
        if (relative && current == 0 && relativeMotion && now < motionUntil)
        {
            if (field?.Index == source.Index) zeroPending = true;
            return;
        }
        field = source; value = current;
        relativeMotion = relative && current != 0;
        zeroPending = false;
        motionUntil = relativeMotion ? now + 250 : 0;
    }

    public string Text(long now)
    {
        if (field is null) return L.T("入力: —", "Input: —");
        int shown = zeroPending && now >= motionUntil ? 0 : value;
        return L.T($"入力: {field.Label} = {shown}", $"Input: {field.Label} = {shown}");
    }
}
