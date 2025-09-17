using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;

namespace MetaMorpheus.Maui;

public partial class App : Application
{
    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        MainPage = serviceProvider.GetRequiredService<MainPage>();
    }
}
