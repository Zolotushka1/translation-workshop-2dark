using System.Security.Cryptography;

namespace Workshop.Core;

public static class NativeSceneState
{
    public static string Encode(byte[] data)
    {
        Validate(data);
        return "2DSTATE1:" + Convert.ToHexString(SHA256.HashData(data)) + ":" + Convert.ToBase64String(data);
    }
    public static byte[] Decode(string text)
    {
        var parts = text.Split(':', 3);
        if (parts.Length != 3 || parts[0] != "2DSTATE1") throw new InvalidDataException("Это не снимок состояния уровня 2Dark.");
        byte[] data;
        try { data = Convert.FromBase64String(parts[2]); }
        catch (FormatException ex) { throw new InvalidDataException("Повреждены данные сохранённой сцены.", ex); }
        Validate(data);
        if (!Convert.ToHexString(SHA256.HashData(data)).Equals(parts[1], StringComparison.Ordinal)) throw new InvalidDataException("Контрольная сумма сцены не совпадает.");
        return data;
    }
    private static void Validate(byte[] bytes)
    {
        if (bytes.Length < 64 || bytes.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("Неизвестный формат внутреннего состояния 2Dark.");
    }
}
