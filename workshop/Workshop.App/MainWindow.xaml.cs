using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Workshop.Core;

namespace Workshop.App;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly Corpus corpus;
    private readonly BuildService builder;
    private readonly GameSession english, russian;
    private readonly DispatcherTimer autosave = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private TranslationProject project = new();
    private string projectPath;
    private bool dirty;
    private volatile bool busy;
    private RowViewModel? selected;
    private string search = "", section = "Все разделы", statusFilter = "Все статусы", statusText = "Готово", contextText = "Выберите реплику и нажмите «Найти контекст».";
    public ObservableCollection<RowViewModel> Rows { get; } = [];
    public ICollectionView VisibleRows { get; private set; }
    public List<string> Sections { get; private set; }
    public string[] Statuses { get; } = ["Все статусы", "Не переведено", "Черновик", "Проверено", "Ошибка"];
    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow(string researchRoot, string? initialProject)
    {
        InitializeComponent();
        corpus = new(researchRoot);
        builder = new(corpus);
        projectPath = initialProject ?? Path.Combine(researchRoot, "projects", "main.2dark.json");
        Sections = new[] { "Все разделы" }.Concat(corpus.Rows.Select(x => x.Section).Distinct()).ToList();
        VisibleRows = CollectionViewSource.GetDefaultView(Rows);
        VisibleRows.Filter = Matches;
        english = new(EnglishHost, builder.EnglishRoot); russian = new(RussianHost, builder.RussianRoot);
        InitializeRuntimeTools();
        DataContext = this;
        LoadProject(projectPath);
        autosave.Tick += async (_, _) =>
        {
            autosave.Stop();
            try { Persist(); } catch (Exception ex) { StatusText = "Ошибка автосохранения: " + ex.Message; }
            Notify(nameof(ProgressText));
            if (LiveTranslation && !busy) await ApplyLiveAsync(false);
        };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; Persist(); }
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; SearchBox.Focus(); SearchBox.SelectAll(); }
            if (e.Key == Key.F5 && Ready) { e.Handled = true; await BuildAsync(true); }
        };
    }

    public bool Ready => !busy;
    public string ProjectCaption => $"{Path.GetFileName(projectPath)}  ·  {(dirty ? "сохранение…" : "сохранено / готово к работе")}";
    public string CountText => $"{VisibleRows.Cast<RowViewModel>().Count()} / {Rows.Count}";
    public string ProgressText => $"Переведено {Rows.Count(x => x.HasTranslation)} из {Rows.Count}  ·  Проверено {Rows.Count(x => x.HasTranslation && x.Reviewed)}  ·  Установленная игра не изменяется";
    public string StatusText { get => statusText; set { statusText = value; Notify(); } }
    public string ContextText { get => contextText; set { contextText = value; Notify(); } }
    public string Search { get => search; set { search = value; RefreshFilter(); Notify(); } }
    public string Section { get => section; set { section = value; RefreshFilter(); Notify(); } }
    public string StatusFilter { get => statusFilter; set { statusFilter = value; RefreshFilter(); Notify(); } }
    public bool UseLegacyFonts { get => project.UseLegacyFonts; set { if (project.UseLegacyFonts != value) { project.UseLegacyFonts = value; MarkDirty(); Notify(); Notify(nameof(FontStatus)); Dispatcher.BeginInvoke(async () => await ApplyFontChangeAsync()); } } }
    public RowViewModel? Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            if (selected is not null) selected.PropertyChanged -= SelectedChanged;
            selected = value;
            if (selected is not null) selected.PropertyChanged += SelectedChanged;
            ContextText = "Выберите «Найти контекст», чтобы увидеть упоминания этого ключа в скриптах.";
            Notify(); Notify(nameof(ValidationText)); Notify(nameof(ReferenceText));
        }
    }
    public string ValidationText => Selected is null ? "Выберите реплику" : Selected.Diagnostics.Count == 0
        ? (Selected.HasTranslation ? "Ссылки и переменные сохранены" : "Перевод ещё не добавлен")
        : string.Join("  •  ", Selected.Diagnostics.Select(x => x.Severity + ": " + x.Message));

    private void SelectedChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(RowViewModel.Diagnostics)) Notify(nameof(ValidationText)); }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void MarkDirty() { dirty = true; Notify(nameof(ProjectCaption)); autosave.Stop(); autosave.Start(); }

    private bool Matches(object value)
    {
        var row = (RowViewModel)value;
        if (section != "Все разделы" && row.Section != section) return false;
        if (statusFilter != "Все статусы" && row.Status != statusFilter) return false;
        return string.IsNullOrWhiteSpace(search) || new[] { row.Key, row.Original, row.Target, row.Source.French, row.Source.Legacy, row.Note }
            .Any(x => x.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshFilter()
    {
        if (VisibleRows is null) return;
        VisibleRows.Refresh(); Notify(nameof(CountText));
    }

    private void LoadProject(string path)
    {
        // Parse fully before replacing the active session; corrupt files stay untouched.
        var loaded = ProjectStore.Load(path);
        foreach (var row in Rows) row.Edited -= RowEdited;
        Selected = null;
        Rows.Clear();
        project = loaded;
        projectPath = path;
        foreach (var source in corpus.Rows)
        {
            var row = new RowViewModel(source, project.Edits.GetValueOrDefault(source.Record.Id));
            TrackLiveBaseline(row);
            row.Edited += RowEdited;
            Rows.Add(row);
        }
        dirty = false;
        RefreshFilter();
        Selected = VisibleRows.Cast<RowViewModel>().FirstOrDefault(x => x.Key == "MAIN_Adventure") ?? VisibleRows.Cast<RowViewModel>().FirstOrDefault();
        if (Selected is not null) RecordList.ScrollIntoView(Selected);
        foreach (string property in new[] { nameof(ProjectCaption), nameof(ProgressText), nameof(UseLegacyFonts) }) Notify(property);
        StatusText = $"Открыто {Rows.Count} записей. Ctrl+F — поиск, Ctrl+S — сохранить, F5 — собрать и запустить RU.";
    }

    private void RowEdited(RowViewModel row) { TrackLiveBaseline(row); MarkDirty(); }
    private TranslationProject Snapshot()
    {
        var known = Rows.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var edits = project.Edits.Where(x => !known.Contains(x.Key)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var row in Rows.Where(x => x.HasSavedContent)) edits.Add(row.Id, row.ToEdit());
        return new() { Name = project.Name, UseLegacyFonts = project.UseLegacyFonts, Edits = edits };
    }

    private void Persist(bool force = false)
    {
        if (!dirty && !force) return;
        var snapshot = Snapshot();
        ProjectStore.Save(projectPath, snapshot);
        project = snapshot;
        dirty = false;
        Notify(nameof(ProjectCaption)); Notify(nameof(ProgressText));
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; Notify(nameof(Ready));
        try { while (applyingLive) await Task.Delay(15); if (frameSync is { } sync) await sync.HoldAsync(); await action(); }
        catch (Exception ex)
        {
            if (!EnglishHost.IsAttached) EnglishHost.Visibility = Visibility.Collapsed;
            if (!RussianHost.IsAttached) RussianHost.Visibility = Visibility.Collapsed;
            StatusText = ex.Message;
            Directory.CreateDirectory(Path.Combine(corpus.ResearchRoot, "logs"));
            File.AppendAllText(Path.Combine(corpus.ResearchRoot, "logs", "workshop.log"), $"{DateTime.Now:O} {ex}\n");
            MessageBox.Show(this, ex.Message, "Мастерская 2Dark", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { busy = false; Notify(nameof(Ready)); }
    }

    private async Task BuildAsync(bool launch) => await ExecuteAsync(async () =>
    {
        fullscreen?.Close();
        RestoreAudio();
        Persist();
        var snapshot = Snapshot();
        StatusText = "Подготовка тестовой сборки…";
        bool enWasPaused = english.Paused;
        if (Linked && english.Running) english.SetPaused(true);
        await StopSynchronizationAsync();
        await russian.StopAsync();
        RussianHost.Visibility = Visibility.Collapsed;
        var progress = new Progress<string>(x => StatusText = x);
        var result = await Task.Run(() => builder.Build(snapshot, progress));
        Notify(nameof(FontStatus));
        StatusText = $"Сборка готова за {result.Seconds:F2} с: изменено архивов {result.Archives}, текстовых ресурсов {result.TextResources}.";
        if (launch)
        {
            RussianHost.Visibility = Visibility.Visible;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            await russian.StartAsync(result.Directory);
            if (Linked && english.Running) english.SetPaused(enWasPaused);
            await InitializeLinkedControlsAsync();
            await ApplyLiveAsync(false);
            StatusText = "Русская тестовая копия запущена. Управление — щелчком внутри игры. Сборка применена к английскому слоту языка.";
        }
        else if (Linked && english.Running) english.SetPaused(enWasPaused);
    });

    private async void BuildOnly(object sender, RoutedEventArgs e) => await BuildAsync(false);
    private async void BuildAndLaunch(object sender, RoutedEventArgs e) => await BuildAsync(true);
    private async void LaunchEnglish(object sender, RoutedEventArgs e) => await ExecuteAsync(async () =>
    {
        StatusText = "Подготовка английской копии…";
        var progress = new Progress<string>(x => StatusText = x);
        var path = await Task.Run(() => builder.PrepareEnglish(progress));
        EnglishHost.Visibility = Visibility.Visible;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await english.StartAsync(path);
        await InitializeLinkedControlsAsync();
        StatusText = "Английская копия запущена. «Снимок ENG» сохраняет изображение без паузы.";
    });

    private async void CaptureEnglish(object sender, RoutedEventArgs e) => await ExecuteAsync(() =>
    {
        Activate();
        var path = SaveCapture(english.Capture(), "eng");
        StatusText = "Снимок ENG сохранён: " + path;
        return Task.CompletedTask;
    });

    private async void CaptureRussian(object sender, RoutedEventArgs e) => await ExecuteAsync(() =>
    {
        Activate();
        var path = SaveCapture(russian.Capture(), "ru");
        StatusText = "Снимок RU сохранён: " + path;
        return Task.CompletedTask;
    });

    private string SaveCapture(BitmapSource image, string suffix)
    {
        string dir = Path.Combine(corpus.ResearchRoot, "captures");
        Directory.CreateDirectory(dir);
        string key = Selected?.Key ?? "scene";
        foreach (char c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
        key = key[..Math.Min(key.Length, 60)];
        string path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{key}-{suffix}.png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path); encoder.Save(output);
        return path;
    }

    private async void DetachEnglish(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await StopSynchronizationAsync(); english.Detach(); EnglishHost.Visibility = Visibility.Collapsed; StatusText = "Окно EN отделено. Кнопка запуска подключит его снова."; });
    private async void DetachRussian(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await StopSynchronizationAsync(); russian.Detach(); RussianHost.Visibility = Visibility.Collapsed; StatusText = "Окно RU отделено. Для подключения используйте запуск."; });
    private async void StopEnglish(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await StopSynchronizationAsync(); linkedInput.Reset(); await english.StopAsync(); EnglishHost.Visibility = Visibility.Collapsed; NotifyRuntime(); StatusText = "EN выключена."; });
    private async void StopRussian(object sender, RoutedEventArgs e) => await ExecuteAsync(async () => { await StopSynchronizationAsync(); linkedInput.Reset(); await russian.StopAsync(); RussianHost.Visibility = Visibility.Collapsed; NotifyRuntime(); StatusText = "RU выключена."; });
    private async void EscapeEnglish(object sender, RoutedEventArgs e) => await SendEscapeAsync(english, EnglishHost);
    private async void EscapeRussian(object sender, RoutedEventArgs e) => await SendEscapeAsync(russian, RussianHost);
    private void UseLegacy(object sender, RoutedEventArgs e) { if (Selected is not null && Selected.Source.Legacy.Length > 0) { Selected.Target = Selected.Source.Legacy; TranslationBox.Focus(); } else StatusText = "У этой строки нет старого перевода."; }
    private void UseEnglish(object sender, RoutedEventArgs e) { if (Selected is not null) { Selected.Target = Selected.Original; TranslationBox.Focus(); } }
    private void SaveProject(object sender, RoutedEventArgs e) { try { Persist(true); StatusText = "Проект сохранён."; } catch (Exception ex) { MessageBox.Show(this, ex.Message); } }

    private void SaveProjectAs(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Проект 2Dark|*.2dark.json", FileName = Path.GetFileName(projectPath), InitialDirectory = Path.GetDirectoryName(projectPath) };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var snapshot = Snapshot(); ProjectStore.Save(dialog.FileName, snapshot);
            projectPath = dialog.FileName; project = snapshot; dirty = false; Notify(nameof(ProjectCaption)); StatusText = "Проект сохранён: " + projectPath;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }

    private void OpenProject(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Проект 2Dark|*.2dark.json;*.json", InitialDirectory = Path.GetDirectoryName(projectPath) };
        if (dialog.ShowDialog(this) != true) return;
        try { Persist(); LoadProject(dialog.FileName); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Проект не открыт"); }
    }

    private void MoveRow(int delta)
    {
        var visible = VisibleRows.Cast<RowViewModel>().ToList();
        int index = Selected is null ? -1 : visible.IndexOf(Selected);
        int next = index + delta;
        if (next >= 0 && next < visible.Count) { Selected = visible[next]; RecordList.ScrollIntoView(Selected); }
    }
    private void PreviousRow(object sender, RoutedEventArgs e) => MoveRow(-1);
    private void NextRow(object sender, RoutedEventArgs e) => MoveRow(1);

    private async void FindContext(object sender, RoutedEventArgs e)
    {
        if (Selected is null) return;
        string key = Selected.Key;
        ContextText = "Поиск…";
        var result = await Task.Run(() =>
        {
            var output = new StringBuilder();
            string root = Path.Combine(corpus.ResearchRoot, "original");
            var paths = Directory.EnumerateFiles(Path.Combine(root, "global", "scripts"), "*.txt", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(root, "*_script.txt", SearchOption.AllDirectories));
            int matches = 0;
            foreach (var path in paths)
            {
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!lines[i].Contains(key, StringComparison.Ordinal)) continue;
                    output.AppendLine($"{Path.GetRelativePath(root, path)}:{i + 1}");
                    for (int n = Math.Max(0, i - 2); n <= Math.Min(lines.Length - 1, i + 3); n++) output.AppendLine($"{n + 1,5}  {lines[n]}");
                    output.AppendLine();
                    if (++matches >= 30) return output + "Показаны первые 30 совпадений.";
                }
            }
            return matches == 0 ? "Прямых упоминаний в текстовых скриптах не найдено. Реплика может вызываться из карты или по составному ключу." : output.ToString();
        });
        if (Selected?.Key == key) ContextText = result;
        var box = new System.Windows.Controls.TextBox { Text = result, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Margin = new Thickness(16), FontFamily = new System.Windows.Media.FontFamily("Consolas") };
        new Window { Owner = this, Title = "Упоминания в скриптах: " + key, Width = 900, Height = 600, Content = box, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }

    private void ShowProblems(object sender, RoutedEventArgs e)
    {
        var issues = Rows.SelectMany(row => row.Diagnostics.Select(d => $"{d.Severity}  {row.Section} / {row.Key}\n{d.Message}\n")).ToList();
        if (issues.Count == 0) { StatusText = "В добавленных переводах ошибок ссылок и переменных не найдено."; return; }
        var box = new System.Windows.Controls.TextBox { Text = string.Join("\n", issues), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Margin = new Thickness(16) };
        new Window { Owner = this, Title = $"Проверка перевода: {issues.Count} замечаний", Width = 800, Height = 550, Content = box, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }

    private bool closingReady;
    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closingReady) return;
        if (busy || applyingLive) { e.Cancel = true; StatusText = "Дождитесь завершения текущей операции."; return; }
        try { Persist(); }
        catch (Exception ex) { e.Cancel = true; MessageBox.Show(this, "Не удалось сохранить проект: " + ex.Message); return; }
        e.Cancel = true; busy = true;
        await StopSynchronizationAsync();
        fullscreen?.Close(); autosave.Stop(); liveTimer.Stop(); inputTimer.Stop(); audioTimer.Stop(); viewportTimer.Stop();
        RestoreAudio(); audio.Dispose(); linkedInput.Dispose(); english.Release(); russian.Release();
        closingReady = true; _ = Dispatcher.BeginInvoke(Close);
    }
}
