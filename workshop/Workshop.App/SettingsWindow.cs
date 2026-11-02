using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Workshop.Core;

namespace Workshop.App;

public sealed class SettingsWindow : Window
{
    public WorkshopSettings Settings { get; }
    public SettingsWindow(Window owner, WorkshopSettings settings)
    {
        Owner = owner; Title = "Настройки мастерской"; Width = 600; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Settings = settings.Copy(); DataContext = Settings;
        Background = (System.Windows.Media.Brush)FindResource("Canvas"); Foreground = (System.Windows.Media.Brush)FindResource("Text");
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void Heading(string text) => panel.Children.Add(new TextBlock { Text = text, FontSize = 18, Margin = new Thickness(0, 6, 0, 10) });
        void Choice(string label, string property, string[] values, StackPanel? parent = null)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(260) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var combo = new ComboBox { ItemsSource = values }; combo.SetBinding(ComboBox.SelectedItemProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            System.Windows.Automation.AutomationProperties.SetName(combo, label); Grid.SetColumn(combo, 1); row.Children.Add(combo); (parent ?? panel).Children.Add(row);
        }
        CheckBox Flag(string label, string property)
        {
            var box = new CheckBox { Content = label }; box.SetBinding(CheckBox.IsCheckedProperty, new Binding(property)); panel.Children.Add(box); return box;
        }
        Heading("Полный экран и сравнение");
        Choice("Полный экран / редактор", nameof(Settings.FullscreenKey), WorkshopSettings.ShortcutKeys);
        Choice("Переключить EN / RU", nameof(Settings.SwitchVersionKey), WorkshopSettings.ShortcutKeys);
        var showLabel = Flag("Показывать надпись EN / RU ver.", nameof(Settings.ShowVersionLabel));
        var labelOptions = new StackPanel();
        labelOptions.SetBinding(VisibilityProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = showLabel, Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(labelOptions);
        Choice("Расположение надписи", nameof(Settings.LabelPosition), WorkshopSettings.Positions, labelOptions);
        var color = new TextBox { Margin = new Thickness(0, 4, 0, 6) };
        color.SetBinding(TextBox.TextProperty, new Binding(nameof(Settings.LabelColor)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        System.Windows.Automation.AutomationProperties.SetName(color, "Цвет надписи #RRGGBB");
        labelOptions.Children.Add(new TextBlock { Text = "Цвет надписи (#RRGGBB)" }); labelOptions.Children.Add(color);
        labelOptions.Children.Add(new TextBlock { Text = "Непрозрачность: слева 0%, справа 100%" });
        var opacity = new Slider { Minimum = 0, Maximum = 1, TickFrequency = .1, Margin = new Thickness(0, 8, 0, 10) };
        opacity.SetBinding(Slider.ValueProperty, new Binding(nameof(Settings.LabelOpacity))); labelOptions.Children.Add(opacity);
        labelOptions.Children.Add(new TextBlock { Text = "Размер надписи" });
        var size = new Slider { Minimum = 14, Maximum = 64, TickFrequency = 2, IsSnapToTickEnabled = true, Margin = new Thickness(0, 8, 0, 10) };
        size.SetBinding(Slider.ValueProperty, new Binding(nameof(Settings.LabelSize))); labelOptions.Children.Add(size);
        Heading("Звук");
        var singleAudio = Flag("Использовать один источник звука", nameof(Settings.SingleAudioSource));
        var audioOptions = new StackPanel();
        audioOptions.SetBinding(VisibilityProperty, new Binding(nameof(CheckBox.IsChecked)) { Source = singleAudio, Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(audioOptions);
        Choice("Слышна только копия", nameof(Settings.AudioSource), ["EN", "RU"], audioOptions);
        panel.Children.Add(new TextBlock { Text = "При выключении режима слышны обе копии. Игровые настройки громкости сохраняются.", Margin = new Thickness(0, 6, 0, 10) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Отмена", IsCancel = true }; buttons.Children.Add(cancel);
        var save = new Button { Content = "Применить", IsDefault = true }; buttons.Children.Add(save);
        save.Click += (_, _) => { try { Settings.Validate(); DialogResult = true; } catch (InvalidDataException ex) { MessageBox.Show(this, ex.Message, Title); } };
        panel.Children.Add(buttons);
    }
}
