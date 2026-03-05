using Avalonia.Controls;
using PIDControlDemo.Services;
using PIDControlDemo.ViewModels;

namespace PIDControlDemo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // ウィンドウサイズ・位置の復元
        var settings = WindowSettingsService.Load();
        Width = settings.Width;
        Height = settings.Height;
        if (settings.X >= 0 && settings.Y >= 0)
        {
            Position = new Avalonia.PixelPoint(settings.X, settings.Y);
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        var settings = new WindowSettings
        {
            Width = Width,
            Height = Height,
            X = Position.X,
            Y = Position.Y
        };
        WindowSettingsService.Save(settings);
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var vm = DataContext as MainWindowViewModel;
        if (vm == null) return;

        var upBtn = this.FindControl<Button>("ManualUpButton");
        var downBtn = this.FindControl<Button>("ManualDownButton");

        if (upBtn != null)
        {
            upBtn.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,
                (_, _) => vm.ManualUpPressCommand.Execute(null), handledEventsToo: true);
            upBtn.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent,
                (_, _) => vm.ManualUpReleaseCommand.Execute(null), handledEventsToo: true);
            upBtn.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent,
                (_, _) => vm.ManualUpReleaseCommand.Execute(null), handledEventsToo: true);
        }

        if (downBtn != null)
        {
            downBtn.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent,
                (_, _) => vm.ManualDownPressCommand.Execute(null), handledEventsToo: true);
            downBtn.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent,
                (_, _) => vm.ManualDownReleaseCommand.Execute(null), handledEventsToo: true);
            downBtn.AddHandler(Avalonia.Input.InputElement.PointerCaptureLostEvent,
                (_, _) => vm.ManualDownReleaseCommand.Execute(null), handledEventsToo: true);
        }
    }
}