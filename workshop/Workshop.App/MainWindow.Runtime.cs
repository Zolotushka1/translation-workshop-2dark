using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Workshop.Core;

namespace Workshop.App;

public partial class MainWindow
{
    private LinkedInput linkedInput = null!;
    private readonly DispatcherTimer liveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool linked = true, liveTranslation = true;
    private volatile bool applyingLive;
    private readonly Dictionary<string, HashSet<string>> liveHistory = [];
    private readonly HashSet<string> liveTouched = [];
    private void TrackLiveBaseline(RowViewModel row)
    {
        if (row.HasTranslation) liveTouched.Add(row.Id);
        if (!liveHistory.TryGetValue(row.Id, out var values)) liveHistory[row.Id] = values = [];
        foreach (string value in new[] { row.Original, row.Target, row.Source.Legacy }) if (value.Length > 0) values.Add(value);
    }
    private string referenceLanguage = "PG Team · старый перевод";
    public string[] ReferenceLanguages { get; } = new[] { "PG Team · старый перевод" }.Concat(Corpus.Languages.Select(x => x.Label)).ToArray();
    public string ReferenceLanguage { get => referenceLanguage; set { referenceLanguage = value; Notify(); Notify(nameof(ReferenceText)); } }
    public string ReferenceText => Selected is null ? "" : TextDocument.Display(ReferenceLanguage.StartsWith("PG Team", StringComparison.Ordinal)
        ? Selected.Source.Legacy.Length > 0 ? Selected.Source.Legacy : "Для этой записи старого перевода нет."
        : Selected.Source.Variants.GetValueOrDefault(ReferenceLanguage, "Текст этого языка отсутствует."));
    public bool Linked { get => linked; set { if (linked == value) return; linked = value; if (linkedInput is not null) { linkedInput.Reset(); linkedInput.Enabled = value; } Notify(); ChangeLinkedModeAsync(); } }
    public bool LiveTranslation { get => liveTranslation; set { liveTranslation = value; Notify(); } }
    public string PauseEnglishCaption => english.Paused ? "Продолжить EN" : "Пауза EN";
    public string PauseRussianCaption => russian.Paused ? "Продолжить RU" : "Пауза RU";
    public string PauseBothCaption => english.Paused || russian.Paused ? "Продолжить обе" : "Пауза обеих";
    public ObservableCollection<CheckpointChoice> Checkpoints { get; } = [];
    public CheckpointChoice? SelectedCheckpoint { get; set; }
    public sealed record CheckpointChoice(string Path, string Title) { public override string ToString() => Title; }

    private void InitializeRuntimeTools()
    {
        linkedInput = new(EnglishHost, RussianHost, () => !busy && english.Running && russian.Running && !english.Paused && !russian.Paused) { Enabled = linked };
        // Low-level input hooks must return immediately. Native I/O is drained separately.
        linkedInput.InputChanged = (keys, x, y, release) => pendingInput = (keys, x, y, release);
        InitializeSettings();
        RefreshCheckpoints();
        liveTimer.Tick += async (_, _) =>
        {
            if (busy || applyingLive || !LiveTranslation || !russian.Running || russian.Paused) return;
            try
            {
                await ApplyLiveAsync(false);
            }
            catch (Exception ex) { StatusText = "Перевод на лету: " + ex.Message; }
        };
        liveTimer.Start();
    }

    private void NotifyRuntime()
    {
        foreach (string property in new[] { nameof(PauseEnglishCaption), nameof(PauseRussianCaption), nameof(PauseBothCaption) }) Notify(property);
        UpdateAudio();
    }
    private async void LaunchLinked(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        Persist(); linkedInput.Reset();
        await StopSynchronizationAsync();
        RestoreAudio();
        await Task.WhenAll(english.StopAsync(), russian.StopAsync());

        StatusText = "Подготовка двух копий…";
        var snapshot = Snapshot();
        await Task.Run(() => { builder.PrepareEnglish(); builder.Build(snapshot); });
        // Start from the same persistent game profile. Keep an automatic backup of RU first.
        string enProfile = GameProfile.Prepare(builder.EnglishRoot);
        string ruProfile = GameProfile.Prepare(builder.RussianRoot);
        string ruState = Path.Combine(ruProfile, "Gloomywood", "2Dark", ".monkeystate");
        if (File.Exists(ruState))
        {
            string backup = Path.Combine(corpus.ResearchRoot, "checkpoints", "profiles", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-ru.monkeystate");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!); File.Copy(ruState, backup);
        }
        BuildService.WriteAtomic(ruState, File.ReadAllBytes(Path.Combine(enProfile, "Gloomywood", "2Dark", ".monkeystate")));
        EnglishHost.Visibility = RussianHost.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await Task.WhenAll(english.StartAsync(builder.EnglishRoot), russian.StartAsync(builder.RussianRoot));
        Linked = true;
        await InitializeLinkedControlsAsync();
        await ApplyLiveAsync(false); NotifyRuntime();
        StatusText = "Обе копии запущены. Ввод, игровое время и генератор случайных событий получают общий шаг.";
    });

    private async void StopBoth(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        fullscreen?.Close();
        await StopSynchronizationAsync();
        RestoreAudio();
        linkedInput.Reset(); await Task.WhenAll(english.StopAsync(), russian.StopAsync());
        EnglishHost.Visibility = RussianHost.Visibility = Visibility.Collapsed;
        NotifyRuntime(); StatusText = "Обе тестовые копии выключены.";
    });
    private async void AttachRussian(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        if (!File.Exists(Path.Combine(builder.RussianRoot, "2Dark.exe"))) throw new InvalidOperationException("Сначала создайте RU-сборку кнопкой F5.");
        RussianHost.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await russian.StartAsync(builder.RussianRoot); await InitializeLinkedControlsAsync(); await ApplyLiveAsync(false); NotifyRuntime();
        StatusText = "RU подключена. Запущенный ранее процесс и текущая сцена сохранены.";
    });
    private void TogglePause(GameSession session)
    {
        if (!session.Running) throw new InvalidOperationException("Сначала запустите эту копию игры.");
        linkedInput.Reset();
        bool pause = !session.Paused;
        if (frameSync is { Active: true }) { english.SetPaused(pause); russian.SetPaused(pause); }
        else session.SetPaused(pause);
        NotifyRuntime(); StatusText = session.Paused ? "Игра приостановлена. Состояние процесса сохранено." : "Игра продолжена.";
    }
    private async void PauseEnglish(object sender, RoutedEventArgs e) => await ExecuteAsync(() => { TogglePause(english); return Task.CompletedTask; });
    private async void PauseRussian(object sender, RoutedEventArgs e) => await ExecuteAsync(() => { TogglePause(russian); return Task.CompletedTask; });
    private async void PauseBoth(object sender, RoutedEventArgs e) => await ExecuteAsync(() =>
    {
        if (!english.Running && !russian.Running) throw new InvalidOperationException("Сначала запустите тестовую копию игры.");
        bool pause = !english.Paused && !russian.Paused;
        linkedInput.Reset();
        foreach (var session in new[] { english, russian }.Where(x => x.Running)) session.SetPaused(pause);
        NotifyRuntime(); StatusText = pause ? "Обе игры на паузе." : "Обе игры продолжены.";
        return Task.CompletedTask;
    });
    private async Task SendEscapeAsync(GameSession session, GameHost host) => await ExecuteAsync(async () =>
    {
        linkedInput.Reset(); pendingInput = null; session.SetPaused(false);
        host.FocusGame();
        if (frameSync is { Active: true } sync)
        { english.SetPaused(false); russian.SetPaused(false); await sync.PulseKeyAsync(27); }
        else await session.Control.PressKeyAsync(27);
        NotifyRuntime();
        StatusText = "Escape отправлен в игру. Для остановки в любом экране используйте кнопку «Пауза».";
    });

    private async void ApplyLive(object sender, RoutedEventArgs e) => await ExecuteAsync(() => ApplyLiveAsync(true));
    private async Task ApplyLiveAsync(bool report)
    {
        if (applyingLive || !russian.Running) { if (report) StatusText = "Сначала запустите RU-копию."; return; }
        if (Rows.Any(x => x.HasErrors)) { if (report) StatusText = "Исправьте ошибки команд и переменных перед применением."; return; }
        applyingLive = true;
        try
        {
            var control = russian.Control;
            string level = SceneCheckpoint.NormalizeLevel(control.CurrentLevel);
            string archive = level.Split('/').ElementAtOrDefault(1) ?? "";
            if (archive is "Office2" or "Office3") archive = "Office";
            // The engine uses the final occurrence, with the current level overriding global keys.
            var changes = Rows.Where(x => x.Source.Archive == "global" || x.Source.Archive == archive)
                .GroupBy(x => x.Key).Select(g => g.OrderBy(x => x.Source.Archive != "global").Last())
                .Where(x => liveTouched.Contains(x.Id))
                .Select(x => new LiveTextChange(x.Key, x.HasTranslation ? x.Target : x.Original, liveHistory[x.Id].ToArray())).ToArray();
            if (frameSync is { } sync) await sync.HoldAsync();
            int count = await Task.Run(() => control.ApplyTranslations(changes));
            if (count > 0 || report) await control.RefreshUiAsync();
            if (report || count > 0) StatusText = $"На лету обновлено строк: {count}. Экран игры обновлён.";
        }
        catch (Exception ex) { StatusText = "Перевод на лету: " + ex.Message; if (report) throw; }
        finally { applyingLive = false; }
    }

    private string CheckpointRoot => Path.Combine(corpus.ResearchRoot, "checkpoints", "scenes");
    private void RefreshCheckpoints()
    {
        Directory.CreateDirectory(CheckpointRoot);
        Checkpoints.Clear();
        foreach (string path in Directory.EnumerateFiles(CheckpointRoot, "*.scene.json").OrderDescending())
        {
            try { Checkpoints.Add(new(path, SceneCheckpoint.Load(path).Title)); }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException) { }
        }
        SelectedCheckpoint = Checkpoints.FirstOrDefault(); Notify(nameof(SelectedCheckpoint));
    }
    private async void SaveScene(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        linkedInput.Reset();
        if (!english.Running && !russian.Running) throw new InvalidOperationException("Сначала запустите игру и откройте уровень.");
        var scene = new SceneCheckpoint { Title = $"{DateTime.Now:dd.MM HH:mm:ss} · {Selected?.Key ?? "сцена"}" };
        foreach (var session in new[] { english, russian }.Where(x => x.Running && x.Control.HasLevel)) session.Control.CheckSceneReady();
        if (english.Running && english.Control.HasLevel) { scene.EnglishLevel = english.Control.CurrentLevel; scene.English = await english.Control.SaveStateAsync(); }
        if (russian.Running && russian.Control.HasLevel) { scene.RussianLevel = russian.Control.CurrentLevel; scene.Russian = await russian.Control.SaveStateAsync(); }
        if (scene.English is null && scene.Russian is null) throw new InvalidOperationException("Для сохранения сцены откройте игровой уровень. Главное меню не содержит состояния прохождения.");
        string path = Path.Combine(CheckpointRoot, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.scene.json");
        SceneCheckpoint.Save(path, scene); RefreshCheckpoints();
        StatusText = "Состояние уровня сохранено: " + scene.Title;
    });
    private async void LoadScene(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        if (SelectedCheckpoint is null) throw new InvalidOperationException("Выберите сохранённую сцену.");
        var scene = SceneCheckpoint.Load(SelectedCheckpoint.Path);
        linkedInput.Reset();
        var targets = new List<(GameSession Session, string State, string Level)>();
        if (Linked)
        {
            string state = scene.English ?? scene.Russian ?? throw new InvalidDataException("Снимок не содержит состояния игры.");
            string level = (scene.English is not null ? scene.EnglishLevel : scene.RussianLevel) ?? "";
            targets.Add((english, state, level)); targets.Add((russian, state, level));
        }
        else
        {
            if (scene.English is not null) targets.Add((english, scene.English, scene.EnglishLevel ?? ""));
            if (scene.Russian is not null) targets.Add((russian, scene.Russian, scene.RussianLevel ?? ""));
        }
        foreach (var target in targets) { NativeSceneState.Decode(target.State); SceneCheckpoint.LevelLaunch(target.Level); }
        await StopSynchronizationAsync();
        StatusText = "Подготовка сохранённой сцены…";
        Persist(); var snapshot = Snapshot();
        await Task.WhenAll(targets.Where(t => !t.Session.Running && !t.Session.HasExistingProcess).Select(t => Task.Run(() =>
        { if (t.Session == english) builder.PrepareEnglish(); else builder.Build(snapshot); })));
        if (targets.Any(t => t.Session == english)) EnglishHost.Visibility = Visibility.Visible;
        if (targets.Any(t => t.Session == russian)) RussianHost.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await Task.WhenAll(targets.Where(t => !t.Session.Running).Select(t => t.Session.StartAsync(t.Session == english ? builder.EnglishRoot : builder.RussianRoot)));
        try
        {
            // Both startup screens must be ready before either level request is issued.
            await Task.WhenAll(targets.Select(t => t.Session.Control.WaitForStartupAsync()));
            StatusText = "Совместная загрузка уровня…";
            await Task.WhenAll(targets.Select(t => t.Session.Control.OpenLevelAsync(t.Level, true)));
            await Task.WhenAll(targets.Select(t => t.Session.Control.LoadStateAsync(t.State, t.Level)));
            await ApplyLiveAsync(false);
            if (Linked)
            {
                await InitializeLinkedControlsAsync(false);
                english.SetPaused(false); russian.SetPaused(false);
            }
            else foreach (var t in targets) t.Session.SetPaused(false);
        }
        catch { foreach (var t in targets.Where(t => t.Session.Running)) t.Session.SetPaused(true); throw; }
        NotifyRuntime(); StatusText = "Сцена загружена одновременно в обе копии: " + scene.Title;
    });
    private async void AlignScene(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        if (!english.Running || !russian.Running) throw new InvalidOperationException("Запустите обе копии и откройте один и тот же уровень.");
        if (!english.Control.HasLevel || !russian.Control.HasLevel || !SceneCheckpoint.SameLevel(english.Control.CurrentLevel, russian.Control.CurrentLevel))
            throw new InvalidOperationException("Для выравнивания откройте один и тот же уровень в EN и RU.");
        linkedInput.Reset();
        english.Control.CheckSceneReady(); russian.Control.CheckSceneReady();
        string state = await english.Control.SaveStateAsync();
        await StopSynchronizationAsync();
        await russian.Control.LoadStateAsync(state, english.Control.CurrentLevel); await ApplyLiveAsync(false);
        await InitializeLinkedControlsAsync(false);
        StatusText = "RU получила состояние уровня EN. Общее управление можно продолжать.";
    });
}
