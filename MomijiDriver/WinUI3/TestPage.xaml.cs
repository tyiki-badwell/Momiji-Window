using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Momiji.Driver;
public sealed partial class TestPage : UserControl
{
    public ViewModel ViewModel { get; } = new();

    public TestPage()
    {
        InitializeComponent();
    }
}

public class Model
{
    public string Text { get; } = "test.";
}

public class ViewModel
{
    public Model Model { get; } = new();
}
