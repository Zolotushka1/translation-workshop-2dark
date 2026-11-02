using System.Buffers.Binary;
using System.Text;

namespace Workshop.Core;

public sealed record ArchiveEntry(string Name, int TableOffset, int PayloadOffset, int PackedSize);

/// <summary>Preserve untouched compressed blocks. New UTF-8 blocks use valid uncompressed QuickLZ frames.</summary>
public static class BigFile
{
    public static List<ArchiveEntry> Index(byte[] data)
    {
        if (data.Length < 16 || Read(data, 0) != 16) throw new InvalidDataException("Неизвестный заголовок .bigfile.");
        int count = Read(data, 4), payload = Read(data, 8);
        if (count < 0 || count > 100000 || payload < 16L + count * 8L || payload > data.Length) throw new InvalidDataException("Повреждённая таблица .bigfile.");
        int names = 16 + count * 8, offset = payload;
        var entries = new List<ArchiveEntry>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int table = 16 + i * 8, length = Read(data, table), size = Read(data, table + 4);
            if (length < 0 || length > payload - names || size < 0 || size > data.Length - offset) throw new InvalidDataException("Повреждённый размер ресурса.");
            string name = new UTF8Encoding(false, true).GetString(data, names, length);
            if (!unique.Add(name)) throw new InvalidDataException("Повторное имя ресурса в архиве.");
            entries.Add(new(name, table, offset, size));
            names += length;
            offset += size;
        }
        if (names != payload || offset != data.Length) throw new InvalidDataException("Размер архива не соответствует индексу.");
        return entries;
    }

    public static byte[] Replace(byte[] original, IReadOnlyDictionary<string, byte[]> replacements)
    {
        var entries = Index(original);
        if (replacements.Keys.Except(entries.Select(x => x.Name), StringComparer.Ordinal).Any()) throw new InvalidDataException("Заменяемый ресурс отсутствует в архиве.");
        var header = original[..Read(original, 8)];
        using var payload = new MemoryStream();
        Span<byte> lengths = stackalloc byte[8];
        foreach (var entry in entries)
        {
            if (replacements.TryGetValue(entry.Name, out var raw))
            {
                if (entry.PackedSize == 0) throw new InvalidDataException("Замена исходного пустого ресурса пока не поддерживается.");
                int size = checked(raw.Length + 9);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(entry.TableOffset + 4), size);
                payload.WriteByte(0x46); // long QuickLZ header, level 1, stored/uncompressed
                BinaryPrimitives.WriteInt32LittleEndian(lengths, size);
                BinaryPrimitives.WriteInt32LittleEndian(lengths[4..], raw.Length);
                payload.Write(lengths);
                payload.Write(raw);
            }
            else payload.Write(original, entry.PayloadOffset, entry.PackedSize);
        }
        using var output = new MemoryStream();
        output.Write(header);
        payload.Position = 0;
        payload.CopyTo(output);
        return output.ToArray();
    }

    private static int Read(byte[] data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
}
