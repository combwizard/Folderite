using System.Windows.Controls;
using Folderite.Settings.ViewModels;

namespace Folderite.Settings.Pages;

public partial class MenuPage : UserControl
{
    public MenuPage()
    {
        InitializeComponent();
        RecentSlider.Maximum = SettingsVm.MaxRecentCount; // x:Static can't feed an int to a double property
    }
}
