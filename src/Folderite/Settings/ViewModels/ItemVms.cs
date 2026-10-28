using CommunityToolkit.Mvvm.ComponentModel;
using Folderite.Config;

namespace Folderite.Settings.ViewModels;

/// <summary>Editable <see cref="CustomAction"/>.</summary>
public sealed partial class ActionVm : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string name = "";

    [ObservableProperty]
    private string command = "";

    [ObservableProperty]
    private string args = "";

    public string DisplayName => MenuText.StripAccelerators(Name);

    public static ActionVm From(CustomAction action) =>
        new() { Name = action.Name, Command = action.Command, Args = action.Args };

    public CustomAction ToConfig() => new() { Name = Name, Command = Command, Args = Args };
}

/// <summary>A string in an editable list (WPF can't two-way bind to list items directly).</summary>
public sealed partial class StringItemVm : ObservableObject
{
    public StringItemVm(string value = "") => this.value = value;

    [ObservableProperty]
    private string value;
}
