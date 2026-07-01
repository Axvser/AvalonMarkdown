using Avalonia.Controls;
using AvalonMarkdown.ViewModels;

namespace AvalonMarkdown.Views;

public partial class BrowserMainView : UserControl
{
    public BrowserMainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
