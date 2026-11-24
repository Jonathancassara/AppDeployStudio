using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AppDeployStudio
{
    internal sealed class DeploymentAction
    {
        internal const string Disabled = "Disabled", ProductCode = "ProductCode";
        internal string Type, Source, Arguments;
    }

    internal sealed class PackageSettings
    {
        internal string Name, Vendor, Version, Architecture, Language, Revision, Author, Title;
        internal string Processes, SuccessCodes, RebootCodes, Style, Mode, Payload, Destination;
        internal bool Admin, Progress, SuppressReboot;
        internal int Deferrals;
        internal DeploymentAction Install, Uninstall, Repair;
    }

    internal sealed class PackagePlan
    {
        internal string Script, Config, Commands, Destination, TemplatePath;
        internal readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class PackageBuilder
    {
        internal const string ScriptName = "Invoke-AppDeployToolkit.ps1";
        internal const string ConfigName = "Config/config.psd1";
        private static readonly UTF8Encoding PowerShellEncoding = new UTF8Encoding(true);

        private static async Task<string> FindTemplate()
        {
            var release = TemplateRelease.Catalog[0];
            string besideApp = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), release.FileName);
            if (File.Exists(besideApp))
                return besideApp;

            string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AppDeployStudio", "Templates", "4.1.8");
            Directory.CreateDirectory(cache);
            string archive = Path.Combine(cache, release.FileName);
            if (!File.Exists(archive))
            {
                try { await release.Download(archive, false).ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (!File.Exists(archive))
                        throw new IOException(Localization.Get("DownloadError", error.Message), error);
                }
            }
            return archive;
        }

        private static ZipArchive OpenTemplate(string path)
        {
            var stream = File.OpenRead(path);
            try
            {
                TemplateRelease.Catalog[0].Verify(stream);
                return new ZipArchive(stream, ZipArchiveMode.Read);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static string ReadEntry(ZipArchive template, string name)
        {
            using (var reader = new StreamReader(template.GetEntry(name).Open()))
                return reader.ReadToEnd().Replace("\r\n", "\n");
        }

        internal static async Task<PackagePlan> Prepare(PackageSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Name))
                throw new ArgumentException(Localization.Get("MissingName"));
            if (string.IsNullOrWhiteSpace(settings.Destination))
                throw new ArgumentException(Localization.Get("MissingOutput"));

            var plan = new PackagePlan { Destination = AbsolutePath(settings.Destination) };
            if (Directory.Exists(plan.Destination) || File.Exists(plan.Destination))
                throw new ArgumentException(Localization.Get("OutputExists"));

            string payload = string.IsNullOrWhiteSpace(settings.Payload) ? "" : AbsolutePath(settings.Payload);
            if (payload.Length > 0)
            {
                if (!Directory.Exists(payload))
                    throw new ArgumentException(Localization.Get("MissingPayload"));
                if (Within(plan.Destination, payload) || string.Equals(plan.Destination, payload, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(Localization.Get("NestedOutput"));
                AddPayload(plan, payload, payload);
            }

            string install = BuildAction(plan, payload, "Install", settings.Install);
            string uninstall = BuildAction(plan, payload, "Uninstall", settings.Uninstall);
            string repair = BuildAction(plan, payload, "Repair", settings.Repair);
            plan.TemplatePath = await FindTemplate().ConfigureAwait(false);
            using (var template = OpenTemplate(plan.TemplatePath))
            {
                string script = ReadEntry(template, ScriptName);
                script = SetValue(script, "AppName", Quote(settings.Name));
                script = SetValue(script, "AppVendor", Quote(settings.Vendor));
                script = SetValue(script, "AppVersion", Quote(settings.Version));
                script = SetValue(script, "AppArch", Quote(settings.Architecture));
                script = SetValue(script, "AppLang", Quote(settings.Language));
                script = SetValue(script, "AppRevision", Quote(settings.Revision));
                script = SetValue(script, "AppScriptAuthor", Quote(settings.Author));
                script = SetValue(script, "AppScriptDate", Quote(DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                script = SetValue(script, "InstallTitle", Quote(settings.Title));
                script = SetValue(script, "RequireAdmin", settings.Admin ? "$true" : "$false");
                script = SetValue(script, "AppSuccessExitCodes", CodeArray(settings.SuccessCodes, Localization.Get("SuccessCodes")));
                script = SetValue(script, "AppRebootExitCodes", CodeArray(settings.RebootCodes, Localization.Get("RebootCodes")));
                var processes = Split(settings.Processes).Select(value => Regex.Replace(value, @"\.exe$", "", RegexOptions.IgnoreCase)).ToArray();
                if (processes.Any(value => value.Length == 0 || value.IndexOfAny(new[] { '\\', '/', '*', '?' }) >= 0))
                    throw new ArgumentException(Localization.Get("InvalidProcesses"));
                script = SetValue(script, "AppProcessesToClose", "@(" + string.Join(", ", processes.Select(Quote)) + ")");
                script = ReplaceSection(script, "    ## Show Welcome Message,", "    ## <Perform Pre-Installation", Welcome(settings));
                script = ReplaceSection(script, "    ## If there are processes to close", "    ## <Perform Pre-Uninstallation", Welcome(settings, false));
                script = ReplaceSection(script, "    ## If there are processes to close", "    ## <Perform Pre-Repair", Welcome(settings, false));
                script = ReplaceSection(script, "    ## Handle Zero-Config MSI installations.", "\n\n    ##================================================", install);
                script = ReplaceSection(script, "    ## Handle Zero-Config MSI uninstallations.", "\n\n    ##================================================", uninstall);
                script = ReplaceSection(script, "    ## Handle Zero-Config MSI repairs.", "\n\n    ##================================================", repair);
                script = ReplaceSection(script, "    ## Display a message at the end of the install.", "\n}", "");
                plan.Script = WindowsLines(script);
                plan.Config = WindowsLines(SetValue(ReadEntry(template, ConfigName), "DialogStyle", Quote(settings.Style)));
            }

            var commands = new List<string> { Command("Install", settings) };
            if (settings.Uninstall.Type != DeploymentAction.Disabled)
                commands.Add(Command("Uninstall", settings));
            if (settings.Repair.Type != DeploymentAction.Disabled)
                commands.Add(Command("Repair", settings));
            plan.Commands = string.Join(Environment.NewLine, commands);
            return plan;
        }

        private static string AbsolutePath(string value)
        {
            if (!Regex.IsMatch(value, @"^(?:[a-zA-Z]:[\\/]|\\\\[^\\]+\\[^\\]+)") || value.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                value.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new ArgumentException(Localization.Get("InvalidPath", value));
            string fullPath = Path.GetFullPath(value);
            return fullPath.Length == Path.GetPathRoot(fullPath).Length ? fullPath : fullPath.TrimEnd('\\', '/');
        }

        private static bool Within(string path, string folder)
        {
            return path.StartsWith(folder.TrimEnd('\\', '/') + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddPayload(PackagePlan plan, string root, string folder)
        {
            RejectLink(folder);
            foreach (string file in Directory.EnumerateFiles(folder))
                AddFile(plan, file.Substring(root.TrimEnd('\\').Length + 1), file);
            foreach (string child in Directory.EnumerateDirectories(folder))
                AddPayload(plan, root, child);
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException(Localization.Get("LinkedFile", path));
        }

        private static void AddFile(PackagePlan plan, string relative, string source)
        {
            RejectLink(source);
            string existing;
            if (plan.Files.TryGetValue(relative, out existing) && !string.Equals(existing, source, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Localization.Get("DuplicateFile", relative));
            plan.Files[relative] = source;
        }

        private static string BuildAction(PackagePlan plan, string payload, string action, DeploymentAction input)
        {
            if (input.Type == DeploymentAction.Disabled)
                return "    throw 'Action " + action + " is not configured for this package.'";

            if (input.Type == DeploymentAction.ProductCode)
            {
                Guid productCode;
                if (!Guid.TryParse(input.Source, out productCode) || productCode == Guid.Empty)
                    throw new ArgumentException(Localization.Get("InvalidGuid", Localization.Get(action)));
                return "    Start-ADTMsiProcess -Action " + action + " -ProductCode " +
                    Quote(productCode.ToString("B").ToUpperInvariant()) + Arguments(input.Arguments, true);
            }

            string extension = input.Type == "MSI" ? ".msi" : ".exe";
            if (string.IsNullOrWhiteSpace(input.Source))
                throw new ArgumentException(Localization.Get("MissingActionFile", extension, Localization.Get(action)));
            string file = AbsolutePath(input.Source);
            if (!File.Exists(file) || !string.Equals(Path.GetExtension(file), extension, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(Localization.Get("InvalidActionFile", extension, Localization.Get(action), file));
            string relative = payload.Length > 0 && Within(file, payload)
                ? file.Substring(payload.TrimEnd('\\').Length + 1)
                : Path.GetFileName(file);
            AddFile(plan, relative, file);

            string command = "    $installer = Join-Path -Path $adtSession.DirFiles -ChildPath " + Quote(relative) + "\n";
            if (input.Type == "MSI")
                return command + "    Start-ADTMsiProcess -Action " + action + " -FilePath $installer" + Arguments(input.Arguments, true);
            return command + "    Start-ADTProcess -FilePath $installer -WorkingDirectory (Split-Path -LiteralPath $installer)" + Arguments(input.Arguments, false);
        }

        private static string Arguments(string value, bool msi)
        {
            return value.Length == 0 ? "" : (msi ? " -AdditionalArgumentList " : " -ArgumentList ") + Quote(value);
        }

        private static string Welcome(PackageSettings settings, bool install = true)
        {
            var commands = new List<string>();
            var processOption = "-CloseProcesses $adtSession.AppProcessesToClose";
            if (install && settings.Deferrals > 0)
            {
                string command = "    Show-ADTInstallationWelcome -AllowDefer -DeferTimes " + settings.Deferrals.ToString(CultureInfo.InvariantCulture);
                commands.Add(command + (Split(settings.Processes).Any() ? " " + processOption : ""));
            }
            else if (Split(settings.Processes).Any())
                commands.Add("    Show-ADTInstallationWelcome " + processOption + " -CloseProcessesCountdown 60");
            if (settings.Progress)
                commands.Add("    Show-ADTInstallationProgress");
            return commands.Count == 0 ? "" : string.Join("\n", commands) + "\n\n";
        }

        private static string Command(string action, PackageSettings settings)
        {
            return "Invoke-AppDeployToolkit.exe -DeploymentType " + action + " -DeployMode " + settings.Mode +
                (settings.SuppressReboot ? " -SuppressRebootPassThru" : "");
        }

        private static IEnumerable<string> Split(string input)
        {
            return input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim()).Where(value => value.Length > 0);
        }

        private static string CodeArray(string input, string field)
        {
            var codes = new List<int>();
            foreach (string value in Split(input))
            {
                int code;
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out code))
                    throw new ArgumentException(Localization.Get("InvalidCodes", field));
                codes.Add(code);
            }
            if (codes.Count == 0)
                throw new ArgumentException(Localization.Get("MissingCodes", field));
            return "@(" + string.Join(", ", codes.Distinct().Select(code => code.ToString(CultureInfo.InvariantCulture))) + ")";
        }

        private static string Quote(string value)
        {
            // PowerShell treats typographic single quotation marks as delimiters too.
            return "'" + Regex.Replace(value, "['\u2018\u2019\u201A\u201B]", match => match.Value + match.Value) + "'";
        }

        private static string SetValue(string text, string key, string value)
        {
            string pattern = @"(?m)^([ \t]+" + Regex.Escape(key) + @"[ \t]*=[ \t]*)[^\n]*";
            var expression = new Regex(pattern);
            if (expression.Matches(text).Count != 1)
                throw new InvalidDataException(Localization.Get("TemplateField", key));
            return expression.Replace(text, match => match.Groups[1].Value + value);
        }

        private static string ReplaceSection(string text, string first, string next, string replacement)
        {
            int start = text.IndexOf(first, StringComparison.Ordinal);
            int end = start < 0 ? -1 : text.IndexOf(next, start + first.Length, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidDataException(Localization.Get("TemplateSection", first.Trim()));
            return text.Substring(0, start) + replacement + text.Substring(end);
        }

        private static string WindowsLines(string value)
        {
            return value.Replace("\n", "\r\n");
        }

        internal static string Export(PackagePlan plan)
        {
            string parent = Path.GetDirectoryName(plan.Destination);
            Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".appdeploy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                using (var template = OpenTemplate(plan.TemplatePath))
                    template.ExtractToDirectory(staging);
                string files = Path.Combine(staging, "Files");
                string placeholder = Path.Combine(files, "Add Setup Files Here.txt");
                File.Delete(placeholder);
                foreach (var payload in plan.Files)
                {
                    string output = Path.Combine(files, payload.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    File.Copy(payload.Value, output, false);
                }
                File.WriteAllText(Path.Combine(staging, ScriptName), plan.Script, PowerShellEncoding);
                File.WriteAllText(Path.Combine(staging, ConfigName), plan.Config, PowerShellEncoding);
                File.WriteAllText(Path.Combine(staging, "Commandes.txt"), plan.Commands + Environment.NewLine, PowerShellEncoding);
                Directory.Move(staging, plan.Destination);
                return plan.Destination;
            }
            finally
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, true);
            }
        }
    }
}
