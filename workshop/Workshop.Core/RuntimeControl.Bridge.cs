using System.ComponentModel;
using System.Text;

namespace Workshop.Core;

public sealed partial class RuntimeControl
{
    private void EnsureBridge()
    {
        if (bridge != 0) return;
        uint entry = Address(0x6831c0), oldPause = 0;
        if (Read(entry, 1)[0] == 0xE9)
        {
            uint destination = unchecked(entry + 5 + (uint)BitConverter.ToInt32(Read(entry + 1, 4)));
            string version = ""; uint previous = 0;
            foreach (uint offset in new uint[] { 256, 64 })
            {
                try
                {
                    string marker = Encoding.ASCII.GetString(Read(destination - offset, 4));
                    if (marker is "2DW2" or "2DW3" or "2DW4" or "2DW5") { version = marker; previous = destination - offset; break; }
                }
                catch (Win32Exception) { }
            }
            if (version == "2DW5") { bridge = previous; return; }
            if (version is not ("2DW2" or "2DW3" or "2DW4")) throw new InvalidDataException("Неизвестный игровой мост. Выключите и включите тестовую копию.");
            if (version is "2DW2" or "2DW3") Native.NtResumeProcess(handle);
            else oldPause = UInt(previous + 40);
            if (UInt(previous + 4) != 0) throw new InvalidOperationException("Дождитесь завершения предыдущей команды игры.");
            Put(previous + 24, 0);
        }
        else if (!Read(entry, 5).SequenceEqual(Convert.FromHexString("558BEC6AFF")))
            throw new InvalidDataException("Неизвестный обработчик кадра. Выключите и включите тестовую копию.");
        bridge = Allocate(8192, true);
        Write(bridge, Encoding.ASCII.GetBytes("2DW5")); Put(bridge + 40, oldPause);
        uint code = bridge + 256, original = bridge + 2048;
        var a = new X86(code);
        a.Emit("9C608BDC81EC1002000083E4F0899C24000200000FAE0424");
        a.Emit(0xBB); a.U32(bridge);
        a.Emit("8B430485C0"); a.Jump("84", "gate");
        a.Emit("83F803"); a.Jump("84", "openLevel");
        a.Emit("83F804"); a.Jump("84", "refreshUi");
        a.Emit("8B0D"); a.U32(Address(0x889c38)); a.Emit("85C9"); a.Jump("84", "noLevel");
        a.Emit("833D"); a.U32(Address(0x8899f8)); a.Emit(1); a.Jump("85", "noLevel");
        foreach (uint global in new uint[] { 0x88af94, 0x889c80, 0x889acc })
        {
            a.Emit("833D"); a.U32(Address(global)); a.Emit(0); a.Jump("85", "blocked");
        }
        a.Emit("83F801"); a.Jump("85", "load");
        a.Emit("8B4924"); a.Call(Address(0x56ffe0)); a.Jump("", "success");
        a.Label("load");
        a.Emit("8B71248B7E108B6F108B7D10FF7324"); a.Call(Address(0x43170c)); a.Emit("83C40485C0"); a.Jump("84", "outOfMemory");
        a.Emit("50FF7710"); a.Call(Address(0x42cd74)); a.Emit("83C404588947108B5324895714895518C745140000000052FF731450");
        a.Call(Address(0x4439f0)); a.Emit("83C40CC64614018BCE"); a.Call(Address(0x56f9f0)); a.Jump("", "success");
        a.Label("openLevel");
        a.Call(Address(0x47d2f0));
        a.Emit("B954000000"); a.Call(Address(0x68f640)); a.Emit("85C0"); a.Jump("84", "outOfMemory");
        a.Emit("8BC8"); a.Call(Address(0x4b4e10));
        a.Emit("8BF06A00FF7334FF73388BCE"); a.Call(Address(0x4b3740));
        a.Emit("8BC8C605"); a.U32(Address(0x889828)); a.Emit(0); a.Call(Address(0x6073b0)); a.Jump("", "success");
        a.Label("refreshUi");
        a.Call(Address(0x608240)); a.Call(Address(0x608450));
        a.Emit("8B0D"); a.U32(Address(0x88b348)); a.Emit("85C9"); a.Jump("84", "noLevel");
        // Exactly the same screen/window reflow that myApp::OnUpdate performs on resize.
        a.Emit("8B01FF5014"); a.Call(Address(0x5dc2a0));
        // WinHelp owns a separate HelpDisplay, omitted by its generic GUI resize handler.
        a.Emit("8B3D"); a.U32(Address(0x889934)); a.Emit("85FF"); a.Jump("84", "success");
        a.Emit("8B77148B7F1083C71485F6"); a.Jump("84", "success");
        a.Label("refreshWindow"); a.Emit("8B0F8139"); a.U32(Address(0x703008)); a.Jump("85", "nextWindow");
        a.Emit("8B89B400000085C9"); a.Jump("84", "nextWindow"); a.Call(Address(0x4b7000));
        a.Label("nextWindow"); a.Emit("83C7044E"); a.Jump("85", "refreshWindow"); a.Jump("", "success");
        a.Label("outOfMemory"); a.Emit("C7430C03000000"); a.Jump("", "complete");
        a.Label("noLevel"); a.Emit("C7430C01000000"); a.Jump("", "complete");
        a.Label("blocked"); a.Emit("C7430C02000000"); a.Jump("", "complete");
        a.Label("success"); a.Emit("C7430C00000000");
        a.Label("complete"); a.Emit("FF4308C7430400000000");
        a.Label("gate");
        a.Emit("FF4330837B2800"); a.Jump("85", "skip");
        a.Emit("837B4000"); a.Jump("84", "input");
        // Do not consume a permit until the previous render has prepared engine resources.
        a.Emit("803D"); a.U32(Address(0x889ca9)); a.Emit(0); a.Jump("84", "skip");
        a.Emit("8B43443B4348"); a.Jump("84", "skip"); a.Emit("894348");
        a.Emit("8B4354A3"); a.U32(Address(0x889b9c));
        a.Label("input");
        a.Emit("837B1800"); a.Jump("84", "run");
        // Recover the original delegate `this` from pushad; commands may clobber ECX.
        a.Emit("8B8C24000200008B49188B711885F6"); a.Jump("84", "run");
        a.Emit("33FF"); a.Label("keyLoop"); a.Emit("8B5614");
        a.Emit("0FB6843B001000000FB6543A143BC2"); a.Jump("84", "nextKey");
        a.Emit("5785C0B802000000"); a.Jump("84", "keyEvent"); a.Emit("B801000000");
        a.Label("keyEvent"); a.Emit("508BCE"); a.Call(Address(0x67d680));
        a.Label("nextKey"); a.Emit("4781FF00020000"); a.Jump("8C", "keyLoop");
        a.Emit("8B431C8946308B4E388941148B43208946348B4E3C894114837B1802"); a.Jump("85", "run");
        a.Emit("C7431800000000");
        a.Label("run"); a.Emit("C7432C000000000FAE0C248BA42400020000619D");
        a.Call(original);
        // Acknowledge only AFTER the original update and input cleanup have returned.
        a.Emit("9C60B8"); a.U32(bridge); a.Emit("8B504489504CFF40608B15"); a.U32(Address(0x889b9c)); a.Emit("895068619DC3");
        a.Label("skip"); a.Emit("C7432C010000000FAE0C248BA42400020000619D33C0C3");
        byte[] dispatch = a.Finish();
        if (dispatch.Length > 1792) throw new InvalidOperationException("Обработчик кадра превышает выделенный блок.");
        Write(code, dispatch);
        var trampoline = new X86(original); trampoline.Emit("558BEC6AFF"); trampoline.JumpAbsolute(entry + 5); Write(original, trampoline.Finish());
        var patches = new List<(uint Entry, byte[] Bytes)>();
        var jump = new X86(entry); jump.JumpAbsolute(code); patches.Add((entry, jump.Finish()));
        // Replace the wall-clock value at its verified use sites, leaving GLFW's scheduler intact.
        foreach (var (clock, offset) in new[] { (0x63b18bu, 2176u), (0x63b0bbu, 2304u) })
        {
            uint site = Address(clock), stub = bridge + offset;
            byte[] prefix = Read(site, 8);
            if (!prefix.AsSpan(0, 4).SequenceEqual(Convert.FromHexString("F30F1015"))) throw new InvalidDataException("Неизвестный обработчик времени игры.");
            var c = new X86(stub);
            c.Emit("890D"); c.U32(bridge + (clock == 0x63b18b ? 108u : 112u));
            c.Emit("833D"); c.U32(bridge + 64); c.Emit(0); c.Jump("84", "nativeClock");
            c.Emit("8B0D"); c.U32(bridge + 80);
            c.Label("nativeClock"); c.Emit(prefix); c.JumpAbsolute(site + 8); Write(stub, c.Finish());
            var j = new X86(site); j.JumpAbsolute(stub); j.Emit("909090"); patches.Add((site, j.Finish()));
        }
        int status = Native.NtSuspendProcess(handle);
        if (status != 0) throw new InvalidOperationException("Не удалось установить обработчик кадра.");
        try
        {
            foreach (var patch in patches)
            {
                if (!Native.VirtualProtectEx(handle, (nint)patch.Entry, (nuint)patch.Bytes.Length, 0x40, out uint protection)) throw new Win32Exception();
                Write(patch.Entry, patch.Bytes);
                Native.VirtualProtectEx(handle, (nint)patch.Entry, (nuint)patch.Bytes.Length, protection, out _);
                Native.FlushInstructionCache(handle, (nint)patch.Entry, (nuint)patch.Bytes.Length);
            }
        }
        finally { Native.NtResumeProcess(handle); }
    }
}
