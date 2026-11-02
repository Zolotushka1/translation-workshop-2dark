using System.Diagnostics;

namespace Workshop.Core;

/// <summary>One permit per shared 60 Hz step; neither process gets its next permit early.</summary>
public sealed class FrameSynchronizer : IAsyncDisposable
{
    private readonly RuntimeControl english, russian;
    private readonly Func<bool> canStep;
    private readonly Action<string>? report;
    private readonly CancellationTokenSource cancellation = new();
    private readonly SemaphoreSlim stepGate = new(1, 1);
    private readonly object inputGate = new();
    private readonly Task worker;
    private byte[] keys = new byte[512];
    private double mouseX = .5, mouseY = .5;
    private (int W, int H) enSize = (640, 360), ruSize = (640, 360);
    private uint token, epochFrame, origin, seed;
    private Pulse? pulse;
    private volatile bool pulseActive;
    private volatile bool active = true;
    public bool Active => active;
    public uint Frame => token;
    public bool Waiting { get; private set; }
    private sealed class Pulse(int key)
    {
        public int Key = key, Down = 2, Up = 1;
        public TaskCompletionSource Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public FrameSynchronizer(RuntimeControl english, RuntimeControl russian, Func<bool> canStep, Action<string>? report = null)
    {
        this.english = english; this.russian = russian; this.canStep = canStep; this.report = report;
        english.ConfigureSynchronization(english); russian.ConfigureSynchronization(english);
        origin = english.RawTime; seed = english.RandomSeed;
        worker = Task.Run(RunAsync);
    }
    public void SetInput(byte[] value, double x, double y)
    {
        if (value.Length != 512) throw new ArgumentException("Ожидалось 512 состояний ввода.");
        lock (inputGate) { keys = (byte[])value.Clone(); mouseX = x; mouseY = y; }
    }
    public void SetSizes((int Width, int Height) en, (int Width, int Height) ru)
    {
        lock (inputGate) { enSize = en; ruSize = ru; }
    }
    public async Task HoldAsync()
    {
        await stepGate.WaitAsync(); stepGate.Release();
    }
    public Task PulseKeyAsync(int key)
    {
        lock (inputGate)
        {
            if (pulse is not null) throw new InvalidOperationException("Предыдущее нажатие ещё выполняется.");
            pulse = new(key); pulseActive = true; return pulse.Completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private async Task RunAsync()
    {
        var clock = Stopwatch.StartNew(); double next = clock.Elapsed.TotalMilliseconds;
        long lastReport = -1000; bool transition = false;
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                if ((!canStep() && !pulseActive) || english.Suspended || russian.Suspended)
                { next = clock.Elapsed.TotalMilliseconds; await Task.Delay(2, cancellation.Token); continue; }
                double remaining = next - clock.Elapsed.TotalMilliseconds;
                if (remaining >= 1) { await Task.Delay(Math.Max(1, (int)remaining), cancellation.Token); continue; }
                await stepGate.WaitAsync(cancellation.Token);
                try
                {
                    if (!canStep() && !pulseActive) continue;
                    bool enReady = english.SceneSaveReady, ruReady = russian.SceneSaveReady;
                    bool enStep = true, ruStep = true;
                    if (enReady != ruReady)
                    {
                        transition = true; enStep = !enReady; ruStep = !ruReady;
                    }
                    else if (enReady && ruReady && transition && SceneCheckpoint.SameLevel(english.CurrentLevel, russian.CurrentLevel))
                    {
                        // The faster level waits; restore an identical native state before release.
                        english.Pause(); russian.Pause();
                        await Task.WhenAll(english.WaitPausedAsync(), russian.WaitPausedAsync());
                        string state = await english.SaveStateAsync();
                        await russian.LoadStateAsync(state, english.CurrentLevel);
                        english.ConfigureSynchronization(english); russian.ConfigureSynchronization(english);
                        token = epochFrame = 0; origin = english.RawTime; seed = english.RandomSeed;
                        english.Resume(); russian.Resume(); transition = false;
                    }
                    byte[] snapshot; double x, y; (int W, int H) es, rs;
                    lock (inputGate)
                    {
                        snapshot = (byte[])keys.Clone(); x = mouseX; y = mouseY; es = enSize; rs = ruSize;
                        if (pulse is not null) snapshot[pulse.Key] = pulse.Down > 0 ? (byte)1 : (byte)0;
                    }
                    token++; epochFrame++;
                    uint time = SyncTimeline.Milliseconds(origin, epochFrame);
                    var enPointer = ViewportPointer.Pixels(x, y, es.W, es.H);
                    var ruPointer = ViewportPointer.Pixels(x, y, rs.W, rs.H);
                    if (enStep) english.LinkedInput(snapshot, enPointer.X, enPointer.Y);
                    if (ruStep) russian.LinkedInput(snapshot, ruPointer.X, ruPointer.Y);
                    if (enStep) english.GrantFrame(token, time, seed);
                    if (ruStep) russian.GrantFrame(token, time, seed);
                    var timeout = Stopwatch.StartNew();
                    while ((enStep && english.CompletedFrame != token) || (ruStep && russian.CompletedFrame != token))
                    {
                        if (!english.Alive || !russian.Alive) throw new InvalidOperationException("Одна из тестовых копий выключена.");
                        if (timeout.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Игра не завершила общий шаг за 15 секунд.");
                        await Task.Delay(1, cancellation.Token);
                    }
                    seed = enStep ? english.CompletedRandomSeed : russian.CompletedRandomSeed;
                    lock (inputGate)
                    {
                        if (pulse is not null)
                        {
                            if (pulse.Down > 0) pulse.Down--; else pulse.Up--;
                            if (pulse.Up <= 0) { pulse.Completion.TrySetResult(); pulse = null; pulseActive = false; }
                        }
                    }
                    Waiting = !enStep || !ruStep;
                    if (clock.ElapsedMilliseconds - lastReport >= 500)
                    {
                        report?.Invoke(Waiting ? enStep ? "Синхро: ожидание EN" : "Синхро: ожидание RU" : $"Синхронно · шаг {token}");
                        lastReport = clock.ElapsedMilliseconds;
                    }
                }
                finally { stepGate.Release(); }
                next = Math.Max(next + 1000.0 / 60, clock.Elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { english.Pause(); russian.Pause(); } catch (Exception) { }
            report?.Invoke("Синхронная пауза: " + ex.Message);
        }
        finally
        {
            active = false;
            english.SetSynchronized(false); russian.SetSynchronized(false);
            lock (inputGate) { pulse?.Completion.TrySetCanceled(); pulse = null; pulseActive = false; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel(); await worker;
        english.SetSynchronized(false); russian.SetSynchronized(false);
        cancellation.Dispose();
    }
}
