namespace ControllerBridge;

// One gesture spans the first new press through release of every captured input.
// USB fields/report IDs arrive separately, so the first field cannot end learning.
internal sealed class ShiftKeyLearner(IEnumerable<InputField> fields)
{
    private readonly ButtonLearner buttons = new(fields, false);
    private readonly List<LearnedInput> inputs = [];
    private readonly HashSet<ushort> held = [];
    internal IReadOnlyList<LearnedInput> Inputs => inputs;
    internal bool Observe(InputField field, int value)
    {
        LearnedInput? input = inputs.FirstOrDefault(i => i.Field.Index == field.Index);
        if (input is null && buttons.Observe(field, value) is LearnedInput learned)
        {
            input = learned; inputs.Add(learned);
        }
        if (input is not null)
        {
            // A hat must return to neutral; rotating it is still one held input.
            bool pressed = field.Hat ? value >= field.Minimum && value <= field.Maximum
                : ShiftSettings.Pressed(input.Binding, value);
            if (pressed) held.Add(field.Index); else held.Remove(field.Index);
        }
        return inputs.Count != 0 && held.Count == 0;
    }
}
