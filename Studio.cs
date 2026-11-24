using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("AppDeploy Studio")]
[assembly: AssemblyDescription("PSAppDeployToolkit packaging studio for SCCM and Intune")]
[assembly: AssemblyProduct("AppDeploy Studio")]
[assembly: AssemblyCompany(AppDeployStudio.Studio.AuthorName)]
[assembly: AssemblyCopyright("Copyright (c) 2026 " + AppDeployStudio.Studio.AuthorName)]
[assembly: AssemblyVersion("1.2.0.0")]

namespace AppDeployStudio
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            var app = new Application();
            try { app.Run(new Studio().Window); }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "AppDeploy Studio", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    internal sealed class Studio
    {
        internal const string AuthorName = "Jonathan Cassara";
        private const string AuthorGitHub = "https://github.com/Jonathancassara";
        private const string ProjectGitHub = AuthorGitHub + "/AppDeployStudio";
        internal readonly Window Window;
        private readonly ActionEditor install, uninstall, repair;
        private string lastOutput;
        private string statusKey = "StartHint", previewKey = "EmptyPreview", downloadKey;
        private object[] statusArguments = new object[0], downloadArguments = new object[0];
        private bool downloading;

        internal Studio()
        {
            using (var layout = Assembly.GetExecutingAssembly().GetManifestResourceStream("Studio.xaml"))
                Window = (Window)XamlReader.Load(layout);
            using (var icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Brand.png"))
                Window.Icon = Find<Image>("BrandIcon").Source = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            var actions = Find<StackPanel>("ActionsHost");
            install = new ActionEditor(Window, actions, "Install", true);
            uninstall = new ActionEditor(Window, actions, "Uninstall", false);
            repair = new ActionEditor(Window, actions, "Repair", false);

            Find<Button>("BrowsePayload").Click += (sender, args) => BrowseFolder("Payload");
            Find<Button>("BrowseDestination").Click += (sender, args) => BrowseFolder("Destination");
            Find<Button>("RefreshPreview").Click += async (sender, args) => await UpdatePackage(false);
            Find<Button>("Generate").Click += async (sender, args) => await UpdatePackage(true);
            Find<Button>("OpenOutput").Click += (sender, args) => RunUiAction(() => Process.Start(lastOutput));
            Find<Button>("CopyCommands").Click += (sender, args) => RunUiAction(() =>
            {
                Clipboard.SetText(Find<TextBox>("Commands").Text);
                SetStatus("Copied");
            });
            var versions = Find<ComboBox>("TemplateVersion");
            versions.ItemsSource = TemplateRelease.Catalog;
            versions.SelectedIndex = 0;
            versions.SelectionChanged += (sender, args) => UpdateTemplateDetails();
            Find<Button>("DownloadTemplate").Click += async (sender, args) => await DownloadTemplate();
            Find<Button>("ReleaseNotes").Click += (sender, args) => RunUiAction(() => Process.Start(SelectedRelease.ReleaseUrl));
            Find<Button>("OtherVersions").Click += (sender, args) => RunUiAction(() => Process.Start(TemplateRelease.ReleasesUrl));
            Find<TextBlock>("StudioVersion").Text = "AppDeploy Studio " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Find<TextBlock>("StudioAuthor").Text = AuthorName;
            Find<Button>("AuthorGitHub").Content = "github.com/" + new Uri(AuthorGitHub).AbsolutePath.TrimStart('/') + " ↗";
            Find<Button>("AuthorGitHub").Click += (sender, args) => RunUiAction(() => Process.Start(AuthorGitHub));
            Find<Button>("ProjectGitHub").Click += (sender, args) => RunUiAction(() => Process.Start(ProjectGitHub));
            Find<Button>("ToolkitGitHub").Click += (sender, args) => RunUiAction(() => Process.Start(TemplateRelease.ReleasesUrl));
            var language = Find<ComboBox>("InterfaceLanguage");
            language.SelectedIndex = Localization.SavedIndex();
            ChangeLanguage(false);
            language.SelectionChanged += (sender, args) => ChangeLanguage(true);
            var pages = Find<TabControl>("Pages");
            pages.SelectionChanged += (sender, args) =>
            {
                if (args.OriginalSource == pages)
                    Find<Border>("PackageFooter").Visibility = pages.SelectedItem == Find<TabItem>("TemplatesPage") ||
                        pages.SelectedItem == Find<TabItem>("AboutPage")
                        ? Visibility.Collapsed : Visibility.Visible;
            };
            Window.ContentRendered += (sender, args) => Find<TextBox>("AppName").Focus();
            Window.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(ClearPreview));
            Window.AddHandler(ComboBox.SelectionChangedEvent, new SelectionChangedEventHandler(ClearPreview));
            Window.AddHandler(CheckBox.CheckedEvent, new RoutedEventHandler(ClearPreview));
            Window.AddHandler(CheckBox.UncheckedEvent, new RoutedEventHandler(ClearPreview));
        }

        private T Find<T>(string name) where T : class
        {
            return (T)Window.FindName(name);
        }

        private TemplateRelease SelectedRelease
        {
            get { return (TemplateRelease)Find<ComboBox>("TemplateVersion").SelectedItem; }
        }

        private void ChangeLanguage(bool save)
        {
            Localization.Apply(Window, Find<ComboBox>("InterfaceLanguage").SelectedIndex, save);
            install.Localize();
            uninstall.Localize();
            repair.Localize();
            UpdateTemplateDetails();
            if (statusKey != null)
                SetStatus(statusKey, statusArguments);
            if (previewKey != null)
                Find<TextBox>("Preview").Text = Localization.Get(previewKey);
            if (downloadKey != null)
                SetDownloadStatus(downloadKey, downloadArguments);
        }

        private void UpdateTemplateDetails()
        {
            Find<TextBlock>("TemplateDetails").Text = Localization.Get(SelectedRelease.StatusKey) + "  ·  " + SelectedRelease.FileName;
        }

        private void SetDownloadStatus(string key, params object[] arguments)
        {
            downloadKey = key;
            downloadArguments = arguments;
            Find<TextBlock>("DownloadStatus").Text = Localization.Get(key, arguments);
        }

        private async Task DownloadTemplate()
        {
            var release = SelectedRelease;
            string extension = Path.GetExtension(release.FileName);
            string pattern = "*" + extension;
            var save = new SaveFileDialog
            {
                Title = Localization.Get("Download"), FileName = release.FileName,
                DefaultExt = extension, Filter = extension.Substring(1).ToUpperInvariant() + " (" + pattern + ")|" + pattern,
                AddExtension = true, OverwritePrompt = true
            };
            if (save.ShowDialog(Window) != true)
                return;

            var button = Find<Button>("DownloadTemplate");
            var versions = Find<ComboBox>("TemplateVersion");
            var progress = Find<ProgressBar>("DownloadProgress");
            var language = Find<ComboBox>("InterfaceLanguage");
            try
            {
                downloading = true;
                button.IsEnabled = versions.IsEnabled = language.IsEnabled = false;
                progress.Visibility = Visibility.Visible;
                SetDownloadStatus("Downloading", release.FileName);
                await release.Download(save.FileName, File.Exists(save.FileName));
                SetDownloadStatus("Downloaded", save.FileName);
            }
            catch (Exception error)
            {
                SetDownloadStatus("DownloadError", error.Message);
            }
            finally
            {
                downloading = false;
                button.IsEnabled = versions.IsEnabled = true;
                language.IsEnabled = Find<Button>("Generate").IsEnabled;
                progress.Visibility = Visibility.Collapsed;
            }
        }

        private void ClearPreview(object sender, RoutedEventArgs args)
        {
            if (args.OriginalSource is TabControl || args.OriginalSource == Find<TextBox>("Preview") ||
                args.OriginalSource == Find<TextBox>("Commands") || args.OriginalSource == Find<ComboBox>("InterfaceLanguage") ||
                args.OriginalSource == Find<ComboBox>("TemplateVersion") || !Find<Button>("CopyCommands").IsEnabled)
                return;
            Find<Button>("CopyCommands").IsEnabled = false;
            Find<TextBox>("Commands").Clear();
            previewKey = "StalePreview";
            Find<TextBox>("Preview").Text = Localization.Get(previewKey);
        }

        private void BrowseFolder(string field)
        {
            RunUiAction(() =>
            {
                using (var picker = new Forms.FolderBrowserDialog())
                {
                    bool exporting = field == "Destination";
                    var target = Find<TextBox>(field);
                    picker.Description = Localization.Get(exporting ? "PickFolder" : "PickPayload");
                    picker.ShowNewFolderButton = exporting;
                    string initial = target.Text.Trim();
                    if (exporting && Path.IsPathRooted(initial))
                        initial = Path.GetDirectoryName(initial.TrimEnd('\\')) ?? initial;
                    if (Directory.Exists(initial))
                        picker.SelectedPath = initial;
                    if (picker.ShowDialog(new DialogOwner(Window)) != Forms.DialogResult.OK)
                        return;
                    if (!exporting)
                        target.Text = picker.SelectedPath;
                    else
                    {
                        string name = Text("AppName") + (Text("Version").Length == 0 ? "" : "_" + Text("Version"));
                        foreach (char invalid in Path.GetInvalidFileNameChars())
                            name = name.Replace(invalid, '_');
                        name = name.Trim(' ', '.');
                        target.Text = Path.Combine(picker.SelectedPath, name.Length == 0 ? "Package-PSADT" : name);
                    }
                }
            });
        }

        private async Task UpdatePackage(bool export)
        {
            var pages = Find<TabControl>("Pages");
            var previewButton = Find<Button>("RefreshPreview");
            var generateButton = Find<Button>("Generate");
            var language = Find<ComboBox>("InterfaceLanguage");
            try
            {
                PackageSettings settings = ReadSettings();
                pages.IsEnabled = previewButton.IsEnabled = generateButton.IsEnabled = language.IsEnabled = false;
                SetStatus(export ? "Building" : "Preparing");
                var plan = await Task.Run(() => PackageBuilder.Prepare(settings));
                if (export)
                    lastOutput = await Task.Run(() => PackageBuilder.Export(plan));
                pages.SelectedIndex = 3;
                Find<TextBox>("Commands").Text = plan.Commands;
                previewKey = null;
                Find<TextBox>("Preview").Text = plan.Script;
                Find<Button>("CopyCommands").IsEnabled = true;
                if (export)
                    Find<Button>("OpenOutput").Visibility = Visibility.Visible;
                if (export)
                    SetStatus("Built", lastOutput);
                else
                    SetStatus("PreviewReady", plan.Files.Count);
            }
            catch (Exception error)
            {
                statusKey = null;
                DisplayStatus(error.Message.Split('\n')[0], true);
                MessageBox.Show(Window, error.Message, "AppDeploy Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                pages.IsEnabled = previewButton.IsEnabled = generateButton.IsEnabled = true;
                language.IsEnabled = !downloading;
            }
        }

        private bool Checked(string field)
        {
            return Find<CheckBox>(field).IsChecked == true;
        }

        private string Text(string field)
        {
            return Find<TextBox>(field).Text.Trim();
        }

        private string Selection(string field)
        {
            return (string)((ComboBoxItem)Find<ComboBox>(field).SelectedItem).Content;
        }

        private PackageSettings ReadSettings()
        {
            int deferrals;
            if (!int.TryParse(Text("Deferrals"), NumberStyles.None, CultureInfo.InvariantCulture, out deferrals) || deferrals > 99)
                throw new ArgumentException(Localization.Get("InvalidDeferrals"));
            return new PackageSettings
            {
                Name = Text("AppName"), Vendor = Text("Vendor"), Version = Text("Version"),
                Architecture = Find<ComboBox>("Architecture").SelectedIndex == 0 ? "" : Selection("Architecture"),
                Language = Text("Language"), Revision = Text("Revision"), Author = Text("Author"), Title = Text("CustomTitle"),
                Processes = Text("Processes"), SuccessCodes = Text("Success"), RebootCodes = Text("Reboot"), Deferrals = deferrals,
                Style = Selection("DialogStyle"), Mode = Selection("Mode"),
                Payload = Text("Payload"), Destination = Text("Destination"),
                Admin = Checked("RequireAdmin"), Progress = Checked("Progress"), SuppressReboot = Checked("Suppress"),
                Install = install.Value, Uninstall = uninstall.Value, Repair = repair.Value
            };
        }

        private void RunUiAction(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                MessageBox.Show(Window, error.Message, "AppDeploy Studio", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void SetStatus(string key, params object[] arguments)
        {
            statusKey = key;
            statusArguments = arguments;
            DisplayStatus(Localization.Get(key, arguments), false);
        }

        private void DisplayStatus(string message, bool error)
        {
            var status = Find<TextBlock>("Status");
            status.Text = message;
            status.ToolTip = message;
            status.Foreground = error ? Brushes.Firebrick : (Brush)Window.Resources["Muted"];
            status.MaxHeight = 59;
            status.TextTrimming = TextTrimming.CharacterEllipsis;
        }
    }

    internal sealed class ActionEditor
    {
        private readonly ComboBox type = new ComboBox();
        private readonly TextBox source = new TextBox();
        private readonly TextBox arguments = new TextBox();
        private readonly Expander section;
        private readonly Label typeLabel, sourceLabel, argsLabel;
        private readonly Button browse;
        private readonly string action;
        private readonly bool required;

        private string SelectedType { get { return (string)((ComboBoxItem)type.SelectedItem).Tag; } }

        internal DeploymentAction Value
        {
            get { return new DeploymentAction { Type = SelectedType, Source = source.Text.Trim(), Arguments = arguments.Text.Trim() }; }
        }

        internal ActionEditor(Window window, Panel host, string action, bool required)
        {
            this.action = action;
            this.required = required;
            section = new Expander { IsExpanded = required, FontWeight = FontWeights.SemiBold };
            host.Children.Add(new Border { Child = section, CornerRadius = new CornerRadius(6), BorderBrush = (Brush)window.Resources["Line"],
                BorderThickness = new Thickness(1), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 10) });
            var fields = new StackPanel { Margin = new Thickness(18, 9, 4, 0) };
            fields.SetValue(TextBlock.FontWeightProperty, FontWeights.Normal);
            section.Content = fields;
            if (!required)
                type.Items.Add(new ComboBoxItem { Tag = DeploymentAction.Disabled });
            type.Items.Add(new ComboBoxItem { Tag = "MSI" });
            type.Items.Add(new ComboBoxItem { Tag = "EXE" });
            if (!required)
                type.Items.Add(new ComboBoxItem { Tag = DeploymentAction.ProductCode });
            typeLabel = new Label { Target = type };
            fields.Children.Add(typeLabel);
            fields.Children.Add(type);

            sourceLabel = new Label { Target = source };
            var sourceRow = new DockPanel();
            browse = new Button { Style = (Style)window.Resources["PathButton"] };
            DockPanel.SetDock(browse, Dock.Right);
            sourceRow.Children.Add(browse);
            sourceRow.Children.Add(source);
            fields.Children.Add(sourceLabel);
            fields.Children.Add(sourceRow);
            argsLabel = new Label { Target = arguments };
            fields.Children.Add(argsLabel);
            fields.Children.Add(arguments);

            browse.Click += (sender, args) =>
            {
                var dialog = new OpenFileDialog { CheckFileExists = true, Title = Localization.Get(action) };
                dialog.Filter = SelectedType == "MSI" ? "MSI (*.msi)|*.msi" : "EXE (*.exe)|*.exe";
                if (dialog.ShowDialog(window) == true)
                    source.Text = dialog.FileName;
            };
            type.SelectionChanged += (sender, args) =>
            {
                bool inactive = SelectedType == DeploymentAction.Disabled;
                bool code = SelectedType == DeploymentAction.ProductCode;
                sourceLabel.Visibility = sourceRow.Visibility = argsLabel.Visibility = arguments.Visibility = inactive ? Visibility.Collapsed : Visibility.Visible;
                browse.Visibility = code ? Visibility.Collapsed : Visibility.Visible;
                UpdateLabels();
            };
            type.SelectedIndex = 0;
        }

        internal void Localize()
        {
            section.Header = Localization.Get(action) + (required ? " *" : "");
            foreach (ComboBoxItem item in type.Items)
            {
                string key = (string)item.Tag;
                item.Content = key == DeploymentAction.Disabled || key == DeploymentAction.ProductCode ? Localization.Get(key) : key;
            }
            typeLabel.Content = Localization.Get("ActionType");
            browse.Content = Localization.Get("Browse");
            UpdateLabels();
            AutomationProperties.SetName(type, Localization.Get("ActionType") + " · " + Localization.Get(action));
            AutomationProperties.SetName(source, (string)sourceLabel.Content + " · " + Localization.Get(action));
            AutomationProperties.SetName(arguments, (string)argsLabel.Content + " · " + Localization.Get(action));
        }

        private void UpdateLabels()
        {
            sourceLabel.Content = Localization.Get(SelectedType == DeploymentAction.ProductCode ? "ProductGuid" : "ActionFile");
            argsLabel.Content = Localization.Get(SelectedType == "EXE" ? "ExeArgs" : "MsiArgs");
        }
    }

    internal sealed class DialogOwner : Forms.IWin32Window
    {
        public IntPtr Handle { get; private set; }

        internal DialogOwner(Window window)
        {
            Handle = new WindowInteropHelper(window).Handle;
        }
    }
}
