namespace ControllerBridge;

internal enum FirmwarePrompt { Replace, ConfirmOverwrite }
internal enum FirmwareStage { Checking, EnteringBootsel, WaitingForBootsel, Writing, Reconnecting, Verifying, Complete }
internal enum FirmwareResult { AlreadyCurrent, Cancelled, Updated }

internal interface IFirmwareDevice : IDisposable
{
    Task<FirmwareInfo?> ProbeAsync();
    Task PrepareAsync(FirmwareInfo? original, CancellationToken cancellationToken = default);
    Task WriteAsync(byte[] image, Action<int> progress);
    Task<FirmwareInfo> VerifyAsync();
}

internal static class FirmwareWorkflow
{
    internal static async Task<FirmwareResult> RunAsync(FirmwarePackage package, IFirmwareDevice device,
        Func<FirmwarePrompt, Task<bool>> confirm, Action<FirmwareStage, int> report,
        CancellationToken bootWaitCancellation = default)
    {
        report(FirmwareStage.Checking, 0);
        FirmwareInfo? current = await device.ProbeAsync();
        // Deliberately do not sort versions: a lower, higher or prerelease value is
        // simply different from the exact bundled version.
        if (current?.Version == package.Version) return FirmwareResult.AlreadyCurrent;
        if (current is not null)
        {
            if (!await confirm(FirmwarePrompt.Replace)) return FirmwareResult.Cancelled;
            report(FirmwareStage.EnteringBootsel, 0);
            await device.PrepareAsync(current);
        }
        else
        {
            // Physical BOOT reconnection is observed before asking permission to
            // write. Cancelling this wait must never advance to a confirmation.
            try
            {
                bootWaitCancellation.ThrowIfCancellationRequested();
                report(FirmwareStage.WaitingForBootsel, 0);
                await device.PrepareAsync(null, bootWaitCancellation);
                bootWaitCancellation.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (bootWaitCancellation.IsCancellationRequested)
            { return FirmwareResult.Cancelled; }
            if (!await confirm(FirmwarePrompt.ConfirmOverwrite)) return FirmwareResult.Cancelled;
        }
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
        _ => L.F("BOOT接続を確認しました。\n\n本ツールのファームウェア v{0} を書き込み、現在のファームウェアを上書きします。書き込みを開始してよろしいですか？", "BOOT connection confirmed.\n\nInstall this tool's firmware v{0} and overwrite the board's current firmware?", version)
    };

    internal static string BootWaitInstructions => L.T(
        "1. RP2350-USB-AのUSB-Cケーブルを抜いてください。\n2. BOOTボタンを押したまま、同じPCのUSBポートへ接続し直してください。\n3. 接続後にBOOTボタンを離してください。\n\nBOOT接続を検出すると、書き込みの最終確認へ自動的に進みます。まだ書き込みは行いません。",
        "1. Unplug the USB-C cable from the RP2350-USB-A.\n2. Hold BOOT and reconnect it to the same USB port on this PC.\n3. Release BOOT after connecting.\n\nWhen BOOT mode is detected, the final write confirmation opens automatically. Nothing is written yet.");

    internal static string StageText(FirmwareStage stage, int percent) => stage switch
    {
        FirmwareStage.Checking => L.T("ファームウェアバージョン確認中…", "Checking firmware version…"),
        FirmwareStage.EnteringBootsel => L.T("更新中です… 書き込みモードに切り替えています。", "Updating… Switching to programming mode."),
        FirmwareStage.WaitingForBootsel => L.T("BOOTボタンを押しながらの再接続を待っています…", "Waiting for reconnection with BOOT held…"),
        FirmwareStage.Writing => L.F("更新中です… 書き込み {0}%", "Updating… Writing {0}%", percent),
        FirmwareStage.Reconnecting => L.T("更新中です… 本体の再起動・再接続を待っています。", "Updating… Waiting for the device to restart and reconnect."),
        FirmwareStage.Verifying => L.T("更新中です… 書き込み後のバージョンを確認しています。", "Updating… Checking the installed firmware version."),
        _ => L.T("更新が完了しました。", "Update complete.")
    };
}
