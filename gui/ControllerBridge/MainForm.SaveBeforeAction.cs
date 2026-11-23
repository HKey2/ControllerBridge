namespace ControllerBridge;

internal sealed partial class MainForm
{
    // Explicit operations share one prompt. Passive reconnect/arrival paths
    // keep drafts without saving or prompting in the background.
    private async Task<bool> SaveBeforeAction(string operation)
    {
        if (working || connecting || closing || bridge is null) return false;
        HidBridge target = bridge;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        bool save;
        try
        {
            if (closing || !ReferenceEquals(bridge, target)) return false;
            if (!GetSaveState().NeedsSave) return true;
            save = MessageBox.Show(this,
                L.F("「{0}」を続けるには、未保存の設定をデバイスに保存する必要があります。\n\n全スロットの変更を保存して続行しますか？\n「はい」：保存して続行\n「いいえ」：この操作を中止（編集内容は保持）",
                    "Unsaved settings must be saved to the device before “{0}” can continue.\n\nSave changes across all slots and continue?\nYes: save and continue.\nNo: cancel this operation and keep your edits.", operation),
                L.T("保存して続行", "Save and continue"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        finally { working = false; UpdateEnabled(); }
        if (!save || closing || !ReferenceEquals(bridge, target)) return false;
        return await SaveSettings() && !closing && ReferenceEquals(bridge, target) && !GetSaveState().NeedsSave;
    }
}
