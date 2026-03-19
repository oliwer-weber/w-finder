using System.Windows;
using System.Windows.Input;
using w_finder.Helpers;
using w_finder.Services;

namespace w_finder.Views;

public partial class SettingsWindow : Window
{
    // Hotkey capture state — stored as raw values until Save
    private int _hotkeyKey;
    private int _hotkeyModifiers;

    public SettingsWindow()
    {
        InitializeComponent();
        ApplyTheme(SettingsService.Current.IsDarkMode);
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var s = SettingsService.Current;
        DarkModeCheck.IsChecked = s.IsDarkMode;
        ExportPathBox.Text = s.DefaultExportPath;
        FilterPlacedCheck.IsChecked = s.FilterPlacedTypesOnly;

        _hotkeyKey = s.HotkeyKey;
        _hotkeyModifiers = s.HotkeyModifiers;
        HotkeyBox.Text = GlobalKeyboardHook.FormatHotkey(_hotkeyModifiers, _hotkeyKey);

        LaunchBehaviorCombo.SelectedIndex = s.LaunchBehavior;
        DefaultModeCombo.SelectedIndex = s.DefaultMode;
        // Default mode panel only visible when "Clean slate" is selected
        DefaultModePanel.Visibility = s.LaunchBehavior == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Live-preview the theme in this dialog when the checkbox changes.
    /// </summary>
    private void DarkModeCheck_Changed(object sender, RoutedEventArgs e)
    {
        ApplyTheme(DarkModeCheck.IsChecked == true);
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select default export folder",
            InitialDirectory = ExportPathBox.Text
        };

        if (dlg.ShowDialog(this) == true)
        {
            ExportPathBox.Text = dlg.FolderName;
        }
    }

    /// <summary>
    /// Captures a key combination when the hotkey box is focused.
    /// The user presses a modifier + key combo and we store it.
    /// </summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true; // prevent the key from being typed into the box

        // Get the actual key (Key vs SystemKey for Alt combos)
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore bare modifier presses — wait for the actual key
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LeftShift || key == Key.RightShift)
            return;

        // Build modifier flags
        int modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= GlobalKeyboardHook.MOD_CTRL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= GlobalKeyboardHook.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= GlobalKeyboardHook.MOD_SHIFT;

        // Require at least one modifier so we don't capture bare letters
        if (modifiers == 0) return;

        // Convert WPF Key to Win32 virtual key code
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        _hotkeyKey = vk;
        _hotkeyModifiers = modifiers;
        HotkeyBox.Text = GlobalKeyboardHook.FormatHotkey(_hotkeyModifiers, _hotkeyKey);
    }

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "Press a key combination...";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        // Restore display if they didn't press anything
        HotkeyBox.Text = GlobalKeyboardHook.FormatHotkey(_hotkeyModifiers, _hotkeyKey);
    }

    private void LaunchBehaviorCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Only show the default mode picker when "Clean slate" is selected
        if (DefaultModePanel != null)
            DefaultModePanel.Visibility = LaunchBehaviorCombo.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var settings = new QuipSettings
        {
            IsDarkMode = DarkModeCheck.IsChecked == true,
            DefaultExportPath = ExportPathBox.Text,
            FilterPlacedTypesOnly = FilterPlacedCheck.IsChecked == true,
            HotkeyKey = _hotkeyKey,
            HotkeyModifiers = _hotkeyModifiers,
            LaunchBehavior = LaunchBehaviorCombo.SelectedIndex,
            DefaultMode = DefaultModeCombo.SelectedIndex
        };

        SettingsService.Save(settings);
        DialogResult = true;
    }

    private void CategoryList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GeneralPage == null) return; // Not yet initialized

        GeneralPage.Visibility = Visibility.Collapsed;
        ExportPage.Visibility = Visibility.Collapsed;
        FamilyModePage.Visibility = Visibility.Collapsed;
        HotkeyPage.Visibility = Visibility.Collapsed;

        switch (CategoryList.SelectedIndex)
        {
            case 0: GeneralPage.Visibility = Visibility.Visible; break;
            case 1: ExportPage.Visibility = Visibility.Visible; break;
            case 2: FamilyModePage.Visibility = Visibility.Visible; break;
            case 3: HotkeyPage.Visibility = Visibility.Visible; break;
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplyTheme(bool isDark)
    {
        string themeFile = isDark ? "Views/DarkTheme.xaml" : "Views/LightTheme.xaml";
        var themeUri = new Uri($"pack://application:,,,/Quip;component/{themeFile}");
        var themeDictionary = new ResourceDictionary { Source = themeUri };
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(themeDictionary);
    }
}
