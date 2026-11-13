namespace ControllerBridge;

// The selected inputs determine the existing wire mode. A lone analog half
// uses endpoint-neutral analog mapping; its side is remembered in GUI metadata.
internal sealed class AxisSelection
{
    private readonly int axis;
    internal AnalogBinding? Whole { get; private set; }
    internal AnalogHalfBinding?[] Halves { get; } = new AnalogHalfBinding?[2];
    internal DigitalBinding?[] Buttons { get; } = new DigitalBinding?[2];
    internal AnalogBinding? SavedGyro { get; }
    internal bool HasHalves => Halves.Any(x => x is not null);
    internal bool HasButtons => Buttons.Any(x => x is not null);

    internal AxisSelection(AxisBinding original, int axis = 0)
    {
        this.axis = axis;
        AxisBinding copy = new DeviceProfile { Flags = 0, Vid = 0x054c, Pid = 0x0268, Axes = [original, new(), new(), new()] }.Clone().Axes[0];
        if (copy.Mode == 1)
        {
            AnalogBinding a = copy.Analog;
            if (a.Gyro) SavedGyro = a;
            if (a.AssignmentSide is 1 or 2)
                Halves[a.AssignmentSide - 1] = new() { Interface = a.Interface, ReportId = a.ReportId, BitOffset = a.BitOffset,
                    Bits = a.Bits, Flags = (byte)(a.Flags & InputDirections.SourceFlags), Neutral = a.Center,
                    Maximum = a.Center == a.Minimum ? a.Maximum : a.Minimum, Deadzone = a.Deadzone };
            else Whole = a;
        }
        else if (copy.Mode == 2) { Buttons[0] = copy.Negative.Mode == 0 ? null : copy.Negative; Buttons[1] = copy.Positive.Mode == 0 ? null : copy.Positive; }
        else if (copy.Mode == 3) { Halves[0] = copy.NegativeAnalog; Halves[1] = copy.PositiveAnalog; }
    }
    private void Clear()
    {
        Whole = null; Array.Clear(Halves); Array.Clear(Buttons);
    }
    internal static bool Matches(InputField f, byte iface, byte id, ushort offset, byte bits, byte flags) =>
        f.Interface == iface && f.ReportId == id && f.BitOffset == offset && f.Bits == bits && (f.Flags & InputDirections.SourceFlags) == (flags & InputDirections.SourceFlags);
    internal static bool Matches(InputField f, AnalogBinding? a) => a is not null && Matches(f, a.Interface, a.ReportId, a.BitOffset, a.Bits, a.Flags);
    internal static bool Matches(InputField f, AnalogHalfBinding? a) => a is not null && Matches(f, a.Interface, a.ReportId, a.BitOffset, a.Bits, a.Flags);
    internal static bool Matches(InputField f, DigitalBinding? a) => a is not null && Matches(f, a.Interface, a.ReportId, a.BitOffset, a.Bits, a.Flags);

    internal void ToggleWhole(InputField f)
    {
        bool remove = Matches(f, Whole); Clear();
        if (!remove) SelectWhole(f);
    }
    internal void SelectWhole(InputField f)
    {
        Clear();
        Whole = f.Gyroscope && SavedGyro is not null ? SavedGyro : f.Analog(f.Minimum,
            f.Pressure ? f.Minimum : f.Valid ? Math.Clamp(f.Value, f.Minimum, f.Maximum) : AxisSourceList.Midpoint(f), f.Maximum, (ushort)(f.Pressure ? 0 : 5), AxisDirections.ProfileInvert(axis, false));
    }
    internal void ToggleHalf(int side, InputField f)
    {
        bool remove = Matches(f, Halves[side]);
        Whole = null; Array.Clear(Buttons);
        int neutral = f.Pressure ? f.Minimum : f.Valid ? Math.Clamp(f.Value, f.Minimum, f.Maximum) : AxisSourceList.Midpoint(f);
        Halves[side] = remove ? null : new() { Interface = f.Interface, ReportId = f.ReportId, BitOffset = f.BitOffset,
            Bits = f.Bits, Flags = f.Flags, Neutral = neutral, Maximum = neutral == f.Maximum ? f.Minimum : f.Maximum,
            Deadzone = (ushort)(f.Pressure ? 0 : 5) };
    }
    internal void SetButton(int side, DigitalBinding? button)
    {
        Whole = null; Array.Clear(Halves); Buttons[side] = button;
    }
    internal AxisBinding Build()
    {
        if (Whole is not null) return new() { Mode = 1, Analog = Whole };
        if (HasButtons) return new() { Mode = 2, Negative = Buttons[0] ?? new(), Positive = Buttons[1] ?? new() };
        if (Halves[0] is { } negative && Halves[1] is { } positive)
            return new() { Mode = 3, NegativeAnalog = negative, PositiveAnalog = positive };
        int side = Halves[0] is not null ? 0 : 1;
        if (Halves[side] is not { } half) return new();
        // Validate before reducing to an ordinary one-sided analog mapping.
        half.Write(new byte[16], 0);
        bool invert = (half.Maximum > half.Neutral) == (side == 0);
        return new() { Mode = 1, Analog = new() { Interface = half.Interface, ReportId = half.ReportId,
            BitOffset = half.BitOffset, Bits = half.Bits, Flags = (byte)(half.Flags | (invert ? 4 : 0)),
            Minimum = Math.Min(half.Neutral, half.Maximum), Center = half.Neutral, Maximum = Math.Max(half.Neutral, half.Maximum),
            Deadzone = half.Deadzone, AssignmentSide = (byte)(side + 1) } };
    }
}
