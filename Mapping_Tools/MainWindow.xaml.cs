﻿using Mapping_Tools.Classes;
using Mapping_Tools.Classes.Exceptions;
using Mapping_Tools.Classes.SystemTools;
using Mapping_Tools.Classes.ToolHelpers;
using Mapping_Tools.Updater;
using Mapping_Tools.Views;
using Mapping_Tools.Views.Standard;
using MaterialDesignThemes.Wpf;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Mapping_Tools {

    public partial class MainWindow {
        private bool autoSave = true;
        private UpdaterWindow _updaterWindow;

        public ViewCollection Views;
        public ListenerManager ListenerManager;
        public bool SessionhasAdminRights;

        public static MainWindow AppWindow { get; set; }
        public static string AppCommon { get; set; }
        public static string AppDataPath { get; set; }
        public static string ExportPath { get; set; }
        public static HttpClient HttpClient { get; set; }
        public static SnackbarMessageQueue MessageQueue { get; set; }

        public MainWindow() {
            try {
                AppWindow = this;
                AppCommon = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                AppDataPath = Path.Combine(AppCommon, "Mapping Tools");
                ExportPath = Path.Combine(AppDataPath, "Exports");
                HttpClient = new HttpClient();
                HttpClient.DefaultRequestHeaders.Add("user-agent", "Mapping Tools");

                InitializeComponent();

                MessageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(2));
                MainSnackbar.MessageQueue = MessageQueue;

                Setup();
                SettingsManager.LoadConfig();
                ListenerManager = new ListenerManager();

                if (SettingsManager.Settings.MainWindowRestoreBounds.HasValue) {
                    SetToRect(SettingsManager.Settings.MainWindowRestoreBounds.Value);
                }

                SetFullscreen(SettingsManager.Settings.MainWindowMaximized);

                SetCurrentView(typeof(StandardView)); // Generate Standard view model to show on startup

                SetCurrentMaps(SettingsManager.GetLatestCurrentMaps()); // Set currentmap to previously opened map
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e) {
            _ = Update();
        }

        private void Setup() {
            SessionhasAdminRights = IsUserAdministrator();
            
            try {
                Directory.CreateDirectory(AppDataPath);
                Directory.CreateDirectory(ExportPath);
            }

            catch( Exception ex ) {
                ex.Show();
            }

            Views = new ViewCollection(); // Make a ViewCollection object

            ToolsMenu.ItemsSource = ViewCollection.GetAllToolTypes().Where(o => o.GetCustomAttribute<HiddenToolAttribute>() == null).Select(o => {
                var name = ViewCollection.GetName(o);
                var item = new MenuItem { Header = "_" + name, ToolTip = $"Open {name}." };
                item.Click += ViewSelectMenuItemOnClick;
                return item;
            }).OrderBy(o => o.Header);
        }

        private async Task Update(bool allowSkip = true, bool notifyUser = false) {
            try {
                var assetNamePattern = Environment.Is64BitProcess ? "release_x64.zip" : "release.zip";
                var updateManager = new UpdateManager("OliBomby", "Mapping_Tools", assetNamePattern);
                var hasUpdate = await updateManager.FetchUpdateAsync();

                if (!hasUpdate) {
                    if (notifyUser)
                        MessageQueue.Enqueue("No new versions available.");
                    return;
                }

                // Check if this version is newer than the version we skip
                var skipVersion = SettingsManager.Settings.SkipVersion;
                if (allowSkip && skipVersion != null && !(updateManager.UpdatesResult.LastVersion > skipVersion)) {
                    if (notifyUser)
                        MessageQueue.Enqueue($"Version {updateManager.UpdatesResult.LastVersion} skipped because of user config.");
                    return;
                }

                Dispatcher.Invoke(() => {
                    _updaterWindow = new UpdaterWindow(updateManager.Progress) {
                        ShowActivated = true
                    };

                    _updaterWindow.ActionSelected += async (sender, action) => {
                        switch (action) {
                            case UpdateAction.Restart:
                                await updateManager.DownloadUpdateAsync();
                                updateManager.RestartAfterUpdate = true;
                                updateManager.StartUpdateProcess();

                                _updaterWindow.Close();
                                Close();
                                break;

                            case UpdateAction.Wait:
                                await updateManager.DownloadUpdateAsync();
                                updateManager.RestartAfterUpdate = false;
                                updateManager.StartUpdateProcess();

                                _updaterWindow.Close();
                                break;

                            case UpdateAction.Skip:
                                // Update the skip version so we skip this version in the future
                                SettingsManager.Settings.SkipVersion = updateManager.UpdatesResult.LastVersion;
                                _updaterWindow.Close();
                                break;

                            default:
                                _updaterWindow.Close();
                                break;
                        }
                    };

                    _updaterWindow.Closed += (sender, e) => {
                        updateManager.Dispose();
                    };

                    _updaterWindow.Show();
                });
            } catch (Exception e) {
                MessageBox.Show("UPDATER_EXCEPTION: " + e.Message);
                if (notifyUser) {
                    MessageQueue.Enqueue("Error fetching update: " + e.Message);
                }
            }
        }

        private void Window_Closing(object sender, EventArgs e) {
            // Perform saving of settings at application exit
            if( autoSave ) {
                Views.AutoSaveSettings();
                if( DataContext is MappingTool mt ) {
                    mt.Dispose();
                }
                SettingsManager.UpdateSettings();
                SettingsManager.WriteToJson();
            }
        }

        //Close window
        private void CloseWin(object sender, RoutedEventArgs e) {
            Close();
        }

        //Close window without saving
        private void CloseWinNoSave(object sender, RoutedEventArgs e) {
            autoSave = false;
            Close();
        }

        private void SetCurrentView(string name) {
            try {
                SetCurrentView(Views.GetView(name));
            }
            catch( ArgumentException ex ) {
                ex.Show();
            }
        }

        private void SetCurrentView(Type type) {
            try {
                SetCurrentView(Views.GetView(type));
            }
            catch( ArgumentException ex ) {
                ex.Show();
            }
        }

        public void SetCurrentView(object view) {
            if( view == null )
                return;

            var type = view.GetType();

            if( FindName("header") is TextBlock txt ) {
                txt.Text = type.GetCustomAttribute<DontShowTitleAttribute>() == null ? $"Mapping Tools - {ViewCollection.GetName(type)}" : "Mapping Tools";
            }

            if( DataContext is MappingTool mt ) {
                mt.Deactivate();
            }
            if( view is MappingTool nmt ) {
                nmt.Activate();
            }

            DataContext = view;
            ViewChanged();
        }

        private void ViewSelectMenuItemOnClick(object sender, RoutedEventArgs e) {
            if( ( (MenuItem) sender ).Header == null )
                return;

            var toolName = ( (MenuItem) sender ).Header.ToString().Substring(1);
            SetCurrentView(toolName);
        }

        private bool IsUserAdministrator() {
            bool isAdmin;
            try {
                WindowsIdentity user = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(user);
                isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch( UnauthorizedAccessException ) {
                isAdmin = false;
            }
            catch( Exception ) {
                isAdmin = false;
            }
            return isAdmin;
        }

        public object GetCurrentView() {
            return DataContext;
        }

        public void SetCurrentMaps(string[] paths) {
            currentMap.Text = string.Join("|", paths);
            SettingsManager.AddRecentMap(paths, DateTime.Now);
        }

        public void SetCurrentMapsString(string paths) {
            currentMap.Text = paths;
            SettingsManager.AddRecentMap(paths.Split('|'), DateTime.Now);
        }

        public string[] GetCurrentMaps() {
            return currentMap.Text.Split('|');
        }

        public string GetCurrentMapsString() {
            return string.Join("|", GetCurrentMaps());
        }

        private void OpenBeatmap(object sender, RoutedEventArgs e) {
            try {
                string[] paths = IOHelper.BeatmapFileDialog(true);
                if( paths.Length != 0 ) {
                    SetCurrentMaps(paths);
                }
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void OpenGetCurrentBeatmap(object sender, RoutedEventArgs e) {
            try {
                string path = IOHelper.GetCurrentBeatmap();
                if( path != "" ) {
                    SetCurrentMaps(new[] { path });
                }
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void SaveBackup(object sender, RoutedEventArgs e) {
            try {
                var paths = GetCurrentMaps();
                var result = BackupManager.SaveMapBackup(paths, true, "UB");  // UB stands for User Backup
                if( result )
                    Task.Factory.StartNew(() => MessageQueue.Enqueue($"Beatmap{( paths.Length == 1 ? "" : "s" )} successfully copied!"));
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void LoadBackup(object sender, RoutedEventArgs e) {
            try {
                var paths = GetCurrentMaps();
                if( paths.Length > 1 ) {
                    throw new Exception($"Can't load backup into multiple beatmaps. You currently have {paths.Length} beatmaps selected.");
                }
                var backupPaths = IOHelper.BeatmapFileDialog(SettingsManager.GetBackupsPath(), false);
                if( backupPaths.Length == 1 ) {
                    try {
                        BackupManager.LoadMapBackup(backupPaths[0], paths[0], false);
                    }
                    catch( BeatmapIncompatibleException ex ) {
                        var exResult = ex.Show();
                        if( exResult == MessageBoxResult.Cancel )
                            return;
                        var result = MessageBox.Show("Do you want to load the backup anyways?", "Load backup",
                            MessageBoxButton.YesNo);
                        if( result == MessageBoxResult.Yes ) {
                            BackupManager.LoadMapBackup(backupPaths[0], paths[0], true);
                        }
                        else {
                            return;
                        }
                    }
                    Task.Factory.StartNew(() => MessageQueue.Enqueue("Backup successfully loaded!"));
                }
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void ViewChanged() {
            if( !( FindName("ProjectMenu") is MenuItem projectMenu ) )
                return;

            var isSavable = DataContext.GetType().GetInterfaces().Any(x =>
                              x.IsGenericType &&
                              x.GetGenericTypeDefinition() == typeof(ISavable<>));

            projectMenu.Visibility = Visibility.Collapsed;
            projectMenu.Items.Clear();

            if( isSavable ) {
                projectMenu.Visibility = Visibility.Visible;

                projectMenu.Items.Add(GetSaveProjectMenuItem());
                projectMenu.Items.Add(GetLoadProjectMenuItem());
                projectMenu.Items.Add(GetNewProjectMenuItem());
            }

            if( DataContext is IHaveExtraProjectMenuItems havingExtraProjectMenuItems ) {
                projectMenu.Visibility = Visibility.Visible;

                foreach( var menuItem in havingExtraProjectMenuItems.GetMenuItems() ) {
                    projectMenu.Items.Add(menuItem);
                }
            }
        }

        private MenuItem GetSaveProjectMenuItem() {
            var menu = new MenuItem {
                Header = "_Save project",
                Icon = new PackIcon { Kind = PackIconKind.ContentSave },
                ToolTip = "Save tool settings to file."
            };
            menu.Click += SaveProject;

            return menu;
        }

        private MenuItem GetLoadProjectMenuItem() {
            var menu = new MenuItem {
                Header = "_Load project",
                Icon = new PackIcon { Kind = PackIconKind.Folder },
                ToolTip = "Load tool settings from file."
            };
            menu.Click += LoadProject;

            return menu;
        }

        private MenuItem GetNewProjectMenuItem() {
            var menu = new MenuItem {
                Header = "_New project",
                Icon = new PackIcon { Kind = PackIconKind.Rocket },
                ToolTip = "Load the default tool settings."
            };
            menu.Click += NewProject;

            return menu;
        }

        private void LoadProject(object sender, RoutedEventArgs e) {
            if( !ProjectManager.IsSavable(DataContext) )
                return;
            dynamic data = DataContext;
            ProjectManager.LoadProject(data, true);
        }

        private void SaveProject(object sender, RoutedEventArgs e) {
            if( !ProjectManager.IsSavable(DataContext) )
                return;
            dynamic data = DataContext;
            ProjectManager.SaveProject(data, true);
        }

        private void NewProject(object sender, RoutedEventArgs e) {
            if( !ProjectManager.IsSavable(DataContext) )
                return;
            dynamic data = DataContext;
            ProjectManager.NewProject(data, true);
        }

        //Open backup folder in file explorer
        private void OpenBackups(object sender, RoutedEventArgs e) {
            try {
                System.Diagnostics.Process.Start("explorer.exe", SettingsManager.GetBackupsPath());
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void OpenConfig(object sender, RoutedEventArgs e) {
            try {
                System.Diagnostics.Process.Start("explorer.exe", AppDataPath);
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        private void OpenWebsite(object sender, RoutedEventArgs e) {
            System.Diagnostics.Process.Start("explorer.exe", "https://mappingtools.seira.moe/");
        }

        private void CoolSave(object sender, RoutedEventArgs e) {
            try {
                EditorReaderStuff.BetterSave();
            }
            catch( Exception ex ) {
                ex.Show();
            }
        }

        //Open project in browser
        private void OpenGitHub(object sender, RoutedEventArgs e) {
            System.Diagnostics.Process.Start("explorer.exe", "https://github.com/OliBomby/Mapping_Tools");
        }

        //Open info screen
        private void OpenInfo(object sender, RoutedEventArgs e) {
            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            var builder = new StringBuilder();
            builder.AppendLine($"Mapping Tools {version}");
            builder.AppendLine();
            builder.AppendLine("Made by:");
            builder.AppendLine("OliBomby");
            builder.AppendLine();
            builder.AppendLine("Supporters:");
            builder.AppendLine("Mercury");
            builder.AppendLine("Spoppyboi");
            builder.AppendLine("Pon -");
            builder.AppendLine("Ryuusei Aika");
            builder.AppendLine("Joshua Saku");
            builder.AppendLine();
            builder.AppendLine("Contributors:");
            builder.AppendLine("Potoofu");
            builder.AppendLine("Karoo13");
            builder.AppendLine("Coppertine");

            MessageBox.Show(builder.ToString(), "Info");
        }

        //Change top right icons on changed window state and set state variable
        private void Window_StateChanged(object sender, EventArgs e) {
            switch( WindowState ) {
                case WindowState.Maximized:
                    SetFullscreen(true, false);
                    break;

                case WindowState.Minimized:
                    break;

                case WindowState.Normal:
                    SetFullscreen(false, false);
                    break;
            }
        }

        //Clickevent for top right maximize/minimize button
        private void ToggleWin(object sender, RoutedEventArgs e) {
            SetFullscreen(WindowState != WindowState.Maximized);
        }

        private void SetFullscreen(bool fullscreen, bool actuallyChangeFullscreen = true) {
            if( !( FindName("toggle_button") is Button bt ) )
                return;

            if( fullscreen && WindowState != WindowState.Maximized ) {
                if( actuallyChangeFullscreen ) {
                    WindowState = WindowState.Maximized;
                    MasterGrid.Margin = new Thickness(5);
                }

                window_border.BorderThickness = new Thickness(0);
                bt.Content = new PackIcon { Kind = PackIconKind.WindowRestore };
            }
            else if( !fullscreen && WindowState == WindowState.Maximized ) {
                if( actuallyChangeFullscreen ) {
                    WindowState = WindowState.Normal;
                    MasterGrid.Margin = new Thickness(0);
                }

                window_border.BorderThickness = new Thickness(1);
                bt.Content = new PackIcon { Kind = PackIconKind.WindowMaximize };
            }
        }

        private void SetToRect(Rect rect) {
            Left = Math.Max(rect.Left, SystemParameters.VirtualScreenLeft);
            Top = Math.Max(rect.Top, SystemParameters.VirtualScreenTop);
            Width = Math.Max(rect.Width, 300);
            Height = Math.Max(rect.Height, 100);
        }

        //Minimize window on click
        private void MinimizeWin(object sender, RoutedEventArgs e) {
            WindowState = WindowState.Minimized;
        }

        //Enable drag control of window and set icons when docked
        private void DragWin(object sender, MouseButtonEventArgs e) {
            if( e.ChangedButton != MouseButton.Left )
                return;

            if( WindowState == WindowState.Maximized ) {
                var point = PointToScreen(e.MouseDevice.GetPosition(this));

                if( point.X <= RestoreBounds.Width / 2 )
                    Left = 0;
                else if( point.X >= RestoreBounds.Width )
                    Left = point.X - ( RestoreBounds.Width - ( ActualWidth - point.X ) );
                else
                    Left = point.X - ( RestoreBounds.Width / 2 );

                Top = point.Y - ( ( (FrameworkElement) sender ).ActualHeight / 2 );
            }
            if( e.LeftButton == MouseButtonState.Pressed )
                DragMove();
            //bt.Content = new PackIcon { Kind = PackIconKind.WindowRestore };
        }

        private async void MenuItem_OnClick(object sender, RoutedEventArgs e) {
            await Update(false, true);
        }
    }
}