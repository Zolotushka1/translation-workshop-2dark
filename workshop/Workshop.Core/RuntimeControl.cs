using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Workshop.Core;

public sealed record LiveTextChange(string Key, string Value, IReadOnlyCollection<string> KnownValues);

/// <summary>Version-bound controller for the workshop's own 32-bit test processes only.</summary>
public sealed partial class RuntimeControl : IDisposable
{
    private readonly Process process;
    private readonly SafeProcessHandle handle;
    private readonly uint image;
    private readonly string runtimeDirectory;
    private uint bridge;
    private uint pinnedBytes;
    private uint stringArena;
    private int stringArenaUsed, stringArenaCapacity;
    private readonly Dictionary<string, uint> pinnedStrings = new(StringComparer.Ordinal);
    private readonly object memoryGate = new();
    private readonly Dictionary<(uint Node, string Key), string> baselineText = [];
    public bool Suspended => bridge != 0 && !process.HasExited && UInt(bridge + 40) != 0;
    public void InitializeBridge() => EnsureBridge();
    public int ProcessId => process.Id;
    public bool HasLevel => UInt(Address(0x889c38)) != 0 && UInt(Address(0x8899f8)) == 1;
    public string CurrentLevel => ReadString(UInt(Address(0x7bddcc)));
    public uint Address(uint preferred) => checked(image + preferred - 0x400000);

    public RuntimeControl(Process process, string expectedExecutable)
    {
        if (!Path.GetFullPath(process.MainModule!.FileName).Equals(Path.GetFullPath(expectedExecutable), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Контроллер работает только с выбранной тестовой копией.");
        var directory = new DirectoryInfo(Path.GetDirectoryName(expectedExecutable)!);
        if (directory.Parent?.Name != "runtime" || directory.Name is not ("english" or "russian") || directory.Parent.Parent is null)
            throw new InvalidOperationException("Контроллер доступен только для runtime/english и runtime/russian.");
        string original = Path.Combine(Corpus.ResolveGameRoot(directory.Parent.Parent.FullName), "2Dark.exe");
        byte[] source = File.ReadAllBytes(original);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory.Parent.Parent.FullName, "original_manifest.json")));
        string expectedHash = manifest.RootElement.EnumerateArray().First(x => x.GetProperty("path").GetString() == "2Dark.exe").GetProperty("sha256").GetString()!;
        if (!Convert.ToHexString(SHA256.HashData(source)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Исходный EXE отличается от исследованной версии.");
        if (!File.ReadAllBytes(expectedExecutable).SequenceEqual(RuntimeExecutable.ForRole(source, directory.Name)))
            throw new InvalidDataException("Тестовый EXE отличается от поддерживаемой сборки.");
        this.process = process;
        runtimeDirectory = directory.FullName;
        image = checked((uint)process.MainModule.BaseAddress.ToInt64());
        handle = Native.OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400 | 0x0800, false, process.Id);
        if (handle.IsInvalid) throw new Win32Exception();
        // All offsets below belong to this exact executable; ASLR is accounted for.
        if (!Read(Address(0x5cdd20), 9).SequenceEqual(Convert.FromHexString("558BEC6AFF68762D6E")))
        {
            // The immediate SEH address is relocated by Windows, compare the instruction prefix instead.
            if (!Read(Address(0x5cdd20), 6).SequenceEqual(Convert.FromHexString("558BEC6AFF68")))
                throw new InvalidDataException("Неизвестная версия таблицы локализации в памяти.");
        }
    }

    public byte[] Read(uint address, int length)
    {
        if (length < 0 || length > 32 * 1024 * 1024) throw new InvalidDataException("Недопустимый размер блока игры.");
        var bytes = new byte[length];
        if (length > 0 && (!Native.ReadProcessMemory(handle, (nint)address, bytes, (nuint)length, out var read) || read != (nuint)length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Не удалось прочитать память игры: {address:X8}.");
        return bytes;
    }
    public uint UInt(uint address) => BitConverter.ToUInt32(Read(address, 4));
    public void Write(uint address, byte[] bytes)
    {
        if (!Native.WriteProcessMemory(handle, (nint)address, bytes, (nuint)bytes.Length, out var written) || written != (nuint)bytes.Length)
            throw new Win32Exception();
    }
    private void Put(uint address, uint value) => Write(address, BitConverter.GetBytes(value));
    public string ReadString(uint rep)
    {
        if (rep == 0) return "";
        uint length = UInt(rep + 4);
        if (length > 8_000_000) throw new InvalidDataException("Неизвестный формат строки движка.");
        return Encoding.Unicode.GetString(Read(rep + 8, checked((int)length * 2)));
    }
    private uint Allocate(int length, bool executable = false)
    {
        nint address = Native.VirtualAllocEx(handle, 0, (nuint)length, 0x3000, executable ? 0x40u : 4u);
        if (address == 0) throw new Win32Exception();
        return checked((uint)address.ToInt64());
    }
    private uint PinString(string text)
    {
        if (pinnedStrings.TryGetValue(text, out uint existing)) return existing;
        // Monkey C++ String::Rep: reference count, UTF-16 length, characters.
        // These small process-local allocations live until the test process exits.
        // A high reference count prevents the game's CRT from trying to free VirtualAlloc memory.
        byte[] bytes = new byte[checked(10 + text.Length * 2)];
        BitConverter.GetBytes(0x10000000).CopyTo(bytes, 0);
        BitConverter.GetBytes(text.Length).CopyTo(bytes, 4);
        Encoding.Unicode.GetBytes(text).CopyTo(bytes, 8);
        if (pinnedBytes + bytes.Length > 32 * 1024 * 1024) throw new InvalidOperationException("За сеанс применено 32 МиБ текста. Перезапустите тестовую копию для очистки памяти.");
        int aligned = checked((bytes.Length + 7) & ~7);
        if (stringArena == 0 || stringArenaUsed + aligned > stringArenaCapacity)
        {
            stringArenaCapacity = Math.Max(1024 * 1024, aligned);
            stringArena = Allocate(stringArenaCapacity); stringArenaUsed = 0;
        }
        uint address = stringArena + (uint)stringArenaUsed;
        Write(address, bytes); stringArenaUsed += aligned; pinnedBytes += (uint)bytes.Length;
        pinnedStrings.Add(text, address);
        return address;
    }

    public void Pause()
    {
        if (process.HasExited) return;
        EnsureBridge();
        Put(bridge + 44, 0); Put(bridge + 40, 1);
    }
    public void Resume()
    {
        if (bridge != 0 && !process.HasExited)
        {
            if (Suspended && UInt(bridge + 44) != 0 && UInt(bridge + 64) == 0) RebaseWallClock();
            Put(bridge + 40, 0);
        }
    }

    public uint LocalizationGeneration
    {
        get { uint map = UInt(Address(0x8897b0)); return map == 0 ? 0 : UInt(map + 16) ^ UInt(Address(0x889c38)); }
    }

    public int ApplyTranslations(IReadOnlyCollection<LiveTextChange> values)
    {
        lock (memoryGate) return ApplyTranslationsCore(values);
    }

    private int ApplyTranslationsCore(IReadOnlyCollection<LiveTextChange> values)
    {
        // Pointer stores are aligned/atomic; reps are immutable and stay alive. Never suspend
        // the window thread: a suspended child can deadlock its WPF parent's input queue.
        uint map = UInt(Address(0x8897b0));
        if (map == 0) throw new InvalidOperationException("Игра ещё не загрузила таблицу локализации.");
        var pending = new Stack<uint>(); pending.Push(UInt(map + 16));
        var byKey = values.ToLookup(x => x.Key, StringComparer.Ordinal);
        var seen = new HashSet<uint>();
        int changed = 0;
        while (pending.Count > 0)
        {
            uint node = pending.Pop();
            if (node == 0 || !seen.Add(node)) continue;
            if (seen.Count > 50_000) throw new InvalidDataException("Неожиданная структура таблицы локализации.");
            byte[] data = Read(node, 36);
            uint keyRep = BitConverter.ToUInt32(data, 16), valueRep = BitConverter.ToUInt32(data, 28);
            string key = ReadString(keyRep);
            string current = ReadString(valueRep);
            string baseline = baselineText.GetValueOrDefault((node, key), current);
            var all = byKey[key].ToList();
            var candidates = all.Select(x => x.Value).Distinct().ToList();
            if (candidates.Count > 1) candidates = all.Where(x => x.KnownValues.Contains(current) || x.KnownValues.Contains(baseline)).Select(x => x.Value).Distinct().ToList();
            if (candidates.Count == 1 && current != candidates[0])
            {
                baselineText.TryAdd((node, key), current);
                Put(node + 28, PinString(candidates[0]));
                changed++;
            }
            pending.Push(BitConverter.ToUInt32(data, 20)); pending.Push(BitConverter.ToUInt32(data, 24));
        }
        return changed;
    }

    private uint SaveManager()
    {
        uint game = UInt(Address(0x889c38));
        if (game == 0 || UInt(Address(0x8899f8)) != 1)
            throw new InvalidOperationException("Для сохранения или загрузки откройте игровой уровень.");
        uint manager = UInt(game + 0x24);
        if (manager == 0) throw new InvalidOperationException("Менеджер сохранения ещё не готов.");
        return manager;
    }

    public void CheckSceneReady()
    {
        SaveManager();
        if (new uint[] { 0x88af94, 0x889c80, 0x889acc }.Any(x => UInt(Address(x)) != 0))
            throw new InvalidOperationException("Закройте внутриигровые меню и дождитесь завершения перехода перед сохранением или загрузкой сцены.");
    }

    public async Task<string> SaveStateAsync()
    {
        CheckSceneReady();
        await CommandAsync(1);
        uint levelState = UInt(SaveManager() + 0x10);
        uint dynamicBuffer = UInt(levelState + 0x10);
        uint nativeBuffer = UInt(dynamicBuffer + 0x10);
        uint length = UInt(dynamicBuffer + 0x18);
        if (length > UInt(nativeBuffer + 0x14) || length > 16 * 1024 * 1024) throw new InvalidDataException("Повреждён внутренний буфер сохранения.");
        return NativeSceneState.Encode(Read(UInt(nativeBuffer + 0x10), checked((int)length)));
    }

    public void LinkedInput(byte[] keys, float x, float y, bool release = false)
    {
        lock (memoryGate)
        {
            if (keys.Length != 512) throw new ArgumentException("Ожидалось 512 состояний клавиш.");
            if (release && bridge == 0) return;
            EnsureBridge();
            Write(bridge + 4096, keys);
            Write(bridge + 28, BitConverter.GetBytes(x).Concat(BitConverter.GetBytes(y)).ToArray());
            Put(bridge + 24, release ? 2u : 1u);
        }
    }

    public async Task PressKeyAsync(int key)
    {
        if (key is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(key));
        EnsureBridge();
        Write(bridge + 4096, new byte[512]);
        Write(bridge + 4096 + (uint)key, [1]); Put(bridge + 24, 1);
        try { await Task.Delay(140); }
        finally { Write(bridge + 4096 + (uint)key, [0]); Put(bridge + 24, 2); }
    }

    public async Task LoadStateAsync(string state, string? level = null)
    {
        byte[] bytes = NativeSceneState.Decode(state);
        CheckSceneReady();
        if (level is not null && !SceneCheckpoint.SameLevel(CurrentLevel, level))
            throw new InvalidOperationException($"Откройте уровень «{level}» перед загрузкой этой сцены. Сейчас открыт «{CurrentLevel}».");
        EnsureBridge();
        uint buffer = Allocate(bytes.Length); Write(buffer, bytes);
        Put(bridge + 20, buffer); Put(bridge + 36, (uint)bytes.Length);
        try { await CommandAsync(2); }
        finally { if (!process.HasExited && UInt(bridge + 4) == 0) Native.VirtualFreeEx(handle, (nint)buffer, 0, 0x8000); }
    }

    public async Task OpenLevelAsync(string level, bool holdWhenReady = false)
    {
        var launch = SceneCheckpoint.LevelLaunch(level); string path = launch.Map;
        string? archive = Directory.EnumerateFiles(Path.Combine(runtimeDirectory, "data"), "*.bigfile", SearchOption.AllDirectories)
            .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Equals(launch.Name, StringComparison.OrdinalIgnoreCase));
        if (archive is null || !BigFile.Index(File.ReadAllBytes(archive)).Any(x => x.Name == path)) throw new InvalidDataException("Ресурс уровня из снимка отсутствует в тестовой копии.");
        EnsureBridge(); Resume();
        await WaitForStartupAsync();
        if (!HasLevel || !SceneCheckpoint.SameLevel(CurrentLevel, path))
        {
            Put(bridge + 52, PinString(launch.Directory)); Put(bridge + 56, PinString(launch.Name));
            await CommandAsync(3);
        }
        var timer = Stopwatch.StartNew();
        while (!HasLevel || !SceneCheckpoint.SameLevel(CurrentLevel, path) || UInt(UInt(Address(0x889c38)) + 0x24) == 0)
        {
            if (process.HasExited) throw new InvalidOperationException("Игра завершилась во время открытия уровня.");
            if (timer.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Движок не завершил открытие уровня за 60 секунд.");
            if (HasLevel && SceneCheckpoint.SameLevel(CurrentLevel, path)) await PressKeyAsync(32);
            await Task.Delay(50);
        }
        if (holdWhenReady) { Pause(); await WaitPausedAsync(); }
    }

    public async Task WaitForStartupAsync()
    {
        EnsureBridge(); Resume();
        var assets = Stopwatch.StartNew();
        while (Read(Address(0x889ca9), 1)[0] == 0)
        {
            if (process.HasExited || assets.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Игра ещё не завершила начальную загрузку ресурсов.");
            await Task.Delay(100);
        }
        // Startup logos can schedule the main menu after an early transition. Wait for
        // a stable native menu/game screen, advancing startup cards with the normal key.
        while (!HasLevel)
        {
            uint screen = UInt(Address(0x88b348));
            if (screen != 0 && UInt(screen) == Address(0x714dd8)) break;
            if (process.HasExited || assets.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException("Игра ещё не открыла главное меню.");
            await PressKeyAsync(32); await Task.Delay(100);
        }
    }

    private async Task CommandAsync(uint command)
    {
        await commandGate.WaitAsync();
        try
        {
            EnsureBridge();
            if (UInt(bridge + 4) != 0) throw new InvalidOperationException("Предыдущая команда игры ещё выполняется.");
            Put(bridge + 12, 0xFFFFFFFF); Put(bridge + 4, command);
            var timer = Stopwatch.StartNew();
            while (UInt(bridge + 4) != 0)
            {
                if (process.HasExited) throw new InvalidOperationException("Тестовая игра завершилась.");
                if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException("Игровой поток не завершил команду за 12 секунд.");
                await Task.Delay(15);
            }
            uint status = UInt(bridge + 12);
            if (status != 0) throw new InvalidOperationException(status switch
            {
                2 => "Закройте внутриигровое меню или дождитесь завершения перехода и повторите команду.",
                3 => "Движок не смог выделить память для снимка сцены.",
                _ => "Сохранение доступно только в загруженном игровом уровне."
            });
        }
        finally { commandGate.Release(); }
    }

    public void Dispose()
    {
        if (!process.HasExited && bridge != 0) { Write(bridge + 4096, new byte[512]); Put(bridge + 24, 2); }
        SetSynchronized(false); Resume(); handle.Dispose();
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int processId);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] data, nuint size, out nuint read);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] data, nuint size, out nuint written);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern nint VirtualAllocEx(SafeProcessHandle process, nint address, nuint size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool VirtualFreeEx(SafeProcessHandle process, nint address, nuint size, uint type);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool VirtualProtectEx(SafeProcessHandle process, nint address, nuint size, uint protect, out uint old);
        [DllImport("kernel32.dll")] internal static extern bool FlushInstructionCache(SafeProcessHandle process, nint address, nuint size);
        [DllImport("ntdll.dll")] internal static extern int NtSuspendProcess(SafeProcessHandle process);
        [DllImport("ntdll.dll")] internal static extern int NtResumeProcess(SafeProcessHandle process);
    }
}

internal sealed class X86(uint origin)
{
    private readonly List<byte> code = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly List<(int Position, string Label)> fixups = [];
    public void Emit(params byte[] bytes) => code.AddRange(bytes);
    public void Emit(string hex) => code.AddRange(Convert.FromHexString(hex));
    public void U32(uint value) => code.AddRange(BitConverter.GetBytes(value));
    public void Label(string name) => labels.Add(name, code.Count);
    public void Jump(string condition, string label)
    {
        Emit(condition.Length == 0 ? "E9" : "0F" + condition);
        fixups.Add((code.Count, label)); U32(0);
    }
    public void Call(uint address) { Emit(0xE8); Relative(address); }
    public void JumpAbsolute(uint address) { Emit(0xE9); Relative(address); }
    private void Relative(uint address) => U32(unchecked(address - origin - (uint)code.Count - 4));
    public byte[] Finish()
    {
        foreach (var fixup in fixups)
            for (int i = 0; i < 4; i++) code[fixup.Position + i] = BitConverter.GetBytes(labels[fixup.Label] - fixup.Position - 4)[i];
        return code.ToArray();
    }
}
