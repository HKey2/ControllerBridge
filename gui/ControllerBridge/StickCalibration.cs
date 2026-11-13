namespace ControllerBridge;

// Learn input identity from motion, never HID axis names. History ages on
// changed values. Completed directions belong only to the current source;
// changing source begins a new measurement instead of reviving old progress.
internal sealed class StickCalibration
{
    internal const int HistoryLength = 180;
    private const int MotionAverageLength = 6;
    private const uint MotionMaximumAgeUs = 500_000;
    private const uint SourceStabilityUs = 100_000;
    private const uint ReadyProtectionUs = 250_000;
    private const int HoldDurationMs = 400;
    internal readonly record struct AxisRange(double Negative, double Positive)
    {
        internal double Size => Math.Max(Negative, Positive);
        internal AxisRange Flip(bool invert) => invert ? new(Positive, Negative) : this;
    }
    internal readonly record struct StickView(AxisRange X, AxisRange Y, double LiveX, double LiveY,
        bool HasX, bool HasY, bool ReadyX, bool ReadyY, int ActiveAxis, int XColor = -1, int YColor = -1)
    {
        internal bool ShowPointer => HasX && HasY;
        internal bool RetainedX => HasX && ActiveAxis != 0;
        internal bool RetainedY => HasY && ActiveAxis != 1;
    }
    internal sealed class InputAxis(InputField field, int colorIndex)
    {
        private readonly (double Value, long Sample)[] history = new (double, long)[HistoryLength];
        private readonly (double Value, long Sample, uint Time)[] motionValues = new (double, long, uint)[MotionAverageLength];
        private int motionWrite;
        private long replacementSelection = -1;
        private uint replacementSince;
        private int replacementDirection;
        private double replacementPeak;
        private double holdAnchor;
        private int holdMilliseconds;
        private bool holdChanged;
        private long holdSample, holdSelection;
        internal readonly InputField Field = field;
        internal readonly int ColorIndex = colorIndex;
        internal int Count, Write, Value, Center, Minimum, Maximum, MotionAnchor;
        internal bool Valid, NegativeReady, PositiveReady;
        internal long Motion;
        internal double MotionMagnitude;
        internal bool Ready => NegativeReady || PositiveReady;
        internal double Span => (long)Field.Maximum - Field.Minimum;
        internal double Position => Normalize(Field, Center, Value);
        internal void Begin(bool captureCenter = false)
        {
            if (captureCenter) Center = Valid ? Value : AxisSourceList.Midpoint(Field);
            RestartMeasurement(); MotionAnchor = Center;
            holdAnchor = 0; holdMilliseconds = 0; holdSample = 0; holdChanged = false;
            Motion = 0; MotionMagnitude = 0;
            Array.Clear(motionValues); motionWrite = 0;
            replacementSelection = -1; replacementDirection = 0; replacementPeak = 0;
        }
        internal void RestartMeasurement()
        {
            Minimum = Maximum = Center;
            Count = Write = 0;
            NegativeReady = PositiveReady = false;
        }
        internal void Sample(long sample)
        {
            history[Write] = (((long)Value - Center) / Span, sample);
            Write = (Write + 1) % HistoryLength; Count = Math.Min(Count + 1, HistoryLength);
        }
        internal void SampleMotion(long sample, uint time, long selectedAt)
        {
            double value = ((long)Value - Center) / Span, magnitude = Math.Abs(value);
            int direction = Math.Sign(value);
            var previous = motionValues[(motionWrite + MotionAverageLength - 1) % MotionAverageLength];
            // A high report rate can put all six samples inside one rebound.
            // Qualify a new outward gesture by time as well as sample count.
            // Returning materially toward center, crossing center or stale
            // input breaks this gesture; a falling tail cannot start another.
            if (replacementSelection != selectedAt || magnitude < .10 || direction != replacementDirection ||
                magnitude < replacementPeak - .04 ||
                (previous.Sample > 0 && unchecked(time - previous.Time) > MotionMaximumAgeUs))
            {
                replacementSelection = selectedAt; replacementDirection = 0; replacementPeak = 0;
            }
            if (magnitude >= .10)
            {
                if (replacementDirection == 0 && (direction != Math.Sign(previous.Value) || magnitude > Math.Abs(previous.Value)))
                {
                    replacementDirection = direction; replacementSince = time;
                }
                if (replacementDirection != 0) replacementPeak = Math.Max(replacementPeak, magnitude);
            }
            motionValues[motionWrite] = (value, sample, time);
            motionWrite = (motionWrite + 1) % MotionAverageLength;
        }
        internal bool CanReplace(long selectedAt, uint time)
        {
            if (replacementSelection != selectedAt || replacementDirection == 0 ||
                unchecked(time - replacementSince) < SourceStabilityUs) return false;
            // Keep the sign until after averaging so a brief return/rebound
            // cancels instead of accumulating as intentional movement. Use
            // only fresh values since the last source selection, padded with
            // neutral: old gestures must not make a later single twitch win.
            // The short average complements the gesture duration above.
            // Unsigned subtraction also handles the device clock wrapping.
            bool Fresh(long sample, uint stamp) => sample > selectedAt && unchecked(time - stamp) <= MotionMaximumAgeUs;
            double sum = 0;
            foreach (var point in motionValues) if (Fresh(point.Sample, point.Time)) sum += point.Value;
            double average = sum / MotionAverageLength;
            if (Math.Abs(average) < .10 || Math.Sign(average) != replacementDirection) return false;
            int supporting = 0;
            foreach (var point in motionValues)
                if (Fresh(point.Sample, point.Time) && point.Value * Math.Sign(average) >= .04) supporting++;
            return supporting >= 3;
        }
        internal void ObserveHold(long sample, long selection)
        {
            double value = ((long)Value - Center) / Span;
            if (Math.Abs(value) < .25)
            {
                holdAnchor = 0; holdMilliseconds = 0; holdSample = 0; return;
            }
            if (holdSample == 0 || holdSelection != selection || Math.Abs(value - holdAnchor) > .04)
            {
                holdAnchor = value; holdMilliseconds = 0; holdSample = sample;
                holdSelection = selection; holdChanged = true;
            }
        }
        internal void AdvanceHold(int milliseconds)
        {
            if (milliseconds is < 0 or > 250) { holdMilliseconds = 0; holdChanged = true; return; }
            // The interval containing a new position may have been spent at
            // its old value. Only subsequent successful polls credit the hold.
            if (holdChanged) { holdChanged = false; return; }
            if (holdSample > 0) holdMilliseconds = Math.Min(HoldDurationMs, holdMilliseconds + milliseconds);
        }
        internal bool CanReplaceHeld(long selection) => holdSample > selection && holdMilliseconds >= HoldDurationMs;
        internal void CancelHold() { holdAnchor = 0; holdMilliseconds = 0; holdSample = 0; holdChanged = false; }
        private double HoldProgress(bool positive) => holdSample > 0 && (holdAnchor > 0) == positive
            ? holdMilliseconds / (double)HoldDurationMs : 0;
        private double Progress(bool positive, long sample)
        {
            Span<double> values = stackalloc double[HistoryLength]; int count = 0;
            double peak = 0, rawPeak = 0;
            for (int i = 0; i < Count; i++)
            {
                (double value, long stamp) = history[i]; long age = sample - stamp;
                if (age >= HistoryLength) continue;
                value *= positive ? 1 : -1;
                if (value <= 0) continue;
                values[count++] = value;
                rawPeak = Math.Max(rawPeak, value);
                peak = Math.Max(peak, value * (HistoryLength - age) / HistoryLength);
            }
            values = values[..count]; values.Sort();
            double previous = 0; int levels = 0;
            foreach (double value in values)
            {
                if (value - previous >= .04) { previous = value; levels++; }
            }
            // Reject isolated spikes and jitter around a single extreme.
            // Aging changes the displayed strength, not the number of distinct
            // raw levels: otherwise a held, jittering extreme could look like a sweep.
            if (levels >= 3 && peak >= .25) return 1;
            double movement = rawPeak == 0 ? 0 : Math.Min(1, rawPeak / .25) * (peak / rawPeak) * Math.Min(1, levels / 3.0);
            return Math.Max(movement, HoldProgress(positive));
        }
        internal AxisRange Envelope(long sample) => new(NegativeReady ? 1 : Progress(false, sample), PositiveReady ? 1 : Progress(true, sample));
        internal void UpdateReadiness(long sample)
        {
            if (!NegativeReady) NegativeReady = Progress(false, sample) >= 1;
            if (!PositiveReady) PositiveReady = Progress(true, sample) >= 1;
        }
    }
    private readonly Dictionary<ushort, InputAxis> inputs;
    private readonly Dictionary<ushort, bool> pressed;
    private readonly DeviceProfile draft;
    private readonly bool[] visited = new bool[4], confirmed = new bool[4];
    private readonly bool[] inversions = new bool[4];
    private bool buttonBlocked, haveTime, fromSaved, packetFromSaved;
    private uint lastTime;
    private ulong timeline, eventTimeline, protectedUntil;
    private bool inputSinceRefresh;
    private InputAxis? protectedAxis;
    private bool CandidateProtected => Candidate is not null && Candidate == protectedAxis &&
        timeline < protectedUntil;
    private long motionClock;
    private long selectedAt, packetSelectedAt;
    private InputAxis? pending, packetCandidate, packetPending;
    internal bool Started { get; private set; }
    internal int Step { get; private set; }
    internal long Samples { get; private set; }
    internal bool Ambiguous { get; private set; }
    internal InputAxis? Candidate { get; private set; }
    internal bool Invert => inversions[Math.Min(Step, 3)];
    internal bool Reviewing => Started && Step == 4;
    internal bool CanStart => inputs.Values.Any(a => a.Valid);
    internal bool CanConfirm => Started && !Reviewing && Candidate is not null;
    internal AxisBinding[] Result => draft.Clone().Axes;
    internal IReadOnlyCollection<InputAxis> Inputs => inputs.Values;
    internal byte InputInterval => draft.InputInterval;
    internal uint RecenterButtons { get => draft.GyroRecenterButtons; set => draft.GyroRecenterButtons = value; }
    internal AxisBinding Axis(int index) => draft.Clone().Axes[index];
    internal void SetGyro(int axis, AxisBinding binding, uint recenter)
    {
        draft.Axes[axis] = binding; draft.GyroRecenterButtons = recenter;
        // Both steering outputs use the same physical sensor. Keep their
        // independent ranges and directions, but share the latest still bias.
        foreach (AxisBinding other in draft.Axes)
            if (other.Mode == 1 && other.Analog.Gyro)
            {
                other.Analog.Center = binding.Analog.Center;
                other.Analog.NoiseQ8 = binding.Analog.NoiseQ8;
            }
        visited[axis] = confirmed[axis] = true;
        if (Step == axis) Advance();
    }
    internal bool Visited(int axis) => visited[axis];
    internal bool Confirmed(int axis) => confirmed[axis];

    internal StickCalibration(DeviceProfile original, IEnumerable<InputField> fields)
    {
        draft = original.Clone(); InputField[] all = fields.ToArray();
        InputDirections.RequireCurrentInput(all);
        // Assign colors by input identity, independently of arrival order and
        // the output axis being configured. Additional axes repeat the palette.
        inputs = all.Where(IsAnalog).OrderBy(f => f.Index)
            .Select((f, i) => new InputAxis(f, i % 4) { Value = f.Value, Valid = f.Valid && InRange(f, f.Value) })
            .ToDictionary(a => a.Field.Index);
        foreach (InputAxis axis in inputs.Values) axis.Begin(true);
        InputField[] buttons = all.Where(f => f.Learnable && f.Binary && f.UsagePage == 9).ToArray();
        InputField[] mapped = buttons.Where(f => original.Buttons.Take(4).Any(b => b.Mode == 1 && Matches(f, b))).ToArray();
        InputField[] confirmation = mapped.Length > 0 ? mapped : buttons.Where(f => f.Usage is >= 1 and <= 4).ToArray();
        if (confirmation.Length == 0) confirmation = buttons;
        pressed = confirmation.ToDictionary(f => f.Index, f => !f.Valid || f.Value != f.Minimum);
        buttonBlocked = pressed.Values.Any(p => p);
    }
    internal static bool IsAnalog(InputField f) => f.Learnable && !f.Motion && !f.Binary && !f.Hat && f.UsagePage != 9;
    internal static bool InRange(InputField f, int value) => value >= f.Minimum && value <= f.Maximum;
    internal static double Normalize(InputField f, int center, int value)
    {
        long delta = (long)value - center;
        long span = delta < 0 ? (long)center - f.Minimum : (long)f.Maximum - center;
        return span <= 0 ? 0 : Math.Clamp(delta / (double)span, -1, 1);
    }
    private static bool Matches(InputField f, DigitalBinding b) => f.Interface == b.Interface && f.ReportId == b.ReportId &&
        f.BitOffset == b.BitOffset && f.Bits == b.Bits && (f.Flags & InputDirections.SourceFlags) == (b.Flags & InputDirections.SourceFlags);
    private static bool Matches(InputField f, AnalogBinding b) => f.Interface == b.Interface && f.ReportId == b.ReportId &&
        f.BitOffset == b.BitOffset && f.Bits == b.Bits && (f.Flags & InputDirections.SourceFlags) == (b.Flags & InputDirections.SourceFlags);
    // Only assignments confirmed in this session establish an X/Y coordinate.
    // Existing profile axes are retained for Skip/Cancel, never used to infer
    // the physical layout or the direction of a newly measured input.
    internal InputAxis? Source(int axis) => confirmed[axis] && draft.Axes[axis].Mode == 1
        ? inputs.Values.FirstOrDefault(a => Matches(a.Field, draft.Axes[axis].Analog)) : null;
    internal void Start() { if (!Started) { Started = true; BeginStep(0); } }
    internal void BeginStep(int step, bool restart = false)
    {
        if (step is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(step));
        Step = step; pending = null; Ambiguous = false; haveTime = false;
        selectedAt = Samples; timeline = eventTimeline = 0; inputSinceRefresh = false; protectedAxis = null;
        foreach (InputAxis axis in inputs.Values) axis.Begin();
        Candidate = !restart && confirmed[step] ? Source(step) : null; fromSaved = Candidate is not null;
        buttonBlocked = pressed.Values.Any(p => p);
    }
    internal void SetInvert(bool value) { if (Step < 4) inversions[Step] = value; }
    internal void InputEventsLost()
    {
        if (Started && !Reviewing) BeginStep(Step, true);
    }
    internal bool Navigate(int step)
    {
        if (!Started || step is < 0 or > 3 || (!visited[step] && step != Step)) return false;
        BeginStep(step); return true;
    }
    internal void Back() { if (Started && Step > 0) BeginStep(Step - 1); }
    internal void Skip() { if (Started && !Reviewing) { visited[Step] = true; Advance(); } }
    private void Advance()
    {
        if (Step == 3) { Step = 4; Candidate = pending = null; return; }
        BeginStep(Step + 1);
    }
    internal bool Observe(InputField field, int value, uint time)
    {
        if (!InRange(field, value)) return false;
        if (pressed.TryGetValue(field.Index, out bool wasPressed))
        {
            bool down = value != field.Minimum; pressed[field.Index] = down;
            if (down) foreach (InputAxis input in inputs.Values) input.CancelHold();
            if (!down && !pressed.Values.Any(p => p)) buttonBlocked = false;
            if (down && !wasPressed && !buttonBlocked) { buttonBlocked = true; return true; }
            return false;
        }
        if (!inputs.TryGetValue(field.Index, out InputAxis? axis) || (axis.Valid && axis.Value == value)) return false;
        bool previouslyValid = axis.Valid; axis.Value = value; axis.Valid = true;
        if (!previouslyValid) axis.Begin(true);
        if (!Started) return false;
        Samples++;
        if (Reviewing || buttonBlocked) { axis.CancelHold(); return false; }
        axis.SampleMotion(Samples, time, selectedAt);
        axis.ObserveHold(Samples, selectedAt);
        if (!haveTime || time != lastTime)
        {
            eventTimeline = haveTime ? eventTimeline + unchecked(time - lastTime) : timeline;
            timeline = Math.Max(timeline, eventTimeline);
            motionClock++; lastTime = time; haveTime = true; inputSinceRefresh = true;
            packetCandidate = Candidate; packetPending = pending; packetFromSaved = fromSaved;
            packetSelectedAt = selectedAt;
        }
        if (Math.Abs((long)value - axis.MotionAnchor) >= Math.Max(1, axis.Span * .04))
        {
            axis.MotionAnchor = value;
            if (Math.Abs((long)value - axis.Center) >= axis.Span * .04)
            {
                axis.Motion = motionClock; axis.MotionMagnitude = Math.Abs((long)value - axis.Center) / axis.Span;
            }
        }
        SelectCandidate();
        if (!Ambiguous && axis == pending)
        {
            axis.Minimum = Math.Min(axis.Minimum, value); axis.Maximum = Math.Max(axis.Maximum, value);
            axis.Sample(Samples); axis.UpdateReadiness(Samples);
            SetCandidate(axis.Ready ? axis : null); fromSaved = false;
        }
        return false;
    }
    private void SetCandidate(InputAxis? value)
    {
        if (value is not null && value != Candidate) { protectedAxis = value; protectedUntil = timeline + ReadyProtectionUs; }
        Candidate = value;
    }
    // Called only after a fresh, successful poll has drained the device's
    // events. Held positions do not invent samples or age the change history.
    internal bool AdvanceHeld(int milliseconds)
    {
        if (!Started || Reviewing || buttonBlocked) return false;
        StickView before = View(Step / 2);
        if (milliseconds is >= 0 and <= 250 && !inputSinceRefresh)
            timeline += (uint)milliseconds * 1000;
        inputSinceRefresh = false;
        foreach (InputAxis axis in inputs.Values) axis.AdvanceHold(milliseconds);
        SelectCandidate(true);
        if (!Ambiguous && pending is InputAxis current)
        {
            current.Minimum = Math.Min(current.Minimum, current.Value);
            current.Maximum = Math.Max(current.Maximum, current.Value);
            current.UpdateReadiness(Samples);
            SetCandidate(current.Ready ? current : null); fromSaved = false;
        }
        return before != View(Step / 2);
    }
    private void SelectCandidate(bool held = false)
    {
        InputAxis[] moved = inputs.Values.Where(a => a.Motion > 0).OrderByDescending(a => a.Motion)
            .ThenByDescending(a => a.MotionMagnitude).ThenBy(a => a.Field.Index).ToArray();
        InputAxis? next = moved.FirstOrDefault();
        if (next is null) return;
        Ambiguous = moved.Length > 1 && moved[1].Motion == next.Motion && Math.Abs(moved[1].MotionMagnitude - next.MotionMagnitude) < .15;
        if (Ambiguous && next.Motion == motionClock && !held)
        {
            Candidate = packetCandidate; pending = packetPending; fromSaved = packetFromSaved;
            selectedAt = packetSelectedAt; return;
        }
        if (Ambiguous || next == pending) return;
        if (next == packetPending && !held)
        {
            // Report fields arrive one by one. A later field can establish that
            // the original source still dominates this same report.
            pending = packetPending; Candidate = packetCandidate; fromSaved = packetFromSaved;
            selectedAt = packetSelectedAt; return;
        }
        // Once a source is selected, preserve its range through release, hold
        // jitter and short cross-axis rebounds. A different source must show
        // several substantial signed samples before resetting the band.
        if (CandidateProtected) return;
        if ((pending is not null || Candidate is not null) &&
            !(held ? next.CanReplaceHeld(selectedAt) : next.CanReplace(selectedAt, lastTime))) return;
        // Only collect the selected source. Re-selecting an earlier input must
        // start from a fresh range, including both direction-completion flags.
        pending = next; pending.RestartMeasurement(); Candidate = null; fromSaved = false;
        selectedAt = Samples;
    }
    internal bool Confirm()
    {
        if (!CanConfirm || Candidate is not InputAxis axis) return false;
        bool profileInvert = AxisDirections.ProfileInvert(Step, Invert);
        if (fromSaved) draft.Axes[Step].Analog.Flags = (byte)((draft.Axes[Step].Analog.Flags & InputDirections.SourceFlags) | (profileInvert ? 4 : 0));
        else
        {
            ushort deadzone = draft.Axes[Step].Mode == 1 ? draft.Axes[Step].Analog.Deadzone : (ushort)5;
            int minimum = axis.NegativeReady ? axis.Minimum : axis.Center;
            int maximum = axis.PositiveReady ? axis.Maximum : axis.Center;
            if (minimum >= maximum) return false;
            draft.Axes[Step] = new AxisBinding { Mode = 1, Analog = axis.Field.Analog(minimum, axis.Center, maximum, deadzone, profileInvert) };
        }
        draft.Encode(); confirmed[Step] = visited[Step] = true; Advance(); return true;
    }
    private AxisRange SavedRange(int step, bool? invert = null)
    {
        AnalogBinding a = draft.Axes[step].Analog;
        return new AxisRange(a.Minimum < a.Center ? 1 : 0, a.Maximum > a.Center ? 1 : 0).Flip(invert ?? AxisDirections.ProfileInvert(step, (a.Flags & 4) != 0));
    }
    internal StickView View(int stick)
    {
        int ix = stick * 2, iy = ix + 1; bool active = Started && Step < 4 && Step / 2 == stick;
        InputAxis? sourceX = Source(ix), sourceY = Source(iy);
        AxisRange x = sourceX is null ? new() : SavedRange(ix), y = sourceY is null ? new() : SavedRange(iy);
        bool readyX = sourceX is not null, readyY = sourceY is not null;
        int colorX = sourceX?.ColorIndex ?? -1, colorY = sourceY?.ColorIndex ?? -1;
        bool invX = AxisDirections.ProfileInvert(ix, (draft.Axes[ix].Analog.Flags & 4) != 0),
            invY = AxisDirections.ProfileInvert(iy, (draft.Axes[iy].Analog.Flags & 4) != 0);
        if (active)
        {
            // Only the current output starts a fresh detection band. Keep every
            // other axis confirmed in this session as a subdued reference line.
            InputAxis? axis = Candidate ?? pending; bool ready = Candidate is not null;
            AxisRange range = axis is null ? new() : fromSaved && ready ? SavedRange(Step, Invert) : axis.Envelope(Samples).Flip(Invert);
            if (!ready) range = new(Math.Min(.95, range.Negative), Math.Min(.95, range.Positive));
            if (Step == ix)
            {
                x = range; readyX = ready; colorX = axis?.ColorIndex ?? -1;
            }
            else
            {
                y = range; readyY = ready; colorY = axis?.ColorIndex ?? -1;
            }
        }
        double Live(InputAxis? a, int output, bool invert)
        {
            if (a is null) return 0;
            return Normalize(a.Field, draft.Axes[output].Analog.Center, a.Value) * (invert ? -1 : 1);
        }
        double lx = Live(sourceX, ix, invX), ly = Live(sourceY, iy, invY), length = Math.Sqrt(lx * lx + ly * ly);
        if (length > 1) { lx /= length; ly /= length; }
        return new(x, y, lx, ly, sourceX is not null, sourceY is not null, readyX, readyY, active ? Step % 2 : -1, colorX, colorY);
    }
}
