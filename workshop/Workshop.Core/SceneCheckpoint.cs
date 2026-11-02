using System.Text.Json;

namespace Workshop.Core;

public sealed class SceneCheckpoint
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string? English { get; set; }
    public string? Russian { get; set; }
    public string? EnglishLevel { get; set; }
    public string? RussianLevel { get; set; }
    public static string NormalizeLevel(string? path) => (path ?? "").Replace("monkey://data/", "", StringComparison.OrdinalIgnoreCase).Replace('\\', '/').TrimStart('/');
    public static bool SameLevel(string? left, string? right) => NormalizeLevel(left).Equals(NormalizeLevel(right), StringComparison.OrdinalIgnoreCase);
    public static (string Map, string Name, string Directory) LevelLaunch(string level)
    {
        string path = NormalizeLevel(level); string[] parts = path.Split('/');
        if (parts.Length != 3 || parts[0] != "levels" || parts[1].Length == 0 || !parts[1].All(char.IsAsciiLetterOrDigit) || !parts[2].EndsWith(".tmx", StringComparison.Ordinal) || parts[2].Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException("В снимке указан неизвестный путь уровня.");
        return (path, parts[1], "monkey://data/levels/" + parts[1]);
    }
    public override string ToString() => Title;
    public static void Save(string path, SceneCheckpoint checkpoint) => BuildService.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(checkpoint));
    public static SceneCheckpoint Load(string path)
    {
        var item = JsonSerializer.Deserialize<SceneCheckpoint>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Пустая сцена.");
        if (item.Version != 1 || item.English is null && item.Russian is null) throw new InvalidDataException("Неизвестный формат сцены.");
        return item;
    }
}
