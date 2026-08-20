 using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Views.Windows;

public partial class AttributesPage : UserControl
{
    private AttributesPageViewModel ViewModel => (AttributesPageViewModel)DataContext!;

    public AttributesPage()
    {
        InitializeComponent();

        AttributesPageViewModel vm = App.Services.GetRequiredService<AttributesPageViewModel>();
        DataContext = vm;
    }
}