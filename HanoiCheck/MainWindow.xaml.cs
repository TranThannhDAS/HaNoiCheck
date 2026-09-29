using System.Windows;

namespace HanoiCheck;

public partial class MainWindow : Window
{
    public MainWindow(IServiceProvider services)
    {
        InitializeComponent();
        BlazorView.Services = services;
    }
}
