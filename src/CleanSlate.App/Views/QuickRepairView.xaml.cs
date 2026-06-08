using System.Windows;
using System.Windows.Controls;
using CleanSlate.Core.Modules;
using CleanSlate.App.ViewModels;

namespace CleanSlate.App.Views;

public partial class QuickRepairView : UserControl
{
    public QuickRepairView()
    {
        InitializeComponent();
    }

    private void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is RepairCheck check &&
            DataContext is QuickRepairViewModel vm)
        {
            _ = vm.RepairCheckAsync(check);
        }
    }
}
