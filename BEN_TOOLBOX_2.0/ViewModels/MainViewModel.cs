using CommunityToolkit.Mvvm.ComponentModel;

namespace BEN_TOOLBOX_2_0.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}
