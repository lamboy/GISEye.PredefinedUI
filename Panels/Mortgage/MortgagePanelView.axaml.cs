using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GISEye.PredefinedUI.Panels.Mortgage;

public partial class MortgagePanelView : UserControl
{
    public MortgagePanelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
