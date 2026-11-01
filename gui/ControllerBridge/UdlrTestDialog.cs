#if UDLR_TEST
using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    private async Task OpenUdlrTest()
    {
        if (working || bridge is null) return;
        if (dirty.Any(x => x) || caps?.ActiveSlot != selected || !profiles[selected].Mouse.DirectionKeysEnabled)
        {
            MessageBox.Show(this, L.T("対象のトラックボール／パドルの変換をONにして保存し、そのプロファイルを使用してから開いてください。", "Enable movement conversion, save the profile, and activate that profile before opening the test."), "UDLR test");
            return;
        }
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            using UdlrTestDialog dialog = new(bridge.RawAsync, profiles[selected].Clone(), Path.Combine(AppContext.BaseDirectory, "logs"));
            dialog.ShowDialog(this);
        }
        finally { working = false; UpdateEnabled(); }
    }
}

internal sealed record UdlrStats(bool Running, bool Conversion, uint Threshold, decimal BallX, decimal BallY, decimal Paddle,
    uint Generated, uint Enqueued, uint Pressed, uint Released, uint Dropped, uint Blocked, uint Queue, uint QueueResets)
{
    internal static UdlrStats Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(0, 4).SequenceEqual("DKY1"u8) || data[4] != 1 || data[6] != 2)
            throw new InvalidDataException(L.T("UDLRテスト専用ファームウェアへ更新してください。", "Install the UDLR test firmware first."));
        return new(data[7] != 0, data[5] != 0, Wire.U32(data, 8), Wire.I32(data, 12) / 10m, Wire.I32(data, 16) / 10m, Wire.I32(data, 20) / 10m,
            Wire.U32(data, 24), Wire.U32(data, 28), Wire.U32(data, 32), Wire.U32(data, 36), Wire.U32(data, 40), Wire.U32(data, 44), Wire.U32(data, 48), Wire.U32(data, 52));
    }
}

internal sealed record CursorSettings(uint Threshold, decimal TrackballGain, decimal PaddleGain)
{
    internal static CursorSettings Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(40, 4).SequenceEqual("DKC1"u8) || data[52] != 2 || data[53] != 1)
            throw new InvalidDataException("Persisted cursor settings are not available; install the matching firmware.");
        uint threshold = Wire.U32(data, 44); int ball = Wire.U16(data, 48), paddle = Wire.U16(data, 50);
        if (threshold is < 1 or > 65535 || ball is < 10 or > 2560 || paddle is < 10 or > 100)
            throw new InvalidDataException("Invalid cursor settings");
        return new(threshold, ball / 10m, paddle / 10m);
    }
}

internal sealed class UdlrTestDialog : Form
{
    private readonly Func<byte, byte[]?, Task<byte[]>> command;
    private readonly DeviceProfile profile;
    private readonly string logRoot;
    private readonly Label threshold = Ui.Label("—", 11);
    private readonly TextBox letters = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AcceptsReturn = true, ImeMode = ImeMode.Disable, MaxLength = 1000000 };
    private readonly Label values = Ui.Label("", 11), state = Ui.Label("", 10), counts = Ui.Label("", 10);
    private readonly Button start, stop, clear, save;
    private readonly System.Windows.Forms.Timer poll = new() { Interval = 100 };
    private readonly List<object> samples = [];
    private bool busy, initialized, running, finalClose, closeRequested;
    private DateTime started;
    private UdlrStats? last;
    private CursorSettings? cursorSettings;
    private string? lastLog;
    private string? failure;

    internal UdlrTestDialog(Func<byte, byte[]?, Task<byte[]>> command, DeviceProfile profile, string logRoot)
    {
        this.command = command; this.profile = profile; this.logRoot = logRoot;
        using IDisposable layout = Ui.Form(this, L.T("UDLR文字出力テスト", "UDLR character output test"), new(940, 650), new(840, 580));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) root.RowStyles.Add(new(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        Label instructions = Ui.Label(L.T("基板 USB-A ← EGRET ／ USB-C → PC\n開始後、下の入力欄へ U・D・L・R を送信します。IMEとCaps LockはOFFにしてください。\n倍率適用後の符号付き移動量を蓄積。±しきい値で1文字を送り、その軸の余りを捨てます。",
            "EGRET → bridge USB-A / bridge USB-C → PC\nStart sends U, D, L, R into the box below. Turn off IME and Caps Lock.\nSigned movement after gain accumulates. Reaching ±threshold sends one letter and clears that axis."));
        root.Controls.Add(instructions, 0, 0);
        MouseProfileSettings m = profile.Mouse;
        string ball = m.TrackballEnabled && m.TrackballToDpad ? "ON" : "OFF", paddle = m.PaddleEnabled && m.PaddleToDpad ? "ON" : "OFF";
        root.Controls.Add(Ui.Label(L.T($"{profile.Name}  |  変換: ボール {ball} / パドル {paddle} ({m.PaddleAxis})",
            $"{profile.Name}  |  Convert: ball {ball} / paddle {paddle} ({m.PaddleAxis})"), 9), 0, 1);
        FlowLayoutPanel controls = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        controls.Controls.Add(Ui.Label(L.T("1文字の移動量（本体共通）", "Movement per letter (device setting)"))); controls.Controls.Add(threshold);
        start = Ui.Button(L.T("開始", "Start"), async (_, _) => await StartTest(), true);
        stop = Ui.Button(L.T("停止", "Stop"), async (_, _) => await StopTest());
        clear = Ui.Button(L.T("文字を消去", "Clear text"), (_, _) => letters.Clear());
        save = Ui.Button(L.T("記録を保存", "Save log"), (_, _) => SaveLog());
        controls.Controls.AddRange([start, stop, clear, save]); root.Controls.Add(controls, 0, 2);
        root.Controls.Add(values, 0, 3); root.Controls.Add(letters, 0, 4); root.Controls.Add(counts, 0, 5); root.Controls.Add(state, 0, 6);
        letters.BackColor = Ui.Input; letters.ForeColor = Ui.Ink; letters.Font = new Font("Consolas", 18);
        Controls.Add(root); Ui.ThemeInputs(this); UpdateControls();
        letters.TextChanged += (_, _) => UpdateCounts();
        poll.Tick += async (_, _) => { if (!busy && initialized) await RefreshStats(); };
        Shown += async (_, _) => await Initialize();
        FormClosing += async (_, e) =>
        {
            if (finalClose) return;
            e.Cancel = true; if (closeRequested) return; closeRequested = true; poll.Stop();
            while (busy) await Task.Delay(10);
            await StopTest(); finalClose = true; BeginInvoke(Close);
        };
    }
    protected override void Dispose(bool disposing) { if (disposing) poll.Dispose(); base.Dispose(disposing); }
    private void UpdateControls()
    {
        start.Enabled = initialized && !busy && !running && !closeRequested;
        stop.Enabled = initialized && !busy && running && !closeRequested;
        clear.Enabled = save.Enabled = !busy && !running && started != default;
    }
    private async Task Initialize()
    {
        busy = true; UpdateControls();
        try
        {
            last = UdlrStats.Read(await command(49, null));
            await SetGate(false);
            cursorSettings = CursorSettings.Read(await command(2, null));
            if (cursorSettings.Threshold != last.Threshold) throw new InvalidDataException("Cursor threshold response mismatch");
            initialized = true; state.Text = L.T("停止中。開始を押してください。", "Stopped. Press Start."); poll.Start();
            RenderStats();
        }
        catch (Exception ex) { Fail(ex); }
        finally { busy = false; UpdateControls(); }
    }
    private async Task SetGate(bool enabled)
    {
        byte[] args = new byte[12]; "UDL2"u8.CopyTo(args); args[4] = enabled ? (byte)1 : (byte)0;
        byte[] reply = await command(50, args);
        if (reply.Length != 56 || !reply.AsSpan(0, 4).SequenceEqual("UTS2"u8) || reply[4] != 2 || reply[5] != args[4] || Wire.U32(reply, 8) != last?.Threshold)
            throw new InvalidDataException("UDLR control response mismatch");
        running = enabled;
    }
    private async Task StartTest()
    {
        if (busy || !initialized || running || closeRequested) return;
        busy = true; UpdateControls();
        try
        {
            last = UdlrStats.Read(await command(49, null));
            if (!last.Conversion) throw new InvalidDataException(L.T("使用中のプロファイルで移動の変換をONにしてください。", "Enable movement conversion in the active profile."));
            await SetGate(true); started = DateTime.UtcNow; samples.Clear(); letters.Clear(); lastLog = null; failure = null;
            state.Text = L.T("実行中。文字はフォーカスのある入力欄へ届きます。停止すると記録を自動保存します。", "Running. Letters go to the focused input. Stop also saves the log.");
            letters.Focus();
        }
        catch (Exception ex) { Fail(ex); }
        finally { busy = false; UpdateControls(); }
    }
    private async Task StopTest()
    {
        if (busy || !initialized) return;
        busy = true; UpdateControls();
        try
        {
            await SetGate(false); last = UdlrStats.Read(await command(49, null)); RenderStats();
            if (started != default) SaveLog(); else state.Text = L.T("停止中", "Stopped");
        }
        catch (Exception ex) { Fail(ex); }
        finally { running = false; busy = false; UpdateControls(); }
    }
    private async Task RefreshStats()
    {
        busy = true;
        try
        {
            last = UdlrStats.Read(await command(49, null));
            if (running && !last.Running) { running = false; state.Text = L.T("本体側で停止しました。再開するには開始を押してください。", "The device stopped. Press Start to resume."); }
            if (started != default && samples.Count < 36000) samples.Add(new { utc = DateTime.UtcNow, stats = last, received = letters.TextLength });
            RenderStats();
        }
        catch (Exception ex) { Fail(ex); }
        finally { busy = false; UpdateControls(); }
    }
    private void Fail(Exception ex)
    {
        failure = ex.Message; running = false; initialized = false; poll.Stop();
        state.Text = L.T("通信エラー。本体は最後の通信から約2秒でキーを解放します。\n", "Communication error. The device releases keys about 2 s after its last contact.\n") + ex.Message;
    }
    private void RenderStats()
    {
        if (last is null) return;
        threshold.Text = last.Threshold.ToString();
        values.Text = L.T($"蓄積: ボールX {last.BallX:F1} / Y {last.BallY:F1} / パドル {last.Paddle:F1}\n生成 {last.Generated}  待ち行列投入 {last.Enqueued}  ACK 押下 {last.Pressed} / 解放 {last.Released}\n破棄 {last.Dropped}  十字キー競合 {last.Blocked}  待ち {last.Queue}  待ち行列リセット {last.QueueResets}",
            $"Sums: ball X {last.BallX:F1} / Y {last.BallY:F1} / paddle {last.Paddle:F1}\nGenerated {last.Generated}  Enqueued {last.Enqueued}  ACK press {last.Pressed} / release {last.Released}\nDropped {last.Dropped}  D-pad conflicts {last.Blocked}  Queued {last.Queue}  Queue resets {last.QueueResets}");
    }
    private object LetterCounts() => new { U = letters.Text.Count(c => c is 'U' or 'u'), D = letters.Text.Count(c => c is 'D' or 'd'), L = letters.Text.Count(c => c is 'L' or 'l'), R = letters.Text.Count(c => c is 'R' or 'r'), total = letters.TextLength };
    private void UpdateCounts() => counts.Text = L.T("受信文字数: ", "Received letters: ") +
        $"U {letters.Text.Count(c => c is 'U' or 'u')}   D {letters.Text.Count(c => c is 'D' or 'd')}   L {letters.Text.Count(c => c is 'L' or 'l')}   R {letters.Text.Count(c => c is 'R' or 'r')}   " +
        L.T($"合計 {letters.TextLength}", $"Total {letters.TextLength}");
    private void SaveLog()
    {
        if (started == default) return;
        try
        {
            Directory.CreateDirectory(logRoot);
            string path = Path.Combine(logRoot, $"udlr-{started:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { schema = 2, firmware = typeof(MainForm).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0], startedUtc = started, savedUtc = DateTime.UtcNow,
                threshold = last?.Threshold, conversionGains = new { trackball = cursorSettings!.TrackballGain, paddle = cursorSettings.PaddleGain }, paddleAxis = profile.Mouse.PaddleAxis.ToString(), profile, finalStats = last, received = LetterCounts(), text = letters.Text, failure, samples }, new JsonSerializerOptions { WriteIndented = true }));
            lastLog = path; state.Text = L.T("停止中。記録: ", "Stopped. Log: ") + Path.GetFileName(path);
        }
        catch (Exception ex) { state.Text = L.T("記録を保存できません: ", "Could not save log: ") + ex.Message; }
    }

    internal static void RunChecks(string directory)
    {
        Directory.CreateDirectory(directory); List<string> checks = []; bool enabled = false; uint currentThreshold = 64;
        List<(byte command, byte[]? data)> calls = [];
        Task<byte[]> Fake(byte cmd, byte[]? data)
        {
            calls.Add((cmd, data?.ToArray())); byte[] reply = new byte[56]; reply[4] = 1;
            if (cmd == 49) { "DKY1"u8.CopyTo(reply); reply[5] = 1; reply[6] = 2; reply[7] = enabled ? (byte)1 : (byte)0; Wire.W32(reply, 8, currentThreshold); Wire.W32(reply, 12, 123); Wire.W32(reply, 24, 7); }
            else if (cmd == 2) { "DKC1"u8.CopyTo(reply.AsSpan(40)); Wire.W32(reply, 44, currentThreshold); Wire.W16(reply, 48, 83); Wire.W16(reply, 50, 27); reply[52] = 2; reply[53] = 1; }
            else if (cmd == 50) { "UTS2"u8.CopyTo(reply); reply[4] = 2; enabled = data![4] != 0; if (Wire.U32(data, 8) != 0) throw new InvalidDataException("Test cannot change the device threshold"); reply[5] = data[4]; Wire.W32(reply, 8, currentThreshold); }
            else throw new InvalidDataException("Unexpected command");
            return Task.FromResult(reply);
        }
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks.Add(message); }
        using UdlrTestDialog form = new(Fake, new() { Flags = 1, Vid = 0x0ae4, Pid = 0x0701, Name = "EGRET II mini", Mouse = new() { TrackballToDpad = true, PaddleToDpad = true } }, Path.Combine(directory, "logs")) { ShowInTaskbar = false, Opacity = 0 };
        Exception? error = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                while (form.busy) await Task.Delay(5); form.poll.Stop();
                Check(form.initialized && !enabled && form.start.Enabled && !form.stop.Enabled && form.threshold.Text == "64", "opens stopped with threshold 64");
                Check(calls[0].command == 49 && calls[1].command == 50 && calls[1].data![4] == 0, "checks diagnostic identity before disarming");
                await form.StartTest();
                Check(enabled && currentThreshold == 64 && form.running && form.stop.Enabled, "start retains the device-wide threshold and enables stop");
                Check(calls.Last().data!.AsSpan(0, 4).SequenceEqual("UDL2"u8), "command has diagnostic marker");
                await form.RefreshStats(); Check(form.last!.BallX == 12.3m && form.last.Generated == 7, "live statistics retain signed tenths");
                form.letters.Text = "UDLLRRR"; Check(form.letters.TextLength == 7, "input box accepts text");
                form.Opacity = 1; form.PerformLayout(); using (Bitmap bitmap = new(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new(Point.Empty, form.Size)); bitmap.Save(Path.Combine(directory, "udlr-test.png")); } form.Opacity = 0;
                await form.StopTest(); Check(!enabled && !form.running && form.threshold.Enabled && form.start.Enabled, "stop releases and restores Start");
                Check(form.lastLog is not null && File.Exists(form.lastLog), "stop writes trial log");
                using (JsonDocument log = JsonDocument.Parse(File.ReadAllText(form.lastLog!)))
                {
                    Check(log.RootElement.GetProperty("threshold").GetInt32() == 64 && log.RootElement.GetProperty("text").GetString() == "UDLLRRR", "log records threshold and exact received letters");
                    Check(log.RootElement.GetProperty("received").GetProperty("R").GetInt32() == 3, "log contains direction counts");
                    Check(log.RootElement.GetProperty("conversionGains").GetProperty("trackball").GetDecimal() == 8.3m && log.RootElement.GetProperty("conversionGains").GetProperty("paddle").GetDecimal() == 2.7m, "log records device cursor parameters independently of pointer sensitivity");
                }
                Check(form.cursorSettings is { TrackballGain: 8.3m, PaddleGain: 2.7m }, "hidden parameters are read from the device instead of hard-coded");
                Check(!form.Controls.OfType<NumericUpDown>().Any() && form.threshold is Label, "threshold is a read-only label");
                Check(calls.Where(c => c.command == 50).All(c => Wire.U32(c.data!, 8) == 0), "no test command can override the threshold");
                await form.StartTest(); Check(currentThreshold == 64 && form.samples.Count == 0 && form.letters.TextLength == 0, "new trial clears text and samples without changing the device setting");
                await form.StopTest();
                bool rejected = false; try { UdlrStats.Read(new byte[56]); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "normal firmware cannot start a keyboard test");
                Check(calls.All(c => c.command is 2 or 49 or 50), "dialog never writes profile or Flash commands");
                await form.StartTest(); form.Close();
                Check(!enabled, "closing an active test disarms before closing");
            }
            catch (Exception ex) { error = ex; form.finalClose = true; form.Close(); }
        };
        Application.Run(form);
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        File.WriteAllText(Path.Combine(directory, "udlr-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, hardwareOpened = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

#endif
