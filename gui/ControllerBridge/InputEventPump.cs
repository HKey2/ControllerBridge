namespace ControllerBridge;

// The device has a short event ring. Keep draining it while the UI is laying
// out windows, painting, or waiting for a modal operation to finish.
internal sealed class InputEventPump : IDisposable
{
    private const int Capacity = 8192;
    private readonly record struct Entry(uint Cursor, InputChange Change);
    private readonly Entry[] history = new Entry[Capacity];
    private readonly object sync = new();
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly Func<uint, uint, Task<PollResult>> poll;
    private readonly uint generation;
    private Capabilities caps;
    private uint cursor;
    private int first, count;
    private bool changed, disposed, finished;
    private Exception? failure;
    private TaskCompletionSource? requested;

    internal InputEventPump(DeviceState device, Func<uint, uint, Task<PollResult>> poll)
    {
        this.poll = poll; caps = device.Caps; cursor = device.Cursor;
        generation = caps.ConnectionGeneration;
        _ = Task.Run(Receive);
    }

    internal async Task<PollResult> ReadAsync(uint after, uint expectedGeneration)
    {
        Task? refresh = null;
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (!changed)
            {
                // A learning baseline must include a read started after its
                // request, rather than accepting a possibly stale UI snapshot.
                requested ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                refresh = requested.Task;
                if (wake.CurrentCount == 0) wake.Release();
            }
        }
        if (refresh is not null) await refresh.ConfigureAwait(false);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            if (changed || generation != expectedGeneration) return new(caps, cursor, [], true);
            List<InputChange> changes = [];
            int unread = count;
            while (unread > 0 && unchecked((int)(history[(first + unread - 1) % Capacity].Cursor - after)) > 0) unread--;
            for (int i = unread; i < count; i++)
            {
                Entry entry = history[(first + i) % Capacity];
                InputChange change = entry.Change;
                if (changes.Count == 0 && unchecked(entry.Cursor - after) != 1) change = change with { Lost = true };
                changes.Add(change);
            }
            return new(caps, cursor, changes, false);
        }
    }

    private async Task Receive()
    {
        TaskCompletionSource? active = null;
        try
        {
            while (true)
            {
                lock (sync)
                {
                    if (disposed) break;
                    active = requested; requested = null;
                }
                PollResult result = await poll(cursor, generation).ConfigureAwait(false);
                lock (sync)
                {
                    if (disposed) break;
                    // Events from different input layouts or active profiles
                    // must never be replayed into the same learning session.
                    changed |= result.GenerationChanged || result.Caps.ConnectionGeneration != generation ||
                        result.Caps.DescriptorHash != caps.DescriptorHash || result.Caps.FieldCount != caps.FieldCount ||
                        result.Caps.ActiveSlot != caps.ActiveSlot;
                    caps = result.Caps; cursor = result.Cursor;
                    if (changed) first = count = 0;
                    else
                    {
                        foreach (InputChange change in result.Changes)
                        {
                            if (count == Capacity) { first = (first + 1) % Capacity; count--; }
                            history[(first + count++) % Capacity] = new(change.Cursor, change);
                        }
                    }
                    active?.TrySetResult(); active = null;
                    if (changed) { requested?.TrySetResult(); requested = null; }
                }
                // Catch a bounded device batch immediately when there is more
                // history, but otherwise keep the usual 25 ms polling cadence.
                if (!changed && unchecked((int)(cursor - caps.EventHead)) < 0) continue;
                await wake.WaitAsync(25, stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lock (sync)
            {
                failure = ex;
                active?.TrySetException(ex); active = null;
                requested?.TrySetException(ex); requested = null;
            }
        }
        finally
        {
            lock (sync)
            {
                active?.TrySetCanceled(); requested?.TrySetCanceled(); requested = null;
                finished = true;
                if (disposed) { wake.Dispose(); stop.Dispose(); }
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true; stop.Cancel();
            requested?.TrySetCanceled(); requested = null;
            // A failed pump has already left Receive's finally block.
            if (finished) { wake.Dispose(); stop.Dispose(); }
        }
    }
}
