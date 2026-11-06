namespace muten.Core;

// Not thread-safe: call it from one thread only (the UI thread in the tray). Background work
// (session/device notifications, grace timers) is marshalled back through the sync context;
// only the volume fades themselves run on the thread pool.
public class AutoMuteService
{
    private const int FadeStepMs = 15;

    private readonly AudioSessionManager _manager;
    private readonly SynchronizationContext? _syncContext;
    private readonly Dictionary<string, ManagedApp> _managedApps = new(StringComparer.OrdinalIgnoreCase);

    // Apps muten has muted/lowered, with the volume to restore; mirrored to state.json
    private readonly Dictionary<string, AlteredApp> _altered = new(StringComparer.OrdinalIgnoreCase);

    // Apps waiting out the grace period, mapped to the batch that scheduled them
    private readonly Dictionary<string, object> _pendingLowers = new(StringComparer.OrdinalIgnoreCase);

    // Latest volume transition per app; a newer one cancels it and waits for it to stop first
    private readonly Dictionary<string, Transition> _transitions = new(StringComparer.OrdinalIgnoreCase);

    private string? _foregroundProcess;

    public bool Enabled { get; set; } = true;
    public int FadeMs { get; set; } = 150;
    public int GraceMs { get; set; } = 300;

    public event Action<List<string>>? AppsMuted;

    public AutoMuteService(AudioSessionManager manager, SynchronizationContext? syncContext = null)
    {
        _manager = manager;
        _syncContext = syncContext;
        _manager.SessionCreated += name => Dispatch(() => OnSessionCreated(name));
        _manager.DevicesChanged += () => Dispatch(Reevaluate);
    }

    public IReadOnlyDictionary<string, ManagedApp> ManagedApps => _managedApps;

    public bool IsManaged(string? processName) =>
        !string.IsNullOrEmpty(processName) && _managedApps.ContainsKey(processName);

    public void AddManagedApp(ManagedApp app)
    {
        if (!string.IsNullOrEmpty(app.ProcessName))
            _managedApps[app.ProcessName] = app;
    }

    public void RemoveManagedApp(string? processName)
    {
        if (string.IsNullOrEmpty(processName)) return;

        if (_managedApps.Remove(processName))
        {
            _pendingLowers.Remove(processName);
            Restore(processName, fade: false);
        }
    }

    public void OnForegroundChanged(string? foregroundProcess, string? foregroundExePath)
    {
        _foregroundProcess = foregroundProcess;
        Reevaluate();
    }

    // Applies the rules to every managed app; also called on device and setting changes
    public void Reevaluate()
    {
        if (!Enabled) return;

        List<string>? toLower = null;

        foreach (var (processName, app) in _managedApps)
        {
            if (ShouldPlay(processName))
            {
                // Regaining focus restores right away, cancelling any pending lower
                _pendingLowers.Remove(processName);
                Restore(processName, fade: true);
            }
            else if (_altered.ContainsKey(processName))
            {
                // Already lowered: re-apply so sessions opened since then are covered
                Lower(processName, app, fade: true);
            }
            else if (!_pendingLowers.ContainsKey(processName))
            {
                (toLower ??= []).Add(processName);
            }
        }

        if (toLower is not null)
            ScheduleLower(toLower);
    }

    public void RestoreAll()
    {
        _pendingLowers.Clear();

        var names = _managedApps.Keys.Concat(_altered.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var processName in names)
        {
            // An app without sessions cannot be restored now; keep its entry so it is restored when it shows up
            // (Windows remembers per-app mute/volume, so it would otherwise come back muted)
            if (_altered.ContainsKey(processName) && _manager.GetVolumeByName(processName) is null)
                continue;

            Restore(processName, fade: false);
        }

        WaitForTransitions();
    }

    // Restores whatever a previous run left muted or lowered (e.g. after a crash)
    public void RestoreFromState()
    {
        foreach (var entry in StateManager.Load())
        {
            _altered[entry.ProcessName] = new AlteredApp
            {
                OriginalVolume = entry.OriginalVolume,
                Muted = entry.Muted,
                Percent = -1,
            };
        }

        foreach (var processName in _altered.Keys.ToList())
        {
            if (_manager.GetVolumeByName(processName) is not null)
                Restore(processName, fade: false);
        }

        WaitForTransitions();
        SaveState();
    }

    private void OnSessionCreated(string processName)
    {
        bool isManaged = _managedApps.TryGetValue(processName, out var app);

        if (!Enabled || !isManaged)
        {
            // Left over from a crash or from an app that was not running when restoring
            if (_altered.ContainsKey(processName))
                Restore(processName, fade: false);
            return;
        }

        // Inside the grace period the pending lower takes care of it
        if (_pendingLowers.ContainsKey(processName)) return;

        if (ShouldPlay(processName))
        {
            if (_altered.ContainsKey(processName))
                Restore(processName, fade: false);
            return;
        }

        // Started playing in the background: apply the background treatment immediately
        if (Lower(processName, app!, fade: false))
            AppsMuted?.Invoke([app!.DisplayName]);
    }

    private bool ShouldPlay(string processName) =>
        processName.Equals(_foregroundProcess, StringComparison.OrdinalIgnoreCase);

    private void ScheduleLower(List<string> processNames)
    {
        if (GraceMs <= 0)
        {
            LowerNow(processNames);
            return;
        }

        var batch = new object();
        foreach (var processName in processNames)
            _pendingLowers[processName] = batch;

        Task.Delay(GraceMs).ContinueWith(_ => Dispatch(() =>
        {
            // Apps that regained focus meanwhile were removed or rescheduled under another batch
            var due = processNames
                .Where(n => _pendingLowers.TryGetValue(n, out var b) && b == batch)
                .ToList();
            foreach (var processName in due)
                _pendingLowers.Remove(processName);

            if (Enabled) LowerNow(due);
        }), TaskScheduler.Default);
    }

    private void LowerNow(List<string> processNames)
    {
        List<string>? newlyMuted = null;

        foreach (var processName in processNames)
        {
            if (_managedApps.TryGetValue(processName, out var app) && Lower(processName, app, fade: true))
                (newlyMuted ??= []).Add(app.DisplayName);
        }

        if (newlyMuted is { Count: > 0 })
            AppsMuted?.Invoke(newlyMuted);
    }

    // Returns true when the app was not altered before
    private bool Lower(string processName, ManagedApp app, bool fade)
    {
        int percent = Math.Clamp(app.BackgroundVolume, 0, 100);
        bool isNew = false;

        if (!_altered.TryGetValue(processName, out var altered))
        {
            // A restore still fading in knows the real original; the session would report a mid-fade value
            float? original = _transitions.TryGetValue(processName, out var running) && !running.Task.IsCompleted
                ? running.Target.Volume
                : null;
            original ??= _manager.GetVolumeByName(processName);
            if (original is null) return false; // Not playing anything yet

            altered = new AlteredApp { OriginalVolume = original.Value, Percent = -1 };
            _altered[processName] = altered;
            isNew = true;
        }

        if (altered.Percent != percent)
        {
            altered.Percent = percent;
            altered.Muted = percent == 0;
            SaveState();
        }

        // Mute keeps the session volume and only flips the mute flag, like muten always did
        var target = percent == 0
            ? new VolumeTarget(altered.OriginalVolume, Mute: true)
            : new VolumeTarget(altered.OriginalVolume * percent / 100f, Mute: false);
        StartTransition(processName, target, fade ? FadeMs : 0);

        return isNew;
    }

    private void Restore(string processName, bool fade)
    {
        if (_altered.Remove(processName, out var altered))
        {
            SaveState();
            StartTransition(processName, new VolumeTarget(altered.OriginalVolume, Mute: false), fade ? FadeMs : 0);
        }
        else if (!IsTransitionRunning(processName))
        {
            // Not altered by us: just make sure it is unmuted, as before
            StartTransition(processName, new VolumeTarget(null, Mute: false), 0);
        }
    }

    private bool IsTransitionRunning(string processName) =>
        _transitions.TryGetValue(processName, out var t) && !t.Task.IsCompleted;

    private void StartTransition(string processName, VolumeTarget target, int fadeMs)
    {
        _transitions.TryGetValue(processName, out var previous);

        // Already heading there; its final step covers every session, including new ones
        if (previous is not null && !previous.Task.IsCompleted && previous.Target == target) return;

        previous?.Cancellation.Cancel();
        var previousTask = previous?.Task ?? Task.CompletedTask;
        var cts = new CancellationTokenSource();

        var task = Task.Run(async () =>
        {
            // Let the cancelled fade stop before touching the volume, so the last write is always ours
            try { await previousTask; } catch { }
            await RunTransition(processName, target, fadeMs, cts.Token);
        });

        _transitions[processName] = new Transition(target, cts, task);
    }

    private async Task RunTransition(string processName, VolumeTarget target, int fadeMs, CancellationToken token)
    {
        try
        {
            if (fadeMs > 0)
                await Fade(processName, target, fadeMs, token);

            token.ThrowIfCancellationRequested();

            // Exact final state on every current session, including ones opened during the fade
            using var snapshot = _manager.GetSessionVolumes(processName);
            foreach (var volume in snapshot.Volumes)
            {
                try
                {
                    if (target.Mute)
                    {
                        volume.Mute = true;
                        if (target.Volume is { } original) volume.Volume = original;
                    }
                    else
                    {
                        if (target.Volume is { } level) volume.Volume = level;
                        volume.Mute = false;
                    }
                }
                catch
                {
                    // Session expired
                }
            }
        }
        catch
        {
            // Cancelled by a newer transition, or the sessions went away
        }
    }

    private async Task Fade(string processName, VolumeTarget target, int fadeMs, CancellationToken token)
    {
        if (target.Volume is null) return;

        // Muting fades to silence first; the mute flag and original volume are set afterwards
        float end = target.Mute ? 0f : target.Volume.Value;
        using var snapshot = _manager.GetSessionVolumes(processName);
        var volumes = snapshot.Volumes.ToList();
        var starts = new float[volumes.Count];
        bool anyToFade = false;

        for (int i = 0; i < volumes.Count; i++)
        {
            var volume = volumes[i];
            if (volume.Mute)
            {
                if (target.Mute)
                {
                    starts[i] = float.NaN; // Already silent
                    continue;
                }

                // Fade in from silence
                volume.Volume = 0f;
                volume.Mute = false;
                starts[i] = 0f;
            }
            else
            {
                starts[i] = volume.Volume;
            }

            if (Math.Abs(starts[i] - end) > 0.001f) anyToFade = true;
        }

        if (!anyToFade) return;

        int steps = Math.Max(1, fadeMs / FadeStepMs);
        for (int step = 1; step <= steps; step++)
        {
            await Task.Delay(FadeStepMs, token);
            float t = (float)step / steps;

            for (int i = 0; i < volumes.Count; i++)
            {
                if (float.IsNaN(starts[i])) continue;
                try { volumes[i].Volume = starts[i] + (end - starts[i]) * t; } catch { }
            }
        }
    }

    private void WaitForTransitions()
    {
        // Fades never need this thread, so blocking briefly cannot deadlock
        try { Task.WaitAll(_transitions.Values.Select(t => t.Task).ToArray(), TimeSpan.FromSeconds(2)); } catch { }
    }

    private void SaveState()
    {
        StateManager.Save(_altered
            .Select(kv => new AlteredAppState
            {
                ProcessName = kv.Key,
                OriginalVolume = kv.Value.OriginalVolume,
                Muted = kv.Value.Muted,
            })
            .ToList());
    }

    private void Dispatch(Action action)
    {
        void Run()
        {
            try { action(); } catch { }
        }

        if (_syncContext is null) Run();
        else _syncContext.Post(_ => Run(), null);
    }

    private class AlteredApp
    {
        public float OriginalVolume { get; init; }
        public bool Muted { get; set; }

        // Background volume currently applied; -1 when unknown (loaded from state.json)
        public int Percent { get; set; }
    }

    // Volume null leaves the volume alone
    private record VolumeTarget(float? Volume, bool Mute);

    private record Transition(VolumeTarget Target, CancellationTokenSource Cancellation, Task Task);
}
