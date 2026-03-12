using System.Windows;
using w_finder.Services;

namespace w_finder.Views;

public partial class SettingsWindow : Window
{
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

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        var settings = new RauncherSettings
        {
            IsDarkMode = DarkModeCheck.IsChecked == true,
            DefaultExportPath = ExportPathBox.Text,
            FilterPlacedTypesOnly = FilterPlacedCheck.IsChecked == true
        };

        SettingsService.Save(settings);
        DialogResult = true;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ApplyTheme(bool isDark)
    {
        string themeFile = isDark ? "Views/DarkTheme.xaml" : "Views/LightTheme.xaml";
        var themeUri = new Uri($"pack://application:,,,/w_finder;component/{themeFile}");
        var themeDictionary = new ResourceDictionary { Source = themeUri };
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(themeDictionary);
    }
}
