using Microsoft.Maui.Controls;
using MetaMorpheus.Maui.ViewModels;

namespace MetaMorpheus.Maui;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
