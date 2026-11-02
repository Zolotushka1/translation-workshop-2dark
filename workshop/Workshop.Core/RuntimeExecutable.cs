using System.Text;

namespace Workshop.Core;

/// <summary>Keep test copies independent and their embedded viewports active.</summary>
public static class RuntimeExecutable
{
    public static byte[] ForRole(byte[] original, string role)
    {
        string replacement = role switch { "english" => "2DarkWorkshop_EN", "russian" => "2DarkWorkshop_RU", _ => throw new ArgumentException("Неизвестная тестовая роль.", nameof(role)) };
        byte[] token = Encoding.ASCII.GetBytes("2Dark_Gloomywood\0");
        byte[] changed = Encoding.ASCII.GetBytes(replacement + "\0");
        if (changed.Length != token.Length) throw new InvalidOperationException("Размер имени mutex изменился.");
        int index = original.AsSpan().IndexOf(token);
        if (index < 0 || original.AsSpan(index + token.Length).IndexOf(token) >= 0) throw new InvalidDataException("Имя проверки единственного экземпляра не соответствует исследованному EXE.");
        byte[] result = (byte[])original.Clone();
        changed.CopyTo(result, index);
        // Verified x86 GLFW focused-window query: compare window->HWND with GetActiveWindow.
        // A reparented child cannot be the active top-level window. Keep both test viewports
        // rendering while the translator edits, and use the explicit Esc controls for pausing.
        byte[] focus = Convert.FromHexString("568BF1FF1524B36F0033D23986340200005E0F94C28BC2C3");
        int focusIndex = original.AsSpan().IndexOf(focus);
        if (focusIndex < 0 || original.AsSpan(focusIndex + focus.Length).IndexOf(focus) >= 0)
            throw new InvalidDataException("Проверка активности GLFW не соответствует исследованному EXE.");
        // mov eax,1; ret. Register-argument function, no stack parameters or callee cleanup.
        new byte[] { 0xB8, 1, 0, 0, 0, 0xC3 }.CopyTo(result, focusIndex);
        return result;
    }
}
