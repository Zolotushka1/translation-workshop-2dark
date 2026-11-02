using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Workshop.Core;

public sealed record TextRecord(string Id, string Key, int Occurrence, int LineIndex, string Value);

/// <summary>Lossless UTF-8 text table: retain comments, duplicate keys, BOM and line endings.</summary>
public sealed class TextDocument
{
    public string RelativePath { get; }
    public string SourceHash { get; }
    public IReadOnlyList<TextRecord> Records { get; }
    private readonly List<(string Text, string Ending)> lines = [];
    private readonly bool bom;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public TextDocument(string relativePath, byte[] data)
    {
        RelativePath = relativePath.Replace('\\', '/');
        SourceHash = Convert.ToHexString(SHA256.HashData(data));
        bom = data.AsSpan().StartsWith(Encoding.UTF8.GetPreamble());
        var text = Utf8.GetString(data, bom ? 3 : 0, data.Length - (bom ? 3 : 0));
        int start = 0;
        while (start < text.Length)
        {
            int end = start;
            while (end < text.Length && text[end] is not '\r' and not '\n') end++;
            int next = end;
            if (next < text.Length && text[next] == '\r') next++;
            if (next < text.Length && text[next] == '\n') next++;
            lines.Add((text[start..end], text[end..next]));
            start = next;
        }
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var records = new List<TextRecord>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Text;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('\'') || trimmed.StartsWith('#') || trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
            int separator = line.IndexOf('=');
            if (separator < 1) continue;
            var key = line[..separator];
            int occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            records.Add(new($"{RelativePath}|{key}|{occurrence}", key, occurrence, i, line[(separator + 1)..]));
        }
        Records = records;
    }

    public byte[] Render(IReadOnlyDictionary<string, string> replacements)
    {
        var known = Records.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        if (replacements.Keys.Any(x => !known.Contains(x))) throw new InvalidDataException("Неизвестный идентификатор строки.");
        var byLine = Records.Where(x => replacements.ContainsKey(x.Id)).ToDictionary(x => x.LineIndex);
        var output = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            if (byLine.TryGetValue(i, out var row))
            {
                var value = replacements[row.Id];
                if (value.Contains('\n') || value.Contains('\r')) throw new InvalidDataException($"{row.Key}: используйте {{CR}} вместо физического переноса.");
                output.Append(row.Key).Append('=').Append(value);
            }
            else output.Append(lines[i].Text);
            output.Append(lines[i].Ending);
        }
        var body = Utf8.GetBytes(output.ToString());
        return bom ? Encoding.UTF8.GetPreamble().Concat(body).ToArray() : body;
    }

    public static string Display(string value) => Regex.Replace(value, "\\{CR\\}", "\n", RegexOptions.IgnoreCase);
    public static string ToEngine(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "{CR}");
}

public sealed record Diagnostic(string RecordId, string Severity, string Message);

public static partial class TranslationValidator
{
    [GeneratedRegex("\\{[^{}]+\\}")]
    private static partial Regex Tags();
    [GeneratedRegex("\\{(?:Value(?::[^{}]*)?|Loc:[^{}]+|ICON:[^{}]+|Bitmap:[^{}]+)\\}", RegexOptions.IgnoreCase)]
    private static partial Regex ProtectedTags();

    public static List<Diagnostic> Validate(string id, string original, string target)
    {
        var result = new List<Diagnostic>();
        if (string.IsNullOrEmpty(target)) return result;
        if (target.Contains('\r') || target.Contains('\n')) result.Add(new(id, "Ошибка", "Физический перенос строки: нужен {CR}."));
        if (target.Contains('\0')) result.Add(new(id, "Ошибка", "Нулевой символ в тексте."));
        var plain = Tags().Replace(target, "");
        if (plain.Contains('{') || plain.Contains('}')) result.Add(new(id, "Ошибка", "Незакрытая или вложенная команда в фигурных скобках."));
        var before = Counts(ProtectedTags().Matches(original).Select(x => x.Value));
        var after = Counts(ProtectedTags().Matches(target).Select(x => x.Value));
        foreach (var tag in before.Keys.Union(after.Keys))
            if (before.GetValueOrDefault(tag) != after.GetValueOrDefault(tag))
                result.Add(new(id, "Ошибка", $"Изменена ссылка или переменная {tag}."));
        var sourceFormatting = Counts(Tags().Matches(original).Select(x => x.Value).Where(x => !x.Equals("{CR}", StringComparison.OrdinalIgnoreCase) && !ProtectedTags().IsMatch(x)));
        var targetFormatting = Counts(Tags().Matches(target).Select(x => x.Value).Where(x => !x.Equals("{CR}", StringComparison.OrdinalIgnoreCase) && !ProtectedTags().IsMatch(x)));
        if (sourceFormatting.Keys.Union(targetFormatting.Keys).Any(x => sourceFormatting.GetValueOrDefault(x) != targetFormatting.GetValueOrDefault(x)))
            result.Add(new(id, "Внимание", "Изменены команды оформления. Проверьте результат в игре."));
        if (target == original && Regex.IsMatch(original, "[A-Za-z]{3,}")) result.Add(new(id, "Внимание", "Текст совпадает с английским оригиналом."));
        return result;
    }

    private static Dictionary<string, int> Counts(IEnumerable<string> values) => values.GroupBy(x => x, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
}
