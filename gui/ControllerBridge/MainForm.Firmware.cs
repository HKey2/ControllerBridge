namespace ControllerBridge;

internal sealed partial class MainForm
{
    private async Task UpdateFirmware(string? introduction = null)
    {
        if (working || closing || preview) return;
        working = true; UpdateEnabled(); AbortLearning(); timer.Stop(); await WaitForPoll(); await WaitForConnection();
        if (closing) { working = false; return; }
        try
        {
            // Draft profile edits remain in memory. Opening or cancelling the updater
            // never saves/discards them, and a later reconnection uses the usual rules.
            FirmwarePackage package = bundledFirmware.Value;
            bridge?.Dispose(); bridge = null;
            using FirmwareDevice device = new();
            using FirmwareUpdateDialog dialog = new(package, device, introduction);
            dialog.ShowDialog(this);
            if (dialog.Result == FirmwareResult.Updated)
                SetText(saved, L.F("同梱の v{0} に更新しました。", "Installed the bundled v{0}.", package.Version));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally
        {
            working = false; UpdateEnabled();
            await Connect(false, allowFirmwareNotice: false);
        }
    }
}
