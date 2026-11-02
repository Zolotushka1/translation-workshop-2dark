using System.Diagnostics;
using System.Text.Json;
using Workshop.Core;

if (args.Length < 3) { Console.WriteLine("PID exact-exe-path inspect|save|load|live [path]"); return; }
using var process = Process.GetProcessById(int.Parse(args[0]));
using var runtime = new RuntimeControl(process, args[1]);
switch (args[2])
{
    case "inspect":
        foreach (uint global in new uint[] { 0x88b348, 0x889acc, 0x88af94, 0x889c80 })
        {
            uint nativeObject = runtime.UInt(runtime.Address(global));
            Console.WriteLine($"global{global:X}={nativeObject:X8}; vt={(nativeObject == 0 ? 0 : runtime.UInt(nativeObject)):X8}");
        }
        uint windows = runtime.UInt(runtime.Address(0x889934));
        uint windowCount = runtime.UInt(windows + 0x14), windowItems = runtime.UInt(windows + 0x10);
        for (uint i = 0; i < Math.Min(20, windowCount); i++)
        {
            uint window = runtime.UInt(windowItems + 20 + i * 4);
            Console.WriteLine($"gui={window:X8}; vt={runtime.UInt(window):X8}; width={runtime.UInt(window + 0x18)}; height={runtime.UInt(window + 0x14)}");
        }
        Console.WriteLine($"assetsReady={runtime.Read(runtime.Address(0x889ca9), 1)[0]}; app={runtime.UInt(runtime.Address(0x8898d0)):X8}");
        uint hookEntry = runtime.Address(0x6831c0);
        if (runtime.Read(hookEntry, 1)[0] == 0xE9)
        {
            uint block = unchecked(hookEntry + 5 + (uint)BitConverter.ToInt32(runtime.Read(hookEntry + 1, 4))) - 256;
            Console.WriteLine($"bridge={block:X8}; pause={runtime.UInt(block + 40)}; ack={runtime.UInt(block + 44)}; callbacks={runtime.UInt(block + 48)}; sync={runtime.UInt(block + 64)}; permit={runtime.UInt(block + 68)}; consumed={runtime.UInt(block + 72)}; complete={runtime.UInt(block + 76)}; virtualMs={runtime.UInt(block + 80)}; seed={runtime.UInt(block + 84)}; updates={runtime.UInt(block + 96)}; endSeed={runtime.UInt(block + 104)}; worldMs={runtime.WorldTime}; rawMs={runtime.RawTime}");
        }
        uint game = runtime.UInt(runtime.Address(0x889c38));
        Console.WriteLine($"game={game:X8}; mode={runtime.UInt(runtime.Address(0x8899f8))}; localization={runtime.LocalizationGeneration:X8}");
        Console.WriteLine("CurrentLevel=" + runtime.CurrentLevel);
        if (game != 0)
        {
            Console.WriteLine(Convert.ToHexString(runtime.Read(game, 96)));
            foreach (uint offset in new uint[] { 0x24, 0x28 })
            {
                uint manager = runtime.UInt(game + offset);
                if (manager == 0) { Console.WriteLine($"manager{offset:X}=null"); continue; }
                Console.WriteLine($"manager{offset:X}={manager:X8} {Convert.ToHexString(runtime.Read(manager, 32))}");
                Console.WriteLine(runtime.ReadString(runtime.UInt(manager + 0x18))[..Math.Min(100, runtime.ReadString(runtime.UInt(manager + 0x18)).Length)]);
            }
        }
        break;
    case "save":
        string state = await runtime.SaveStateAsync(); File.WriteAllText(args[3], state); Console.WriteLine($"Saved {state.Length} characters to {args[3]}"); break;
    case "bridge":
        runtime.InitializeBridge();
        uint entry = runtime.Address(0x6831c0);
        uint code = unchecked(entry + 5 + (uint)BitConverter.ToInt32(runtime.Read(entry + 1, 4)));
        File.WriteAllBytes(args[3], runtime.Read(code, 600)); Console.WriteLine($"Bridge {code:X8}"); break;
    case "open": await runtime.OpenLevelAsync(args[3]); Console.WriteLine("Opened " + runtime.CurrentLevel); break;
    case "load": await runtime.LoadStateAsync(File.ReadAllText(args[3])); Console.WriteLine("Loaded"); break;
    case "live":
        var item = JsonSerializer.Deserialize<LiveTextChange[]>(File.ReadAllText(args[3]))!;
        Console.WriteLine("Changed " + runtime.ApplyTranslations(item)); break;
}
