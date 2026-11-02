using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workshop.Core;

string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("..");
string output = Path.Combine(root, "checks");
Directory.CreateDirectory(output);
var passed = new List<string>();
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); passed.Add(name); Console.WriteLine("PASS " + name); }
void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { passed.Add(name); Console.WriteLine("PASS " + name); return; } throw new InvalidOperationException("Should reject: " + name); }

var corpus = new Corpus(root);
Check(SceneCheckpoint.SameLevel("monkey://data/levels/Office/Office_map.tmx", "levels/Office/Office_map.tmx"), "Recognize both native prefixes for the same level");
Check(!SceneCheckpoint.SameLevel("levels/Office/Office_map.tmx", "levels/Forest/Forest_map.tmx"), "Do not mix different level snapshots");
Check(SceneCheckpoint.LevelLaunch("monkey://data/levels/Office/Office_map.tmx") == ("levels/Office/Office_map.tmx", "Office", "monkey://data/levels/Office"), "Give the native loader a directory instead of duplicating the map filename");
Reject(() => SceneCheckpoint.LevelLaunch("levels/../bad.tmx"), "Reject unknown snapshot level paths before native loading");
string settingsPath = Path.Combine(output, "settings-roundtrip.json");
WorkshopSettings.Save(settingsPath, new() { LabelColor = "#20C0FF", LabelPosition = "Снизу справа", LabelOpacity = .35, AudioSource = "RU" });
var settings = WorkshopSettings.Load(settingsPath);
Check(settings.LabelColor == "#20C0FF" && settings.LabelOpacity == .35 && settings.AudioSource == "RU", "Preserve fullscreen label and audio settings");
Check(settings.LabelOffset(1920, 1080, 100, 30) == (1800d, 1030d), "Place bottom-right fullscreen label inside the screen margin");
Check(new WorkshopSettings { LabelPosition = "По центру" }.LabelOffset(1920, 1080, 100, 30) == (910d, 525d), "Center the label on both axes");
Reject(() => new WorkshopSettings { LabelColor = "bad color" }.Validate(), "Reject invalid overlay colors");
Reject(() => new WorkshopSettings { SwitchVersionKey = "F11" }.Validate(), "Reject colliding fullscreen shortcuts");
Check(new WorkshopSettings().FullscreenHint(false) == "F11 to quit" && new WorkshopSettings().FullscreenHint(true) == "F11 — выход", "Localize fullscreen exit hints");
Check(new WorkshopSettings { FullscreenKey = "F10" }.FullscreenHint(false) == "F10 to quit", "Use configured shortcut in the hint");
Check(SyncTimeline.Milliseconds(100, 60) == 1100 && SyncTimeline.Milliseconds(100, 216000) == 3600100, "Shared 60 Hz time has no accumulated rounding drift");
Check(SyncTimeline.Milliseconds(uint.MaxValue - 10, 1) == 5, "Virtual game clock wraps like the native clock");
var embeddedPointer = ViewportPointer.Pixels(.2, .7, 540, 228);
var normalizedPointer = ViewportPointer.Normalize(embeddedPointer.X, embeddedPointer.Y, 540, 228);
var fullscreenPointer = ViewportPointer.Pixels(normalizedPointer.X, normalizedPointer.Y, 1920, 1080);
Check(Math.Abs(fullscreenPointer.X - 384) < .001 && Math.Abs(fullscreenPointer.Y - 756) < .001, "Match mouse positions across letterboxed editor and fullscreen canvases");
Check(ViewportPointer.Pixels(0, 0, 1000, 1000) == (0f, 218.75f), "Account for vertical letterboxing on a square viewport");
var menuFont = File.ReadAllBytes(Path.Combine(root, "old_extracted", "global", "fonts", "2Dark_menu_full_48.txt"));
string expandedMenuFont = Encoding.UTF8.GetString(BuildService.ExpandMenuCyrillic(menuFont));
Check(Enumerable.Range(1072, 32).Append(1105).All(x => expandedMenuFont.Contains(";" + x + ",", StringComparison.Ordinal)), "Give every lowercase Cyrillic menu letter a visible PG glyph");
string profileTest = Path.Combine(output, "profile-test");
string profileRoot = GameProfile.Prepare(profileTest);
string stateFile = Path.Combine(profileRoot, "Gloomywood", "2Dark", ".monkeystate");
Check(File.ReadAllText(stateFile).Contains("I:FULLSCREEN=0", StringComparison.Ordinal), "Create independent windowed test profile");
var profileBytes = File.ReadAllBytes(stateFile);
GameProfile.Prepare(profileTest);
Check(profileBytes.SequenceEqual(File.ReadAllBytes(stateFile)), "Profile preparation is idempotent");
File.WriteAllText(stateFile, "DARKDATA\t2\tconfigs/systemconfig\t1\t[CONFIG]\nI:FULLSCREEN=1\nS:CurrentPlayerID=test-id\n\tplayers/test-id/config\t1\t[CONFIG]\nS:Name=Keep me\n");
GameProfile.Prepare(profileTest);
Check(File.ReadAllText(stateFile).Contains("S:Name=Keep me", StringComparison.Ordinal) && !File.ReadAllText(stateFile).Contains("I:FULLSCREEN=1", StringComparison.Ordinal), "Change fullscreen without losing test player data");
var exeBytes = File.ReadAllBytes(Path.Combine(corpus.GameRoot, "2Dark.exe"));
var exeEn = RuntimeExecutable.ForRole(exeBytes, "english");
var exeRu = RuntimeExecutable.ForRole(exeBytes, "russian");
Check(exeEn.Length == exeBytes.Length && exeRu.Length == exeBytes.Length && !exeEn.SequenceEqual(exeRu), "Independent mutex names preserve test EXE size");
Check(Enumerable.Range(0, exeBytes.Length).Count(i => exeBytes[i] != exeRu[i]) <= 32, "Only known mutex and GLFW focus query change in test EXE");
int focusOffset = exeBytes.AsSpan().IndexOf(Convert.FromHexString("568BF1FF1524B36F0033D23986340200005E0F94C28BC2C3"));
Check(focusOffset >= 0 && exeRu.AsSpan(focusOffset, 6).SequenceEqual(new byte[] { 0xB8, 1, 0, 0, 0, 0xC3 }), "Keep embedded test viewports rendering while editing");
Reject(() => RuntimeExecutable.ForRole(new byte[100], "english"), "Reject unsupported EXE instead of guessing patch location");
Check(corpus.Rows.Count == 2834 && corpus.Documents.Count == 11, "Load all 2834 occurrences across 11 documents");
Check(corpus.Rows.All(x => x.Variants.Count == 8) && corpus.Rows.First(x => x.Record.Key == "MAIN_Adventure").Variants["Французский"] == "AVENTURE", "Load all eight reference languages without merging records");
string checkpointPath = Path.Combine(output, "roundtrip.scene.json");
SceneCheckpoint.Save(checkpointPath, new SceneCheckpoint { Title = "Сцена Ё", English = "native state EN", Russian = "native state RU" });
Check(SceneCheckpoint.Load(checkpointPath).Russian == "native state RU" && SceneCheckpoint.Load(checkpointPath).Title == "Сцена Ё", "Round-trip independent scene snapshots with Unicode metadata");
File.WriteAllText(Path.Combine(output, "invalid.scene.json"), "{\"Version\":2,\"English\":\"x\"}");
Reject(() => SceneCheckpoint.Load(Path.Combine(output, "invalid.scene.json")), "Reject incompatible scene snapshot versions");
var nativeState = new byte[128]; BitConverter.GetBytes(14).CopyTo(nativeState, 0); nativeState[80] = 213;
Check(NativeSceneState.Decode(NativeSceneState.Encode(nativeState)).SequenceEqual(nativeState), "Preserve every native scene byte in checksummed snapshots");
string damagedNative = NativeSceneState.Encode(nativeState).Replace("2DSTATE1:", "2DSTATE1:0", StringComparison.Ordinal);
Reject(() => NativeSceneState.Decode(damagedNative), "Reject scene corruption before touching a game process");
Reject(() => NativeSceneState.Decode("players/test/savestates/adventure/SmokeSave"), "Reject old slot paths as scene snapshots");
foreach (var doc in corpus.Documents.Values)
    Check(doc.Render(new Dictionary<string, string>()).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "original", doc.RelativePath))), "Lossless no-op: " + doc.RelativePath);
var duplicate = corpus.Rows.Where(x => x.Record.Key == "Kim_Disguised_NoTatami").ToList();
Check(duplicate.Count == 2 && duplicate[0].Record.Id != duplicate[1].Record.Id, "Retain duplicate keys as separate occurrences");
var sample = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("'comment=stay\r\nA=x=y\r\nA=two\nEMPTY=\r\n")).ToArray();
var table = new TextDocument("sample", sample);
Check(table.Render(new Dictionary<string, string>()).SequenceEqual(sample), "Preserve BOM, mixed line endings, comments and equals in values");
var translated = table.Render(new Dictionary<string, string> { [table.Records[1].Id] = "Ёжик{CR}готов" });
Check(new TextDocument("sample", translated).Records[0].Value == "x=y" && new TextDocument("sample", translated).Records[1].Value == "Ёжик{CR}готов", "Replace only selected duplicate occurrence");
Reject(() => table.Render(new Dictionary<string, string> { [table.Records[0].Id] = "bad\nphysical" }), "Reject physical newlines");
Reject(() => table.Render(new Dictionary<string, string> { ["unknown"] = "x" }), "Reject unknown record IDs");
Reject(() => Corpus.SafePath(root, "../escape.txt"), "Reject paths outside workspace");
Check(TranslationValidator.Validate("x", "Save {Value} {ICON:A}", "Сохранить {Value} {ICON:A}").Count == 0, "Preserve protected tags");
Check(TranslationValidator.Validate("x", "Save {Value}", "Сохранить").Any(x => x.Severity == "Ошибка"), "Detect lost variable");
Check(TranslationValidator.Validate("x", "Hi", "Привет {Loc:unexpected}").Any(x => x.Severity == "Ошибка"), "Detect added reference");
Check(TranslationValidator.Validate("x", "Hi", "Привет {CR").Any(x => x.Severity == "Ошибка"), "Detect broken command");
Check(TranslationValidator.Validate("x", "A{CR}B", "А Б").Count == 0, "Allow intentional changes to line breaks");

var project = new TranslationProject { UseLegacyFonts = true };
foreach (var (key, value) in new[] { ("MAIN_Help", "ПОМОЩЬ"), ("MAIN_Adventure", "НАЧАТЬ ИГРУ"), ("TEXT_Scarlett_map", "Карта: запретные зоны отмечены красным."), ("Welcome1-3", "7 марта 1976 года") })
{
    var row = corpus.Rows.First(x => x.Record.Key == key);
    project.Edits[row.Record.Id] = new() { Target = value, Note = "check", SourceHash = row.Document.SourceHash };
}
var officeRow = corpus.Rows.First(x => x.Archive == "Office" && x.Record.Value.Length > 5 && !x.Record.Value.Contains('{'));
project.Edits[officeRow.Record.Id] = new() { Target = "Проверка офиса", SourceHash = officeRow.Document.SourceHash };
string projectPath = Path.Combine(output, "roundtrip.2dark.json");
ProjectStore.Save(projectPath, project);
Check(ProjectStore.Load(projectPath).Edits.Count == 5, "Save and reload Unicode project");
ProjectStore.Save(projectPath, project);
Check(File.Exists(projectPath + ".bak"), "Create backup on subsequent save");
string badPath = Path.Combine(output, "corrupt.json"); File.WriteAllText(badPath, "{bad");
try { ProjectStore.Load(badPath); throw new InvalidOperationException("Corrupt project accepted"); } catch (JsonException) { passed.Add("Reject corrupt project without overwriting"); }

var archivePaths = Directory.GetFiles(Path.Combine(corpus.GameRoot, "data"), "*.bigfile", SearchOption.AllDirectories);
var hashes = archivePaths.ToDictionary(x => x, x => SHA256.HashData(File.ReadAllBytes(x)));
foreach (string path in archivePaths)
{
    var bytes = File.ReadAllBytes(path);
    Check(BigFile.Replace(bytes, new Dictionary<string, byte[]>()).SequenceEqual(bytes), "Archive no-op preserves every byte: " + Path.GetFileName(path));
}
var builder = new BuildService(corpus);
var timer = Stopwatch.StartNew();
var built = builder.Build(project, new Progress<string>(Console.WriteLine));
Console.WriteLine($"Build: {built.Seconds:F3}s, {built.Archives} archives, {built.TextResources} text resources");
Check(built.TextResources == 6, "Propagate Office translation into Office, Office2 and Office3");
string extracted = Path.Combine(output, "extracted"); Directory.CreateDirectory(extracted);
var tool = new ProcessStartInfo(Path.Combine(root, "tools", "quickbms", "quickbms.exe")) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
foreach (string arg in new[] { "-R", "-q", "-o", "-f", "{}texts{}.txt;fonts/{}.txt", Path.Combine(root, "sources", "2dark_extended.bms"), Path.Combine(built.Directory, "data", "global.bigfile"), extracted }) tool.ArgumentList.Add(arg);
using (var process = Process.Start(tool)!)
{
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    File.WriteAllText(Path.Combine(output, "quickbms-extract.log"), await stdout + await stderr);
    Check(process.ExitCode == 0, "Independent QuickBMS extraction succeeds");
}
foreach (var doc in corpus.Documents.Values.Where(x => x.RelativePath.StartsWith("global/", StringComparison.Ordinal)))
{
    var expected = doc.Render(doc.Records.Where(x => project.Edits.ContainsKey(x.Id)).ToDictionary(x => x.Id, x => project.Edits[x.Id].Target));
    Check(expected.SequenceEqual(File.ReadAllBytes(Path.Combine(extracted, doc.RelativePath[7..]))), "QuickBMS confirms rebuilt UTF-8 bytes: " + doc.RelativePath);
}
Check(hashes.All(x => x.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(x.Key)))), "All source archives unchanged after build");
var originalGlobal = File.ReadAllBytes(Path.Combine(corpus.GameRoot, "data", "global.bigfile"));
var patchedGlobal = File.ReadAllBytes(Path.Combine(built.Directory, "data", "global.bigfile"));
var beforeIndex = BigFile.Index(originalGlobal).ToDictionary(x => x.Name);
int untouched = 0;
foreach (var entry in BigFile.Index(patchedGlobal).Where(x => !x.Name.StartsWith("texts/", StringComparison.Ordinal) && !x.Name.StartsWith("fonts/", StringComparison.Ordinal)))
{
    var old = beforeIndex[entry.Name];
    if (!originalGlobal.AsSpan(old.PayloadOffset, old.PackedSize).SequenceEqual(patchedGlobal.AsSpan(entry.PayloadOffset, entry.PackedSize))) throw new InvalidDataException("Changed untouched block: " + entry.Name);
    untouched++;
}
Check(untouched > 400, "Preserve all untouched compressed blocks byte-for-byte");
var invalid = new TranslationProject();
var variable = corpus.Rows.First(x => x.Record.Value.Contains("{Value}", StringComparison.Ordinal));
invalid.Edits[variable.Record.Id] = new() { Target = "Потеряна переменная", SourceHash = variable.Document.SourceHash };
Reject(() => builder.Build(invalid), "Block structurally invalid translation before modifying runtime");
Check(patchedGlobal.SequenceEqual(File.ReadAllBytes(Path.Combine(built.Directory, "data", "global.bigfile"))), "Failed validation leaves previous test build intact");
builder.Build(new TranslationProject { UseLegacyFonts = false });
bool restored = archivePaths.All(p =>
{
    string target = Path.Combine(builder.RussianRoot, Path.GetRelativePath(corpus.GameRoot, p));
    return File.ReadAllBytes(p).SequenceEqual(File.ReadAllBytes(target));
});
Check(restored, "Empty project restores every archive to original bytes");
builder.Build(project);
File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = passed.Count, checks = passed, buildSeconds = built.Seconds, untouched, runtimeTested = false }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"SUCCESS: {passed.Count} checks");
