namespace ControllerBridge;

[Flags]
internal enum GyroStillnessIssue
{
    None = 0, GyroVariation = 1, AccelerationVariation = 2,
    MissingAcceleration = 4, SensorLimit = 8, InputGap = 16
}

// Integer arithmetic matches gyro.c, including timestamp wrap.
// StillnessIssue observes rejected samples/windows without changing filtering.
internal sealed class GyroFilter
{
    internal int BiasQ8 { get; private set; }
    internal int NoiseQ8 { get; private set; }
    internal int RateQ8 { get; private set; }
    internal uint QuietMicroseconds { get; private set; }
    internal bool Manual { get; private set; }
    internal bool Reliable { get; private set; }
    internal GyroStillnessIssue StillnessIssue { get; private set; }
    private int filteredQ8;
    private long quietSum;
    private uint time, heldUs, motionUs, samples;
    private readonly int[] history = new int[5], accelLow = new int[3], accelHigh = new int[3];
    private int cursor, low, high;
    private bool seen, moving, held, holdDone, pendingRecenter;
    internal readonly record struct Step(long Delta, uint Elapsed, bool Recentered, bool Calibrated);
    internal GyroFilter(int biasQ8, int noiseQ8) { BiasQ8 = biasQ8; NoiseQ8 = noiseQ8; }
    private int Band => Math.Max(8 * 256, Math.Min(50 * 256, NoiseQ8 * 2 + 2 * 256));
    private static uint Advance(uint a, uint b, uint limit) => b >= limit - a ? limit : a + b;
    private void Stop() { RateQ8 = 0; moving = false; motionUs = 0; }
    private void ForgetWindow() { samples = 0; quietSum = 0; QuietMicroseconds = 0; }
    internal void BeginCalibration() { Manual = true; seen = false; StillnessIssue = GyroStillnessIssue.None; ForgetWindow(); Stop(); }
    internal void Recenter() { pendingRecenter = true; }
    internal void SetHeld(bool value) { held = value; heldUs = 0; holdDone = value; }
    private void Learn(bool noise)
    {
        BiasQ8 = (int)((quietSum * 256 + samples / 2) / samples);
        if (noise) NoiseQ8 = Math.Min(50 * 256, Math.Max(2 * 256,
            Math.Max(BiasQ8 - low * 256, high * 256 - BiasQ8) + 256));
        Stop();
    }
    internal Step Update(int raw, ReadOnlySpan<int> accel, bool accelValid, uint timestamp, uint maximumGap, bool pressed)
    {
        StillnessIssue = GyroStillnessIssue.None;
        bool recenter = pendingRecenter || (pressed && !held);
        if (pressed != held) { heldUs = 0; holdDone = false; }
        held = pressed; pendingRecenter = false;
        if (recenter)
        {
            if (QuietMicroseconds >= 500_000 && samples >= 4 && !moving) Learn(false);
            Stop();
        }
        if (raw is < 32 or > 991)
        {
            StillnessIssue = GyroStillnessIssue.SensorLimit;
            seen = false; Reliable = false; ForgetWindow(); Stop(); return new(0, 0, recenter, false);
        }
        uint elapsed = seen ? unchecked(timestamp - time) : 0;
        if (elapsed > maximumGap)
        {
            StillnessIssue = GyroStillnessIssue.InputGap;
            elapsed = 0; seen = false; ForgetWindow(); Stop();
        }
        if (!seen) { Array.Fill(history, raw); filteredQ8 = raw * 256; cursor = 0; }
        seen = true; Reliable = true; time = timestamp;
        if (held) heldUs = Advance(heldUs, elapsed, 2_000_000);
        history[cursor] = raw; cursor = (cursor + 1) % 5;
        int count = elapsed <= 20_000 ? 5 : elapsed <= 50_000 ? 3 : 1;
        Span<int> sorted = stackalloc int[5];
        for (int i = 0; i < count; i++) sorted[i] = history[(cursor + 4 - i) % 5];
        for (int i = 1; i < count; i++) for (int j = i; j > 0 && sorted[j] < sorted[j - 1]; j--)
            (sorted[j], sorted[j - 1]) = (sorted[j - 1], sorted[j]);
        int sample = sorted[count / 2];
        if (elapsed != 0) filteredQ8 += (int)(((long)sample * 256 - filteredQ8) * elapsed / (elapsed + 15_000));

        bool pendingHold = held && !holdDone;
        int spread = Manual || pendingHold ? 64 : Math.Max(12, Math.Min(64, NoiseQ8 * 2 / 256 + 4));
        bool gyroChanged = samples != 0 && Math.Max(high, sample) - Math.Min(low, sample) > spread;
        if (gyroChanged) StillnessIssue |= GyroStillnessIssue.GyroVariation;
        bool restart = samples == 0 || gyroChanged;
        for (int i = 0; i < 3; i++)
        {
            if (accel[i] is < 0 or > 1023) accelValid = false;
            if (samples != 0 && Math.Max(accelHigh[i], accel[i]) - Math.Min(accelLow[i], accel[i]) > 8)
            {
                restart = true; StillnessIssue |= GyroStillnessIssue.AccelerationVariation;
            }
        }
        if (!accelValid)
        {
            StillnessIssue |= GyroStillnessIssue.MissingAcceleration; ForgetWindow();
        }
        else
        {
            if (restart)
            {
                ForgetWindow(); low = high = sample;
                for (int i = 0; i < 3; i++) accelLow[i] = accelHigh[i] = accel[i];
            }
            else QuietMicroseconds = Advance(QuietMicroseconds, elapsed, 4_000_000);
            low = Math.Min(low, sample); high = Math.Max(high, sample);
            for (int i = 0; i < 3; i++) { accelLow[i] = Math.Min(accelLow[i], accel[i]); accelHigh[i] = Math.Max(accelHigh[i], accel[i]); }
            quietSum += sample; samples++;
            if (samples >= 8)
            {
                bool manualReady = Manual && QuietMicroseconds >= 3_000_000;
                bool holdReady = pendingHold && heldUs >= 2_000_000 && QuietMicroseconds >= 2_000_000;
                int mean = (int)((quietSum * 256 + samples / 2) / samples);
                bool autoReady = !Manual && !pendingHold && QuietMicroseconds >= 750_000 &&
                    Math.Abs(mean - BiasQ8) <= Math.Min(64 * 256, Math.Max(24 * 256, Band * 2));
                if (manualReady || holdReady || autoReady)
                {
                    Learn(manualReady || holdReady); ForgetWindow();
                    if (manualReady || holdReady) { Manual = false; holdDone = held; }
                    return new(0, elapsed, recenter || manualReady || holdReady, manualReady || holdReady);
                }
            }
            if (QuietMicroseconds >= 4_000_000) ForgetWindow();
        }
        if (Manual || recenter || elapsed == 0) { Stop(); return new(0, elapsed, recenter, false); }
        int rate = filteredQ8 - BiasQ8, absolute = Math.Abs(rate), dead = Band;
        if (absolute <= dead) { moving = false; motionUs = 0; }
        else if (!moving)
        {
            if (absolute >= dead + Math.Max(2 * 256, dead / 3)) motionUs = Advance(motionUs, elapsed, 20_000);
            else motionUs = 0;
            moving = motionUs >= 20_000;
        }
        int output = moving ? (rate < 0 ? -(absolute - dead) : absolute - dead) : 0;
        long delta = ((long)RateQ8 + output) * elapsed / 2;
        RateQ8 = output; return new(delta, elapsed, recenter, false);
    }
}
