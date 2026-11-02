using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workshop.Core;

public sealed class TranslationEdit
{
    public string Target { get; set; } = "";
    public string Note { get; set; } = "";
    public string Status { get; set; } = "Черновик";
    public string SourceHash { get; set; } = "";
}

public sealed class TranslationProject
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Мой перевод 2Dark";
    public bool UseLegacyFonts { get; set; } = true;
    public Dictionary<string, TranslationEdit> Edits { get; set; } = new(StringComparer.Ordinal);
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class ProjectStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static TranslationProject Load(string path)
    {
        if (!File.Exists(path)) return new();
        var project = JsonSerializer.Deserialize<TranslationProject>(File.ReadAllBytes(path), Json) ?? throw new InvalidDataException("Пустой файл проекта.");
        if (project.Version != 1) throw new InvalidDataException($"Версия проекта {project.Version} не поддерживается.");
        if (project.Edits is null || project.Edits.Any(x => x.Value is null || x.Value.Target is null || x.Value.Note is null))
            throw new InvalidDataException("Повреждённые записи проекта.");
        return project;
    }

    public static void Save(string path, TranslationProject project)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        project.UpdatedUtc = DateTime.UtcNow;
        var temporary = full + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, project, Json);
            output.Flush(true);
        }
        if (File.Exists(full)) File.Copy(full, full + ".bak", true);
        File.Move(temporary, full, true);
    }
}

public sealed record CorpusItem(string File, int Records, int Words);
public sealed record CorpusRow(string Section, string Archive, string File, TextDocument Document,
    TextRecord Record, string French, string Legacy)
{
    public IReadOnlyDictionary<string, string> Variants { get; init; } = new Dictionary<string, string>();
}

public sealed class Corpus
{
    public string ResearchRoot { get; }
    public string GameRoot => ResolveGameRoot(ResearchRoot);
    public static string ResolveGameRoot(string researchRoot)
    {
        string config = Path.Combine(researchRoot, "game.path.json");
        if (!File.Exists(config)) return Directory.GetParent(researchRoot)!.FullName;
        using var json = JsonDocument.Parse(File.ReadAllBytes(config));
        string path = json.RootElement.GetProperty("GamePath").GetString() ?? throw new InvalidDataException("Missing GamePath.");
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("GamePath must be an absolute path.");
        return Path.GetFullPath(path);
    }
    public List<CorpusRow> Rows { get; } = [];
    public Dictionary<string, TextDocument> Documents { get; } = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Names = new()
    {
        ["Bar"] = "Бар", ["Circus"] = "Цирк", ["Clinic"] = "Клиника", ["Doggy"] = "Догги",
        ["Forest"] = "Пролог", ["Krach"] = "Крах", ["Normann"] = "Норманн", ["Office"] = "Дом Смита", ["Scarlett"] = "Скарлетт"
    };

    public Corpus(string researchRoot)
    {
        ResearchRoot = Path.GetFullPath(researchRoot);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var items = JsonSerializer.Deserialize<List<CorpusItem>>(File.ReadAllBytes(Path.Combine(ResearchRoot, "corpus_inventory.json")), options)
            ?? throw new InvalidDataException("Нет списка файлов корпуса.");
        foreach (var item in items.OrderBy(x => x.File.StartsWith("global/", StringComparison.Ordinal) ? (x.File.Contains("documents", StringComparison.Ordinal) ? 1 : 0) : 2).ThenBy(x => x.File))
        {
            var document = new TextDocument(item.File, File.ReadAllBytes(SafePath(Path.Combine(ResearchRoot, "original"), item.File)));
            Documents.Add(item.File, document);
            var archive = item.File.Split('/')[0];
            var french = ReadCompanion(Path.Combine(ResearchRoot, "original"), item.File.Replace(".en.txt", ".fr.txt", StringComparison.Ordinal));
            var legacy = ReadCompanion(Path.Combine(ResearchRoot, "old_extracted"), item.File);
            var variants = new Dictionary<string, Dictionary<(string, int), string>>();
            foreach (var (code, label) in Languages)
                variants[label] = ReadCompanion(Path.Combine(ResearchRoot, "original"), item.File.Replace(".en.txt", "." + code + ".txt", StringComparison.Ordinal));
            string section = archive == "global" ? (item.File.Contains("documents", StringComparison.Ordinal) ? "Документы" : "Интерфейс") : Names.GetValueOrDefault(archive, archive);
            foreach (var row in document.Records)
                Rows.Add(new(section, archive, item.File, document, row,
                    french.GetValueOrDefault((row.Key, row.Occurrence), ""), legacy.GetValueOrDefault((row.Key, row.Occurrence), ""))
                { Variants = variants.ToDictionary(x => x.Key, x => x.Value.GetValueOrDefault((row.Key, row.Occurrence), "")) });
        }
    }

    public static readonly (string Code, string Label)[] Languages = [("en", "Английский"), ("fr", "Французский"), ("de", "Немецкий"), ("es", "Испанский"), ("it", "Итальянский"), ("pt", "Португальский"), ("pl", "Польский"), ("ja", "Японский")];

    private static Dictionary<(string, int), string> ReadCompanion(string root, string relative)
    {
        var path = SafePath(root, relative);
        return File.Exists(path) ? new TextDocument(relative, File.ReadAllBytes(path)).Records.ToDictionary(x => (x.Key, x.Occurrence), x => x.Value) : [];
    }

    public static string SafePath(string root, string relative)
    {
        var canonical = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(canonical, relative));
        if (!path.StartsWith(canonical, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь выходит за пределы рабочей папки.");
        return path;
    }
}
