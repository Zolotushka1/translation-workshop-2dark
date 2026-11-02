using System.Globalization;
using System.Text.Json;

namespace Workshop.Core;

public sealed class WorkshopSettings
{
    public int Version { get; set; } = 1;
    public string FullscreenKey { get; set; } = "F11";
    public string SwitchVersionKey { get; set; } = "Tab";
    public bool ShowVersionLabel { get; set; } = true;
    public string LabelPosition { get; set; } = "Сверху слева";
    public string LabelColor { get; set; } = "#FFFFFF";
    public double LabelOpacity { get; set; } = .8;
    public double LabelSize { get; set; } = 24;
    public bool SingleAudioSource { get; set; } = true;
    public string AudioSource { get; set; } = "EN";
    public static string[] ShortcutKeys { get; } = ["Tab", "F6", "F7", "F8", "F9", "F10", "F11", "F12"];
    public static string[] Positions { get; } = ["Сверху слева", "Сверху по центру", "Сверху справа", "По центру слева", "По центру", "По центру справа", "Снизу слева", "Снизу по центру", "Снизу справа"];

    public void Validate()
    {
        if (Version != 1 || !ShortcutKeys.Contains(FullscreenKey) || !ShortcutKeys.Contains(SwitchVersionKey) || FullscreenKey == SwitchVersionKey)
            throw new InvalidDataException("Выберите разные клавиши для полного экрана и переключения EN/RU.");
        if (!Positions.Contains(LabelPosition) || AudioSource is not ("EN" or "RU")) throw new InvalidDataException("Неизвестное значение настройки.");
        if (LabelColor.Length != 7 || LabelColor[0] != '#' || !uint.TryParse(LabelColor.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("Цвет надписи задаётся в формате #RRGGBB, например #FFFFFF.");
        if (!double.IsFinite(LabelOpacity) || LabelOpacity is < 0 or > 1 || !double.IsFinite(LabelSize) || LabelSize is < 14 or > 64)
            throw new InvalidDataException("Прозрачность должна быть от 0 до 100%, размер надписи — от 14 до 64.");
    }
    public static WorkshopSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var value = JsonSerializer.Deserialize<WorkshopSettings>(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Пустые настройки мастерской.");
        value.Validate(); return value;
    }
    public static void Save(string path, WorkshopSettings value)
    {
        value.Validate(); BuildService.WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions { WriteIndented = true }));
    }
    public WorkshopSettings Copy() => (WorkshopSettings)MemberwiseClone();
    public string FullscreenHint(bool russian) => russian ? FullscreenKey + " — выход" : FullscreenKey + " to quit";
    public (double X, double Y) LabelOffset(double width, double height, double textWidth, double textHeight)
    {
        int position = Array.IndexOf(Positions, LabelPosition);
        return (((position % 3) switch { 0 => 20, 1 => (width - textWidth) / 2, _ => width - textWidth - 20 }),
            ((position / 3) switch { 0 => 20, 1 => (height - textHeight) / 2, _ => height - textHeight - 20 }));
    }
}
