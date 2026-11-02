using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Workshop.Core;

public sealed record BuildResult(string Directory, int Archives, int TextResources, double Seconds);

public sealed class BuildService(Corpus corpus)
{
    public string EnglishRoot => Path.Combine(corpus.ResearchRoot, "runtime", "english");
    public string RussianRoot => Path.Combine(corpus.ResearchRoot, "runtime", "russian");

    public string PrepareEnglish(IProgress<string>? progress = null)
    {
        VerifyOriginals();
        PrepareCopy(EnglishRoot, progress);
        return EnglishRoot;
    }

    public BuildResult Build(TranslationProject project, IProgress<string>? progress = null)
    {
        var watch = Stopwatch.StartNew();
        foreach (var process in Process.GetProcessesByName("2Dark"))
        {
            try
            {
                if (!process.HasExited && process.MainModule?.FileName?.Equals(Path.Combine(RussianRoot, "2Dark.exe"), StringComparison.OrdinalIgnoreCase) == true && !process.WaitForExit(100))
                    throw new InvalidDataException("Перед сборкой остановите русскую тестовую копию игры.");
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }
        VerifyOriginals();
        foreach (var row in corpus.Rows)
        {
            if (!project.Edits.TryGetValue(row.Record.Id, out var edit) || edit.Target.Length == 0) continue;
            if (edit.SourceHash != row.Document.SourceHash) throw new InvalidDataException($"Исходник {row.File} изменился. Проверьте проект перед сборкой.");
            var errors = TranslationValidator.Validate(row.Record.Id, row.Record.Value, edit.Target).Where(x => x.Severity == "Ошибка").ToList();
            if (errors.Count != 0) throw new InvalidDataException($"{row.Record.Key}: {errors[0].Message}");
        }
        var known = corpus.Rows.Select(x => x.Record.Id).ToHashSet(StringComparer.Ordinal);
        if (project.Edits.Keys.Any(x => !known.Contains(x))) throw new InvalidDataException("В проекте есть строки, отсутствующие в текущем корпусе.");
        PrepareCopy(RussianRoot, progress);
        int changedArchives = 0, resources = 0;
        foreach (var archivePath in Directory.EnumerateFiles(Path.Combine(corpus.GameRoot, "data"), "*.bigfile", SearchOption.AllDirectories))
        {
            string archive = Path.GetFileNameWithoutExtension(archivePath);
            string canonical = archive is "Office2" or "Office3" ? "Office" : archive;
            var patches = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var document in corpus.Documents.Values.Where(x => x.RelativePath.StartsWith(canonical + "/", StringComparison.Ordinal)))
            {
                var edits = document.Records.Where(x => project.Edits.TryGetValue(x.Id, out var edit) && edit.Target.Length > 0 && edit.Target != x.Value)
                    .ToDictionary(x => x.Id, x => project.Edits[x.Id].Target, StringComparer.Ordinal);
                if (edits.Count > 0) patches.Add(document.RelativePath[(canonical.Length + 1)..], document.Render(edits));
            }
            if (archive == "global" && project.UseLegacyFonts)
            {
                foreach (string name in FontNames)
                {
                    var bytes = File.ReadAllBytes(Path.Combine(corpus.ResearchRoot, "old_extracted", "global", "fonts", name + ".txt"));
                    patches.Add("fonts/" + name + ".txt", name == "2Dark_menu_full_48" ? ExpandMenuCyrillic(bytes) : bytes);
                }
            }
            progress?.Report($"Сборка {archive}…");
            var source = File.ReadAllBytes(archivePath);
            var output = patches.Count > 0 ? BigFile.Replace(source, patches) : source;
            string destination = Corpus.SafePath(RussianRoot, Path.GetRelativePath(corpus.GameRoot, archivePath));
            WriteAtomic(destination, output);
            if (patches.Count > 0) changedArchives++;
            resources += patches.Count(x => !x.Key.StartsWith("fonts/", StringComparison.Ordinal));
        }
        foreach (var (name, cabinetName) in FontPngs)
        {
            string source = project.UseLegacyFonts
                ? Path.Combine(corpus.ResearchRoot, "old_payload", cabinetName)
                : Path.Combine(corpus.GameRoot, "data", "fonts", name);
            WriteAtomic(Path.Combine(RussianRoot, "data", "fonts", name), File.ReadAllBytes(source));
        }
        watch.Stop();
        return new(RussianRoot, changedArchives, resources, watch.Elapsed.TotalSeconds);
    }

    public bool LegacyFontsInstalled => FontPngs.All(x => File.Exists(Path.Combine(RussianRoot, "data", "fonts", x.Name)) &&
        File.ReadAllBytes(Path.Combine(RussianRoot, "data", "fonts", x.Name)).SequenceEqual(File.ReadAllBytes(Path.Combine(corpus.ResearchRoot, "old_payload", x.CabinetName))));

    public static byte[] ExpandMenuCyrillic(byte[] bytes)
    {
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        var entries = text.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(x => x.Split(',', 2)).Where(x => x.Length == 2 && int.TryParse(x[0], out _)).ToDictionary(x => int.Parse(x[0]), x => x[1]);
        // The PG menu atlas contains uppercase Cyrillic only. Reuse those glyphs for
        // lowercase code points instead of silently losing every lowercase letter.
        foreach (int code in Enumerable.Range(1072, 32).Append(1105))
            if (!entries.ContainsKey(code) && entries.TryGetValue(char.ToUpperInvariant((char)code), out string? glyph)) text = text.TrimEnd() + code + "," + glyph + ";";
        return System.Text.Encoding.UTF8.GetBytes(text);
    }

    private static readonly string[] FontNames = ["2dark_f25_executive_22", "2dark_font_35_border_shadow", "2Dark_menu_full_48"];
    private static readonly (string Name, string CabinetName)[] FontPngs =
    [
        ("2dark_f25_executive_22_P_1.png", "dark_f25_executive_22_P_1.png"),
        ("2dark_font_35_border_shadow_P_1.png", "dark_font_35_border_shadow_P_1.png"),
        ("2Dark_menu_full_48_P_1.png", "Dark_menu_full_48_P_1.png")
    ];

    private void VerifyOriginals()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(corpus.ResearchRoot, "original_manifest.json")));
        foreach (var item in manifest.RootElement.EnumerateArray())
        {
            string relative = item.GetProperty("path").GetString()!;
            if (!relative.EndsWith(".bigfile", StringComparison.Ordinal) && relative != "2Dark.exe") continue;
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Corpus.SafePath(corpus.GameRoot, relative))));
            if (!actual.Equals(item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{relative} отличается от исследованного оригинала. Обновите исходные данные проекта.");
        }
    }

    private void PrepareCopy(string destination, IProgress<string>? progress)
    {
        string runtime = Path.GetFullPath(Path.Combine(corpus.ResearchRoot, "runtime")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(destination).StartsWith(runtime, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Тестовая копия должна быть в runtime.");
        Directory.CreateDirectory(destination);
        var files = Directory.EnumerateFiles(Path.Combine(corpus.GameRoot, "data"), "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(corpus.GameRoot).Where(x => Path.GetExtension(x) is ".exe" or ".dll")).ToList();
        int copied = 0;
        foreach (var source in files)
        {
            string target = Corpus.SafePath(destination, Path.GetRelativePath(corpus.GameRoot, source));
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            if (++copied % 100 == 0) progress?.Report($"Подготовка тестовой копии: {copied} файлов…");
        }
        // Steam remains responsible for ownership/authentication. This identifies the installed game when launched from its test copy.
        File.WriteAllText(Path.Combine(destination, "steam_appid.txt"), "435100");
        var executable = RuntimeExecutable.ForRole(File.ReadAllBytes(Path.Combine(corpus.GameRoot, "2Dark.exe")), Path.GetFileName(destination));
        string exeTarget = Path.Combine(destination, "2Dark.exe");
        if (!File.ReadAllBytes(exeTarget).SequenceEqual(executable)) WriteAtomic(exeTarget, executable);
    }

    public static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".workshop-tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, path, true);
    }
}
