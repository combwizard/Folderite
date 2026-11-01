using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Folderite.Config;
using Folderite.Hooks;
using Folderite.Settings.ViewModels;

namespace Folderite.Settings.Pages;

public partial class GeneralPage : UserControl
{
    private const string AppName = "Folderite";

    // Shown under the recorder after a key press that can't be a shortcut; cleared by the next key or by leaving the box.
    private bool _needsModifier;
    private GeneralVm? _vm;

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachSettings();
    }

    // The page's DataContext is GeneralVm, but the shortcut is saved by the SettingsVm that the window uses as its own.
    private void AttachSettings()
    {
        if (DataContext is not GeneralVm vm)
            return;
        if (_vm != vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmChanged;
        }
        if (Window.GetWindow(this)?.DataContext is SettingsVm settings)
            vm.AttachSettings(settings);
        UpdateHotkeyCaption();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeneralVm.HotkeyConflict))
            UpdateHotkeyCaption();
    }

    private void HotkeyBox_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        _needsModifier = false;
        UpdateHotkeyCaption();
    }

    private void UpdateHotkeyCaption()
    {
        string? text = null;
        string brush = "TextFillColorSecondaryBrush";
        if (_needsModifier)
            text = "Add Ctrl, Alt, Shift or Win";
        else if (_vm?.HotkeyConflict == true)
        {
            text = "This shortcut is already used by another app. Pick another one.";
            brush = "SystemFillColorCautionBrush";
        }
        else if (HotkeyBox.IsKeyboardFocused)
            text = "Press the keys you want to use";

        HotkeyCaption.Text = text;
        HotkeyCaption.SetResourceReference(TextBlock.ForegroundProperty, brush);
        HotkeyCaption.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // The box is a recorder: whatever combination is pressed becomes the shortcut, so nothing may reach the text box itself.
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = HotkeyModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= HotkeyModifiers.Shift;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= HotkeyModifiers.Win;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.ImeProcessed or Key.DeadCharProcessed or Key.None)
        {
            e.Handled = true; // modifier on its own: wait for the key that goes with it
            return;
        }

        if (mods == HotkeyModifiers.None)
        {
            switch (key)
            {
                case Key.Tab:
                    return; // normal focus movement
                case Key.Escape:
                    e.Handled = true;
                    Keyboard.ClearFocus();
                    return;
                case Key.Back or Key.Delete:
                    e.Handled = true;
                    _needsModifier = false;
                    _vm?.SetHotkey("");
                    UpdateHotkeyCaption();
                    return;
            }
        }

        e.Handled = true;
        var hotkey = new Hotkey(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        _needsModifier = !hotkey.IsValid;
        if (hotkey.IsValid)
            _vm?.SetHotkey(hotkey.ToString());
        UpdateHotkeyCaption();
    }

    private void ChangeLocation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeneralVm vm)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder for Folderite's settings",
            InitialDirectory = vm.ConfigDirectory,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            _ = MoveAsync(vm, dialog.FolderName);
    }

    private void UseDefault_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is GeneralVm vm)
            _ = MoveAsync(vm, ConfigLocation.DefaultDirectory);
    }

    private async Task MoveAsync(GeneralVm vm, string target)
    {
        if (ConfigLocation.SameDirectory(target, vm.ConfigDirectory) || Window.GetWindow(this) is not { } window)
            return;

        bool toPortable = ConfigMove.IsPortableTarget(target, ConfigLocation.PortableDirectory);
        if (toPortable && MessageBox.Show(window, ConfigMove.PortablePrompt(target), AppName,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        bool hasConfig = ConfigRelocator.HasConfig(target);
        var answer = hasConfig ? AskConflict(window, target) : ConflictChoice.Cancel;
        if (ConfigMove.ChooseMode(hasConfig, answer) is not { } mode)
            return;

        window.IsEnabled = false;
        MoveOutcome outcome;
        try
        {
            outcome = await vm.MoveConfigAsync(target, mode);
        }
        finally
        {
            window.IsEnabled = true;
        }

        switch (outcome.Status)
        {
            case MoveStatus.Moved:
                SettingsApp.OpenWindow("general", toPortable ? "Folderite is now portable" : $"Settings moved to {target}");
                break;
            case MoveStatus.TrayWouldNotClose:
                Error(window, "Folderite didn't close, so the settings were not moved.");
                break;
            default:
                // The store was released before the move, so this window can't be used any more: reopen on the old location.
                Error(window, outcome.Error ?? "The settings could not be moved.");
                SettingsApp.OpenWindow("general", "Settings were not moved");
                break;
        }
    }

    private static ConflictChoice AskConflict(Window owner, string folder) =>
        MessageBox.Show(owner, ConfigMove.ConflictPrompt(folder), AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => ConflictChoice.UseExisting,
            MessageBoxResult.No => ConflictChoice.Replace,
            _ => ConflictChoice.Cancel,
        };

    private static void Error(Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Error);
}
