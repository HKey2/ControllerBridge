namespace ControllerBridge;

// IDs are persisted in firmware. UI order and translated labels may change.
internal static class DisplayTypes
{
    internal const uint Off = 0, NamePeriodSlot = 1, Name = 2, DetailA = 3, DetailB = 4, DetailC = 5, SendInfo = 6, Default = Off;
    internal const uint RequiredMask = (1u << (int)Off) | (1u << (int)NamePeriodSlot) | (1u << (int)Name) | (1u << (int)DetailA) | (1u << (int)DetailB) | (1u << (int)DetailC) | (1u << (int)SendInfo);
    internal sealed record Choice(uint Id, string Label)
    {
        public override string ToString() => Label;
    }
    internal static bool IsSupported(uint id) => id is Off or NamePeriodSlot or Name or DetailA or DetailB or DetailC or SendInfo;
    internal static Choice[] Choices =>
    [
        new(Off, "OFF"),
        new(NamePeriodSlot, L.T("プロフィール名 + 更新周期 + インデックス", "Profile name + polling interval + index")),
        new(Name, L.T("プロフィール名を最大表示 + 接続モード", "Maximum profile name + output mode")),
        new(SendInfo, L.T("プロフィール名 + 送信情報", "Profile name + transmitted input")),
        new(DetailA, L.T("Detail A：VID/PID + 入出力周期 + データ量", "Detail A: VID/PID + input/output intervals + traffic")),
        new(DetailB, L.T("Detail B：VID/PID + 変換前の入力値", "Detail B: VID/PID + raw input values")),
        new(DetailC, L.T("Detail C：X/Y移動量 + R/S 入力レポート数", "Detail C: X/Y movement + R/S input report rates"))
    ];
}
