namespace ControllerBridge;

internal enum FirmwarePrompt { Replace, ConnectBootsel, ConfirmOverwrite }
internal enum FirmwareStage { Checking, EnteringBootsel, WaitingForBootsel, Writing, Reconnecting, Verifying, Complete }
internal enum FirmwareResult { AlreadyCurrent, Cancelled, Updated }

internal interface IFirmwareDevice : IDisposable
{
    Task<FirmwareInfo?> ProbeAsync();
    Task PrepareAsync(FirmwareInfo? original);
    Task WriteAsync(byte[] image, Action<int> progress);
    Task<FirmwareInfo> VerifyAsync();
}

internal static class FirmwareWorkflow
{
    internal static async Task<FirmwareResult> RunAsync(FirmwarePackage package, IFirmwareDevice device,
        Func<FirmwarePrompt, Task<bool>> confirm, Action<FirmwareStage, int> report)
    {
        report(FirmwareStage.Checking, 0);
        FirmwareInfo? current = await device.ProbeAsync();
        // Deliberately do not sort versions: a lower, higher or prerelease value is
        // simply different from the exact bundled version.
        if (current?.Version == package.Version) return FirmwareResult.AlreadyCurrent;
        if (current is not null)
        {
            if (!await confirm(FirmwarePrompt.Replace)) return FirmwareResult.Cancelled;
        }
        else
        {
            if (!await confirm(FirmwarePrompt.ConnectBootsel)) return FirmwareResult.Cancelled;
            if (!await confirm(FirmwarePrompt.ConfirmOverwrite)) return FirmwareResult.Cancelled;
        }
        report(current is null ? FirmwareStage.WaitingForBootsel : FirmwareStage.EnteringBootsel, 0);
        await device.PrepareAsync(current);
        report(FirmwareStage.Writing, 0);
        await device.WriteAsync(package.Image, percent => report(FirmwareStage.Writing, percent));
        report(FirmwareStage.Reconnecting, 0);
        FirmwareInfo installed = await device.VerifyAsync();
        report(FirmwareStage.Verifying, 0);
        if (installed.Version != package.Version)
            throw new IOException(L.F("書き込み後のバージョンが一致しません。同梱: v{0} / 本体: v{1}", "The version after writing does not match. Bundled: v{0} / Device: v{1}", package.Version, installed.Version));
        report(FirmwareStage.Complete, 100);
        return FirmwareResult.Updated;
    }

    internal static string PromptText(FirmwarePrompt prompt, string version) => prompt switch
    {
        FirmwarePrompt.Replace => L.F("v{0} に更新しますか？", "Install v{0}?", version),
        FirmwarePrompt.ConnectBootsel => L.T("対応するファームウェアを書き込む場合は、BOOTボタンを押しながら RP2350-USB-A のTYPE-C側をPCに接続してください。", "To install the supported firmware, hold the BOOT button while connecting the TYPE-C port of the RP2350-USB-A to this PC."),
        _ => L.T("本当に本ツールのファームウェアで上書きしてよろしいですか？", "Are you sure you want to overwrite the board with this tool's firmware?")
    };

    internal static string StageText(FirmwareStage stage, int percent) => stage switch
    {
        FirmwareStage.Checking => L.T("ファームウェアバージョン確認中…", "Checking firmware version…"),
        FirmwareStage.EnteringBootsel => L.T("更新中です… 書き込みモードに切り替えています。", "Updating… Switching to programming mode."),
        FirmwareStage.WaitingForBootsel => L.T("更新中です… BOOT接続を確認しています。", "Updating… Waiting for the BOOT connection."),
        FirmwareStage.Writing => L.F("更新中です… 書き込み {0}%", "Updating… Writing {0}%", percent),
        FirmwareStage.Reconnecting => L.T("更新中です… 本体の再起動・再接続を待っています。", "Updating… Waiting for the device to restart and reconnect."),
        FirmwareStage.Verifying => L.T("更新中です… 書き込み後のバージョンを確認しています。", "Updating… Checking the installed firmware version."),
        _ => L.T("更新が完了しました。", "Update complete.")
    };
}
