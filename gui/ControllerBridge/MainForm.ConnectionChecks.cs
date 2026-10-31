using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    internal static void RunConnectionChecks(string directory, bool hardware)
    {
        Directory.CreateDirectory(directory);
        using MainForm form = new(autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try { await form.CheckConnectionUi(directory, hardware); }
            catch (Exception ex) { failure = ex; }
            finally { Array.Clear(form.dirty); form.working = false; form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw failure;
    }

    private async Task CheckConnectionUi(string directory, bool hardware)
    {
        List<string> checks = []; List<object> observations = [];
        void Check(bool ok, string description) { if (!ok) throw new InvalidDataException(description); checks.Add(description); }
        int guides = 0, attempts = 0;
        bool arrivalAfterSimulatedAbsence = false;
        ConnectionStatus? detected = null;
        firmwareGuide = introduction =>
        {
            guides++;
            if (hardware)
            {
                using FirmwareDevice device = new();
                FirmwareUpdateDialog.CheckDeclinedGuide(this, bundledFirmware.Value, device, introduction!,
                    Path.Combine(directory, guides == 1 ? "automatic-guide.png" : "arrival-guide.png"), Check);
            }
            return Task.CompletedTask;
        };
        static IEnumerable<Control> Descendants(Control root)
        {
            yield return root;
            foreach (Control child in root.Controls) foreach (Control item in Descendants(child)) yield return item;
        }
        async Task ObserveStable(string scenario, int durationMs)
        {
            await Task.Delay(100);
            int enabledChanges = 0, textChanges = 0, invalidations = 0, layouts = 0;
            int bindings = selectionBindings, beforeGuides = guides, beforeAttempts = attempts;
            EventHandler enabled = (_, _) => enabledChanges++;
            EventHandler text = (_, _) => textChanges++;
            InvalidateEventHandler invalidated = (_, _) => invalidations++;
            LayoutEventHandler layout = (_, _) => layouts++;
            Control[] controls = Descendants(this).ToArray();
            foreach (Control control in controls)
            { control.EnabledChanged += enabled; control.TextChanged += text; control.Invalidated += invalidated; control.Layout += layout; }
            try
            {
                if (hardware) await Task.Delay(durationMs);
                else for (int i = 0; i < 3; i++) { await Connect(false); timer.Stop(); }
            }
            finally
            {
                foreach (Control control in controls)
                { control.EnabledChanged -= enabled; control.TextChanged -= text; control.Invalidated -= invalidated; control.Layout -= layout; }
            }
            observations.Add(new { scenario, enabledChanges, textChanges, invalidations, layouts,
                editorRebindings = selectionBindings - bindings, extraGuides = guides - beforeGuides, retries = attempts - beforeAttempts });
            Check(enabledChanges == 0 && textChanges == 0 && invalidations == 0 && layouts == 0 && selectionBindings == bindings,
                scenario + ": repeated discovery does not change, invalidate, relayout or rebind controls");
            Check(guides == beforeGuides && attempts - beforeAttempts >= 3, scenario + ": retries continue without repeating a guide");
        }
        if (hardware)
        {
            Func<string, Task<ConnectionRead>> readPhysical = async version => { attempts++; ConnectionRead result = await ConnectionRead.OpenAsync(version); detected = result.Status; return result; };
            readConnection = readPhysical;
            await Connect(false);
            if (detected?.Kind == ConnectionKind.NotDetected)
                Check(bridge is null && caps is null && StartupError is null && guides == 0 && connection.Text == detected.Summary,
                    "real USB discovery with no detected bridge stays disconnected without a startup error or firmware guide");
            else
                Check(detected?.Kind is ConnectionKind.UnknownFirmware or ConnectionKind.DifferentFirmware && bridge is null && StartupError is not null && guides == 1,
                    "physical mismatched/unknown firmware opens one automatic guide (requires an absent or mismatched/older bridge)");
            await ObserveStable("physical-device", 6800);
            CapturePreview(Path.Combine(directory, "connection.png"));
            if (detected?.Kind is ConnectionKind.UnknownFirmware or ConnectionKind.DifferentFirmware)
            {
                ConnectionStatus physical = detected;
                Check(connection.Text == physical.Summary && !saveButton.Enabled,
                    "unsupported physical board retains its distinct status after the automatic guide is declined");
                int beforeArrival = guides;
                // The board stays physically attached and unmodified. Simulate
                // only its prior absence, then let the normal background timer
                // discover the real USB device and open the real updater again.
                readConnection = _ => Task.FromResult(new ConnectionRead(new(ConnectionKind.NotDetected)));
                await Connect(false); timer.Stop();
                Check(StartupError is null && guides == beforeArrival, "pre-attachment absence is quiet");
                readConnection = readPhysical; timer.Start();
                for (int i = 0; i < 80 && (guides == beforeArrival || ModalEditorOpen); i++) await Task.Delay(100);
                Check(!ModalEditorOpen && guides == beforeArrival + 1 && detected == physical && connection.Text == physical.Summary,
                    "background discovery of the real board after simulated absence opens one automatic guide");
                arrivalAfterSimulatedAbsence = true;
                await ObserveStable("physical-board-after-simulated-arrival", 6800);
                CapturePreview(Path.Combine(directory, "arrival-connection.png"));
            }
        }
        else
        {
            // Keep a real editable draft while simulated USB discovery is delayed.
            profiles[0] = new() { Flags = 3, Vid = 0x1234, Pid = 1, Name = "Unsaved draft", ProfileId = Guid.NewGuid() };
            dirty[0] = true; selected = 0; RefreshSlots(); BindSelected();
            ConnectionStatus status = new(ConnectionKind.NotDetected);
            bool ioError = false;
            readConnection = async _ =>
            {
                attempts++;
                await Task.Delay(40);
                if (ioError) throw new TimeoutException("USB read timed out");
                return new(status);
            };
            foreach (ConnectionStatus scenario in new ConnectionStatus[]
            {
                new(ConnectionKind.NotDetected), new(ConnectionKind.UnknownFirmware, "board-a"),
                new(ConnectionKind.DifferentFirmware, "board-a", "0.1.0"),
                new(ConnectionKind.DifferentFirmware, "board-a", "99.0.0")
            })
            {
                status = scenario;
                int before = guides;
                await Connect(false); timer.Stop();
                bool missing = scenario.Kind == ConnectionKind.NotDetected;
                Check(guides == before + (missing ? 0 : 1), scenario.Kind + (missing ? ": startup waits without a firmware guide" : ": first detection automatically offers guidance"));
                if (missing)
                {
                    Check(StartupError is null && bridge is null && caps is null && connection.Text == scenario.Summary,
                        "missing device is a disconnected waiting state, not an unsupported firmware error");
                    Check(connect.Enabled && firmwareUpdate.Enabled && !saveButton.Enabled && name.Enabled,
                        "offline discovery keeps reconnect, manual firmware update and drafts available without enabling device writes");
                    await Connect(); timer.Stop();
                    Check(guides == before && StartupError is null, "manual reconnect while unplugged also stays disconnected without an update prompt");
                }
                else if (scenario.Kind == ConnectionKind.UnknownFirmware)
                    Check(connection.Text == scenario.Summary && connection.Text.Contains(L.T("未対応", "unsupported")) && !saveButton.Enabled,
                        "unknown firmware displays unsupported status and cannot save profiles");
                await ObserveStable(scenario.Kind + ":" + scenario.Version, 0);
                CapturePreview(Path.Combine(directory, scenario.Kind + (scenario.Version ?? "") + ".png"));
            }
            status = new(ConnectionKind.NotDetected);
            int currentGuides = guides;
            await Connect(false); timer.Stop();
            status = new(ConnectionKind.UnknownFirmware, "board-a");
            await Connect(false); timer.Stop();
            Check(guides == currentGuides + 1, "observed disconnect rearms a dismissed notice for the next attachment");
            currentGuides = guides;
            ioError = true; await Connect(false); timer.Stop();
            await ObserveStable("transport-error", 0); ioError = false;
            int beforeAttempt = attempts;
            Task pending = Connect(false);
            Check(connecting && body.Enabled && name.Enabled && !working, "editor stays enabled during background discovery");
            name.Text = "Edited during discovery";
            await Connect(false);
            Check(attempts == beforeAttempt + 1, "overlapping discovery is coalesced");
            await pending; timer.Stop();
            Check(profiles[0].Name == "Edited during discovery" && dirty[0], "draft edited during discovery is preserved");
            pending = Connect(false); working = true;
            await pending; timer.Stop(); working = false; UpdateEnabled();
            Check(!connecting && guides == currentGuides, "a foreground operation supersedes pending discovery");
            string? beforeError = StartupError;
            ioError = true; pending = Connect(false); working = true;
            await pending; timer.Stop(); ioError = false; working = false; UpdateEnabled();
            Check(StartupError == beforeError, "a superseded discovery error does not overwrite the foreground operation");
            Enabled = false; beforeAttempt = attempts;
            await Connect(false); timer.Stop(); Enabled = true;
            Check(attempts == beforeAttempt, "background discovery is deferred while another modal editor owns the form");
            status = new(ConnectionKind.DifferentFirmware, "modal-board", "0.2.0");
            pending = Connect(false);
            Exception? modalFailure = null;
            using (Form modal = new() { Opacity = 0, ShowInTaskbar = false })
            {
                modal.Shown += async (_, _) =>
                {
                    try
                    {
                        await pending; timer.Stop();
                        Check(guides == currentGuides, "a modal opened during discovery defers its automatic guide");
                        int beforeModalAttempt = attempts;
                        await Connect(false);
                        Check(attempts == beforeModalAttempt, "an actual owned modal defers new discovery");
                    }
                    catch (Exception ex) { modalFailure = ex; }
                    finally { modal.Close(); }
                };
                modal.ShowDialog(this);
            }
            if (modalFailure is not null) throw modalFailure;
            await Connect(false); timer.Stop();
            Check(guides == currentGuides + 1, "deferred firmware guide appears after the modal editor closes");

            // Exercise the normal display path as firmware host-retry counters change.
            caps = new(255, 0, 0, 0, 0, 4, 1, 0, bankGeneration, 0, 0, 0);
            UpdateConnection(); await Task.Delay(60);
            int changed = 0, redraws = 0, rebound = selectionBindings;
            EventHandler changedHandler = (_, _) => changed++;
            InvalidateEventHandler redrawHandler = (_, _) => redraws++;
            Control[] all = Descendants(this).ToArray();
            foreach (Control control in all) { control.TextChanged += changedHandler; control.Invalidated += redrawHandler; }
            foreach (byte host in new byte[] { 0, 1, 2, 4, 6, 10 }) { caps = caps with { HostStatus = host, ConnectionGeneration = (uint)(host + 1) }; UpdateConnection(); }
            foreach (Control control in all) { control.TextChanged -= changedHandler; control.Invalidated -= redrawHandler; }
            Check(changed == 0 && redraws == 0 && rebound == selectionBindings, "empty input host retries keep the connected UI stable");
            int liveChanges = 0;
            live.TextChanged += (_, _) => liveChanges++;
            fields.Add(new() { Index = 0, Bits = 8, Minimum = 0, Maximum = 255, UsagePage = 9, Usage = 1 });
            ApplyChanges([new(0, 1, 1, false), new(0, 2, 2, false), new(0, 3, 3, false)], false);
            Check(liveChanges == 1 && fields[0].Value == 3, "input burst updates visible status once with the final value");
            ApplyChanges([new(0, 2, 4, false), new(0, 3, 5, false)], false);
            Check(liveChanges == 1, "unchanged final input value does not repaint intermediate values");
            foreach (ConnectionStatus guide in new ConnectionStatus[] { new(ConnectionKind.UnknownFirmware, "board"), new(ConnectionKind.DifferentFirmware, "board", "0.1.0") })
            {
                using FirmwareChecks.FakeDevice device = new(guide.Version, bundledFirmware.Value.Version);
                if (guide.Kind == ConnectionKind.UnknownFirmware) device.OnPrepare = token => Task.Delay(Timeout.Infinite, token);
                FirmwareUpdateDialog.CheckDeclinedGuide(this, bundledFirmware.Value, device, guide.Guidance(bundledFirmware.Value.Version),
                    Path.Combine(directory, guide.Kind + "-guide.png"), Check);
                Check(device.Calls.SequenceEqual(guide.Kind == ConnectionKind.UnknownFirmware ? ["probe", "manual"] : ["probe"]) && device.Written is null,
                    guide.Kind + ": automatic guidance never writes without final confirmation");
            }
            FirmwareNoticeTracker tracker = new();
            Check(!tracker.Observe(new(ConnectionKind.NotDetected), true) && !tracker.Observe(new(ConnectionKind.NotDetected), true), "initial and repeated absence never offer firmware guidance");
            Check(tracker.Observe(new(ConnectionKind.UnknownFirmware, "newly-attached"), true) && !tracker.Observe(new(ConnectionKind.UnknownFirmware, "newly-attached"), true), "a device attached after offline startup still gets one required firmware guide");
            Check(!tracker.Observe(new(ConnectionKind.NotDetected), true) && tracker.Observe(new(ConnectionKind.UnknownFirmware, "newly-attached"), true),
                "the same board receives guidance again only after a disconnected state was observed");
            Check(!tracker.Observe(new(ConnectionKind.Ready), true) && !tracker.Observe(new(ConnectionKind.NotDetected), true), "ordinary unplug after a ready connection does not open installation guidance");
            Check(!tracker.Observe(new(ConnectionKind.UnknownFirmware, "manual"), false) && !tracker.Observe(new(ConnectionKind.UnknownFirmware, "manual"), true), "manual cancellation suppresses the same automatic notice");
        }
        File.WriteAllText(Path.Combine(directory, "connection-checks.json"), JsonSerializer.Serialize(new
        { passed = true, count = checks.Count, hardware, detected = detected?.Kind.ToString(), version = detected?.Version,
            physicalDeviceWritten = false, arrivalAfterSimulatedAbsence, guides, attempts, observations, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
