namespace ControllerBridge;

// One settings set spans all slots. Whether it needs saving is independent of
// which row is selected; connection/busy state only controls when it can save.
internal sealed record DeviceSaveState(bool HasEdits, bool NeedsSave, string? SaveReason, string? TransferReason)
{
    public bool CanSave => NeedsSave && SaveReason is null;
    public bool CanTransfer => TransferReason is null;

    internal static DeviceSaveState Create(IEnumerable<bool> changed, bool? persisted, bool online, bool busy, bool bankTransfer)
    {
        bool edits = changed.Any(value => value), pending = edits || persisted == false;
        string? blocked = busy ? L.T("処理の完了を待ってください。", "Wait for the current operation to finish.") :
            !online ? L.T("デバイスへ再接続すると、保持している編集を保存できます。", "Reconnect the device to save the retained edits.") :
            !bankTransfer ? L.T("設定全体の保存には、同梱ファームウェアへの更新が必要です。", "Update to the bundled firmware to save the complete settings set.") : null;
        return new(edits, pending,
            blocked ?? (pending ? null : L.T("未保存の設定はありません。", "There are no unsaved settings.")),
            blocked);
    }

    internal static bool ProfileChanged(DeviceProfile draft, DeviceProfile baseline)
    {
        try { return !draft.SameContent(baseline); }
        // An incomplete name or another temporarily invalid edit must remain
        // pending. Validation belongs to saving, not to every UI keystroke.
        catch (InvalidDataException) { return true; }
        catch (ArgumentException) { return true; }
        catch (OverflowException) { return true; }
    }
}
