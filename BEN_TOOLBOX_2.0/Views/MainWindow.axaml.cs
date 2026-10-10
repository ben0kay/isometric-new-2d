using Avalonia.Controls;
using Avalonia.Interactivity;

namespace BEN_TOOLBOX_2_0.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowSection("Dashboard");
    }

    private void NavigateDashboard(object? sender, RoutedEventArgs e) => ShowSection("Dashboard");
    private void NavigateGame(object? sender, RoutedEventArgs e) => ShowSection("Game");
    private void NavigateFiles(object? sender, RoutedEventArgs e) => ShowSection("Files");
    private void NavigateInspect(object? sender, RoutedEventArgs e) => ShowSection("Inspect");
    private void NavigateSettings(object? sender, RoutedEventArgs e) => ShowSection("Settings");

    private void ShowSection(string section)
    {
        DashboardNav.Classes.Set("active", section == "Dashboard");
        GameNav.Classes.Set("active", section == "Game");
        FilesNav.Classes.Set("active", section == "Files");
        InspectNav.Classes.Set("active", section == "Inspect");
        SettingsNav.Classes.Set("active", section == "Settings");

        var dashboard = section == "Dashboard";
        HeroPanel.IsVisible = dashboard;
        GameTile.IsVisible = dashboard;
        FilesTile.IsVisible = dashboard;
        InspectTile.IsVisible = dashboard;
        SettingsTile.IsVisible = dashboard;

        // The same responsive card grid is reused for section summaries in Stage 1.
        switch (section)
        {
            case "Game":
                PageKicker.Text = "WORKSPACE / GAME DEVELOPMENT";
                PageTitle.Text = "Game Development";
                PageSubtitle.Text = "Artwork, sprites, project resources and game creation utilities.";
                SectionHeading.Text = "Coming in Stage 2";
                InfoTitle.Text = "GAME DEVELOPMENT TOOLBOX";
                InfoDescription.Text = "The existing Artwork Prompter, Sprite Splitter, Sprite Batch Tool and resource utilities will be connected here.";
                break;
            case "Files":
                PageKicker.Text = "WORKSPACE / FILE MANAGEMENT";
                PageTitle.Text = "File Toolbox";
                PageSubtitle.Text = "Keep your files organized and protected.";
                SectionHeading.Text = "Coming in Stage 2";
                InfoTitle.Text = "FILE MANAGEMENT TOOLS";
                InfoDescription.Text = "Folder Backup and Duplicate File Finder will be accessible here once PowerShell tool launching is connected.";
                break;
            case "Inspect":
                PageKicker.Text = "WORKSPACE / INSPECTORS";
                PageTitle.Text = "Project Inspectors";
                PageSubtitle.Text = "Analyze content, resource usage and project settings.";
                SectionHeading.Text = "Coming in Stage 2";
                InfoTitle.Text = "PROJECT DIAGNOSTICS";
                InfoDescription.Text = "Lighting, asset usage, file size and biome content inspectors will be organized here.";
                break;
            case "Settings":
                PageKicker.Text = "WORKSPACE / SETTINGS";
                PageTitle.Text = "Settings";
                PageSubtitle.Text = "Preferences and application customization.";
                SectionHeading.Text = "Coming in a later stage";
                InfoTitle.Text = "FUTURE SETTINGS";
                InfoDescription.Text = "Appearance controls, saved preferences and toolbox paths will be implemented after the core tool integration.";
                break;
            default:
                PageKicker.Text = "OVERVIEW / HOME";
                PageTitle.Text = "Your toolbox. Reimagined.";
                PageSubtitle.Text = "One streamlined space for development, file management and project utilities.";
                SectionHeading.Text = "Explore toolboxes";
                InfoTitle.Text = "STAGE 1 / FOUNDATION";
                InfoDescription.Text = "The interface, resizing and navigation are ready. Launching existing PowerShell tools comes in Stage 2.";
                break;
        }
    }
}
