using System.Windows;
using System.Windows.Threading;

namespace Workshop.App;

public partial class MainWindow
{
    public string FontStatus => builder.LegacyFontsInstalled
        ? UseLegacyFonts ? "PG Team установлены в RU; строчные меню показаны прописными" : "PG Team ещё в RU; применяется исходный шрифт…"
        : UseLegacyFonts ? "Кириллица ожидает сборки RU" : "В RU используются исходные шрифты";

    private async Task ApplyFontChangeAsync()
    {
        if (busy) return;
        await ExecuteAsync(async () =>
        {
            bool enPaused = english.Paused;
            if (Linked && english.Running) english.SetPaused(true);
            await StopSynchronizationAsync();
            bool launch = russian.Running, wasPaused = russian.Paused;
            string? state = null, level = null;
            if (launch && russian.Control.HasLevel)
            {
                level = russian.Control.CurrentLevel;
                state = await russian.Control.SaveStateAsync();
            }
            Persist(); var snapshot = Snapshot(); fullscreen?.Close(); linkedInput.Reset(); RestoreAudio();
            StatusText = "Применение шрифтов RU…";
            await russian.StopAsync(); RussianHost.Visibility = Visibility.Collapsed;
            await Task.Run(() => builder.Build(snapshot));
            if (launch)
            {
                RussianHost.Visibility = Visibility.Visible; await Dispatcher.Yield(DispatcherPriority.Loaded);
                await russian.StartAsync(builder.RussianRoot);
                if (state is not null && level is not null)
                {
                    await russian.Control.OpenLevelAsync(level); await russian.Control.LoadStateAsync(state, level);
                }
                await ApplyLiveAsync(false); russian.SetPaused(wasPaused);
                if (Linked && english.Running) english.SetPaused(enPaused);
                await InitializeLinkedControlsAsync();
            }
            Notify(nameof(FontStatus)); NotifyRuntime();
            StatusText = UseLegacyFonts ? "Кириллица PG Team применена к RU. В меню строчные русские буквы используют прописные глифы." : "RU пересобрана с исходными шрифтами; кириллица может не отображаться.";
        });
    }
}
