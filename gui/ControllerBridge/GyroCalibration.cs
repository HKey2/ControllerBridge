namespace ControllerBridge;

// Uses the same Q8 count-us integration and receipt timestamps as the firmware.
// Steering limits are learned from movement; no unverified degrees/sec scale.
internal sealed class GyroCalibration
{
    private readonly InputField gyro;
    private readonly uint maximumGap;
    private readonly Dictionary<ushort, int> buttonBits;
    private GyroFilter filter;
    private readonly InputField?[] accelerometers;
    private long position, previewPosition;
    private GyroFilter? previewFilter;
    private int firstDirection;
    private uint stillTime, pressed;
    private long receivedAt, calibrationStartedAt, calibrationIssueAt;
    private GyroStillnessIssue lastCalibrationIssue;
    internal int CalibrationResets { get; private set; }
    internal double CalibrationSeconds => filter.QuietMicroseconds / 1_000_000.0;
    internal bool CalibrationDelayed => Calibrating && Environment.TickCount64 - calibrationStartedAt >= 8_000 &&
        (!Fresh || CalibrationSeconds < .75);
    internal GyroStillnessIssue CalibrationIssue => Calibrating &&
        Environment.TickCount64 - calibrationIssueAt < 1_200 ? lastCalibrationIssue : GyroStillnessIssue.None;
    internal AnalogBinding Binding { get; }
    internal uint RecenterButtons { get; private set; }
    internal bool Calibrating => filter.Manual;
    internal bool SensorReliable => filter.Reliable;
    internal bool Calibrated { get; private set; }
    internal bool RangeReady { get; private set; }
    internal bool RangeLearning { get; private set; }
    internal bool Fresh => receivedAt != 0 && Environment.TickCount64 - receivedAt < 500;
    internal bool CanApply => Calibrated && RangeReady && Fresh && SensorReliable && !Calibrating;
    internal double CalibrationProgress => Math.Min(1, filter.QuietMicroseconds / 3_000_000.0);
    internal int NegativePeak { get; private set; }
    internal int PositivePeak { get; private set; }
    internal bool Inverted { get => (Binding.Flags & 4) != 0; set => Binding.Flags = (byte)((Binding.Flags & ~4) | (value ? 4 : 0)); }
    internal double Position => position / 256000.0;
    internal bool BothDirections => NegativePeak <= -10_000 && PositivePeak >= 10_000;
    // Before calibration, show movement using a temporary resting estimate.
    // This visual preview never supplies saved calibration or learned limits.
    internal double DisplayOutput => Calibrated ? Output :
        Math.Clamp(previewPosition / 25_600_000_000.0, -1, 1) * (Inverted ? -1 : 1);
    internal double Output
    {
        get
        {
            double value = Position;
            double span = value < 0 ? -Binding.Minimum : Binding.Maximum;
            double distance = Math.Abs(value), dead = span * Math.Max((int)Binding.Deadzone, 5) / 100;
            double amount = distance <= dead ? 0 : Math.Min(1, (distance - dead) / (span - dead));
            return amount * Math.Sign(value) * (Inverted ? -1 : 1);
        }
    }

    internal GyroCalibration(AxisBinding original, uint recenter, IEnumerable<InputField> fields, byte inputInterval = 0)
    {
        maximumGap = (uint)Math.Max(100_000, (inputInterval + 20) * 1000);
        InputField[] inputs = fields.ToArray();
        gyro = inputs.First(f => f.Gyroscope);
        accelerometers = Enumerable.Range(1, 3).Select(i => inputs.FirstOrDefault(f => f.Accelerometer && f.Usage == i)).ToArray();
        buttonBits = inputs.Where(f => f.DualShock3 && f.UsagePage == 9 && f.Binary && f.Usage is >= 1 and <= 17)
            .ToDictionary(f => f.Index, f => f.Usage - 1);
        foreach (InputField f in inputs)
            if (f.Valid && f.Value != 0 && buttonBits.TryGetValue(f.Index, out int bit)) pressed |= 1u << bit;
        if (original.Mode == 1 && original.Analog.Gyro)
        {
            byte[] bytes = new byte[32]; original.Analog.Write(bytes, 0);
            Binding = AnalogBinding.Read(bytes, 0); Calibrated = RangeReady = true;
        }
        else Binding = new() { Interface = gyro.Interface, ReportId = gyro.ReportId, BitOffset = gyro.BitOffset,
            Bits = gyro.Bits, Flags = (byte)(gyro.Flags | 8 | 4), Minimum = -100_000, Maximum = 100_000,
            Center = 512 * 256, NoiseQ8 = 512, Deadzone = 5 };
        Binding.Deadzone = (ushort)Math.Max((int)Binding.Deadzone, 5);
        filter = new(Binding.Center, Binding.NoiseQ8);
        if (gyro.Valid && gyro.Value is >= 32 and <= 991)
            previewFilter = new(gyro.Value * 256, Binding.NoiseQ8);
        SetRecenter(recenter);
    }
    internal void SetRecenter(uint value)
    {
        RecenterButtons = value;
        bool held = value != 0 && (pressed & value) == value;
        filter.SetHeld(held); previewFilter?.SetHeld(held);
    }
    internal void StartCalibration()
    {
        if (Calibrated) previewFilter = gyro.Valid && gyro.Value is >= 32 and <= 991 ? new(gyro.Value * 256, Binding.NoiseQ8) : null;
        Calibrated = false; filter.BeginCalibration(); position = previewPosition = 0;
        calibrationStartedAt = Environment.TickCount64; calibrationIssueAt = 0;
        lastCalibrationIssue = GyroStillnessIssue.None; CalibrationResets = 0;
        previewFilter?.Recenter();
    }
    internal void RestartRange()
    {
        if (!Calibrated) return;
        Recenter(); RangeLearning = true; RangeReady = false;
        NegativePeak = PositivePeak = firstDirection = 0; stillTime = 0;
        Binding.Minimum = -100_000; Binding.Maximum = 100_000;
    }
    internal void Recenter()
    {
        position = previewPosition = 0; filter.Recenter(); previewFilter?.Recenter();
        if (RangeLearning) { NegativePeak = PositivePeak = firstDirection = 0; stillTime = 0; }
    }
    internal void EventsLost()
    {
        Calibrated = RangeReady = RangeLearning = false;
        filter = new(Binding.Center, Binding.NoiseQ8); previewFilter = null; position = previewPosition = 0;
    }
    internal void Observe(InputField field, int value, uint time)
    {
        field.Value = value; field.Valid = true;
        if (buttonBits.TryGetValue(field.Index, out int bit))
        {
            if (value != 0) pressed |= 1u << bit; else pressed &= ~(1u << bit);
        }
        if (field.Index != gyro.Index) return;
        receivedAt = Environment.TickCount64;
        Span<int> accel = stackalloc int[3]; bool valid = true;
        for (int i = 0; i < 3; i++)
        {
            if (accelerometers[i] is { Valid: true } input) accel[i] = input.Value;
            else valid = false;
        }
        bool held = RecenterButtons != 0 && (pressed & RecenterButtons) == RecenterButtons;
        if (!Calibrated)
        {
            if (previewFilter is null && value is >= 32 and <= 991)
            {
                previewFilter = new(value * 256, Binding.NoiseQ8);
                previewFilter.SetHeld(held);
            }
            if (previewFilter is not null)
            {
                GyroFilter.Step preview = previewFilter.Update(value, accel, valid, time, maximumGap, held);
                previewPosition = preview.Recentered ? 0 : Math.Clamp(previewPosition + preview.Delta,
                    -256_000_000_000_000L, 256_000_000_000_000L);
            }
        }
        bool measuring = Calibrating;
        GyroFilter.Step step = filter.Update(value, accel, valid, time, maximumGap, held);
        if (measuring && filter.StillnessIssue != GyroStillnessIssue.None)
        {
            // Keep the reason visible across UI refreshes even if the next
            // received sample has already started a new stillness window.
            lastCalibrationIssue = filter.StillnessIssue; calibrationIssueAt = Environment.TickCount64;
            if ((filter.StillnessIssue & (GyroStillnessIssue.GyroVariation |
                GyroStillnessIssue.AccelerationVariation | GyroStillnessIssue.InputGap)) != 0)
                CalibrationResets = Math.Min(9999, CalibrationResets + 1);
        }
        if (step.Calibrated)
        {
            Binding.Center = filter.BiasQ8; Binding.NoiseQ8 = (ushort)filter.NoiseQ8;
            Calibrated = true;
            if (!RangeReady) RestartRange();
        }
        if (!Calibrated) return;
        if (step.Recentered)
        {
            position = 0;
            if (RangeLearning) { NegativePeak = PositivePeak = firstDirection = 0; stillTime = 0; }
            return;
        }
        position = Math.Clamp(position + step.Delta, -256_000_000_000_000L, 256_000_000_000_000L);
        uint elapsed = step.Elapsed; int rate = filter.RateQ8;
        if (!RangeLearning) return;
        int current = (int)(position / 256000);
        NegativePeak = Math.Min(NegativePeak, current); PositivePeak = Math.Max(PositivePeak, current);
        if (firstDirection == 0 && Math.Abs(current) >= 10_000)
        {
            firstDirection = Math.Sign(current); Inverted = firstDirection > 0;
        }
        Binding.Minimum = Math.Min(-10_000, NegativePeak);
        Binding.Maximum = Math.Max(10_000, PositivePeak);
        bool center = BothDirections && Math.Abs(current) <= Math.Min(-NegativePeak, PositivePeak) / 8 && rate == 0;
        stillTime = center ? stillTime + elapsed : 0;
        if (stillTime >= 300_000) FinishRange();
    }
    internal void FinishRange()
    {
        if (!BothDirections || !Calibrated) return;
        Binding.Minimum = NegativePeak; Binding.Maximum = PositivePeak;
        RangeLearning = false; RangeReady = true; stillTime = 0;
    }
    internal AxisBinding Result => new() { Mode = 1, Analog = Binding };
}
