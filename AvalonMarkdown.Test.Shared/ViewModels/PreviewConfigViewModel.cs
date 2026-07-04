using CommunityToolkit.Mvvm.ComponentModel;

namespace AvalonMarkdown.Test.Shared.ViewModels;

public partial class PreviewConfigViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _showToolbar = true;
}
