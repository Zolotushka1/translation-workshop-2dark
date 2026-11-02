using System.Diagnostics;

namespace Workshop.Core;

public sealed partial class RuntimeControl
{
    private readonly SemaphoreSlim commandGate = new(1, 1);
    public bool Alive => !process.HasExited;
    public uint CompletedFrame => UInt(bridge + 76);
    public uint UpdateCount => UInt(bridge + 96);
    public uint CompletedRandomSeed => UInt(bridge + 104);
    public uint RawTime => UInt(Address(0x889e9c));
    public uint WorldTime => UInt(Address(0x88af88));
    public uint RandomSeed => UInt(Address(0x889b9c));
    public bool SceneSaveReady => HasLevel && UInt(UInt(Address(0x889c38)) + 0x24) != 0 &&
        new uint[] { 0x88af94, 0x889c80, 0x889acc }.All(x => UInt(Address(x)) == 0);
    public async Task WaitPausedAsync()
    {
        var timer = Stopwatch.StartNew();
        while (UInt(bridge + 44) == 0)
        {
            if (process.HasExited || timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Игра не подтвердила паузу обновления.");
            await Task.Delay(2);
        }
    }
    public void SetSynchronized(bool enabled)
    {
        if (bridge == 0 || process.HasExited) return;
        if (!enabled && UInt(bridge + 64) != 0 && Suspended && UInt(bridge + 44) != 0)
            RebaseWallClock();
        Put(bridge + 64, enabled ? 1u : 0u);
    }
    private void RebaseWallClock()
    {
        // Rendering continues while paused, so its real clock is newer than the last logic tick.
        uint time = UInt(bridge + 112);
        if (time == 0) time = UInt(bridge + 108);
        if (time == 0) return; // An older running bridge has no wall-clock samples.
        foreach (uint global in new uint[] { 0x88af8c, 0x889e9c, 0x88af6c }) Put(Address(global), time);
    }
    public void ConfigureSynchronization(RuntimeControl leader)
    {
        EnsureBridge();
        if (!Suspended) throw new InvalidOperationException("Перед настройкой синхронизации удержите обе игры на паузе.");
        foreach (uint global in new uint[] { 0x88af88, 0x7c968c, 0x889c64, 0x889e98 }) Put(Address(global), leader.UInt(leader.Address(global)));
        uint time = leader.RawTime;
        foreach (uint global in new uint[] { 0x88af8c, 0x88af6c, 0x889e9c }) Put(Address(global), time);
        foreach (uint offset in new uint[] { 68, 72, 76 }) Put(bridge + offset, 0);
        Put(bridge + 80, time); Put(bridge + 84, leader.RandomSeed);
        SetSynchronized(true);
    }
    public void GrantFrame(uint token, uint time, uint seed)
    {
        Put(bridge + 80, time); Put(bridge + 84, seed);
        // The permit is the last store: the game sees the complete input/time snapshot.
        Put(bridge + 68, token);
    }
    public async Task RefreshUiAsync()
    {
        if (UInt(Address(0x88b348)) != 0 && Read(Address(0x889ca9), 1)[0] != 0) await CommandAsync(4);
    }
}

public static class SyncTimeline
{
    public static uint Milliseconds(uint start, uint frame) => unchecked(start + (uint)((ulong)frame * 1000 / 60));
}
