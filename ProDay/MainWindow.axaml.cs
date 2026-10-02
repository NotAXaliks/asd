using Avalonia.Controls;
using Avalonia.Input;

namespace ProDay;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.F11)
                WindowState = WindowState == WindowState.FullScreen ? WindowState.Maximized : WindowState.FullScreen;
        };
    }
}
