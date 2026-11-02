using System.Windows;
using Workshop.Core;

namespace Workshop.App;

public partial class MainWindow
{
    private FrameSynchronizer? frameSync;
    private string synchronizationText = "Синхронизация выключена";
    public string SynchronizationText { get => synchronizationText; private set { synchronizationText = value; Notify(); } }
    private async Task StopSynchronizationAsync()
    {
        if (frameSync is { } sync)
        {
            bool enPaused = english.Paused, ruPaused = russian.Paused;
            await sync.HoldAsync();
            if (english.Running) english.Control.Pause();
            if (russian.Running) russian.Control.Pause();
            await Task.WhenAll(new[] { english, russian }.Where(x => x.Running).Select(x => x.Control.WaitPausedAsync()));
            frameSync = null; await sync.DisposeAsync();
            if (!enPaused && english.Running) english.Control.Resume();
            if (!ruPaused && russian.Running) russian.Control.Resume();
        }
        linkedInput.EngineOnly = false;
        SynchronizationText = "Синхронизация выключена";
    }
    private async Task InitializeLinkedControlsAsync(bool align = true)
    {
        await StopSynchronizationAsync();
        if (!Linked || !english.Running || !russian.Running) return;
        var en = english.Control; var ru = russian.Control;
        bool enPaused = english.Paused, ruPaused = russian.Paused;
        await Task.WhenAll(en.WaitForStartupAsync(), ru.WaitForStartupAsync());
        en.Pause(); ru.Pause();
        await Task.WhenAll(en.WaitPausedAsync(), ru.WaitPausedAsync());
        if (align && en.SceneSaveReady)
        {
            string level = en.CurrentLevel;
            string state = await en.SaveStateAsync();
            if (!ru.SceneSaveReady || !SceneCheckpoint.SameLevel(level, ru.CurrentLevel))
                await ru.OpenLevelAsync(level, true);
            await ru.LoadStateAsync(state, level);
        }
        frameSync = new(en, ru, () => !busy && !applyingLive, text => Dispatcher.BeginInvoke(() => SynchronizationText = text));
        frameSync.SetSizes(EnglishHost.ClientSize, RussianHost.ClientSize);
        linkedInput.EngineOnly = true;
        linkedInput.Reset();
        en.Resume(); ru.Resume();
        if (enPaused || ruPaused) { en.Pause(); ru.Pause(); }
        SynchronizationText = "Синхронно · общий шаг 60 Гц";
        NotifyRuntime();
    }
    private async void ChangeLinkedModeAsync()
    {
        if (busy) return;
        await ExecuteAsync(async () => { if (Linked) await InitializeLinkedControlsAsync(); else await StopSynchronizationAsync(); });
    }
}
