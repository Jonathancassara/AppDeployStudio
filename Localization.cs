using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;

namespace AppDeployStudio
{
    internal static class Localization
    {
        private static readonly string[] Codes = { "fr", "en", "pl", "uk" };
        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppDeployStudio", "language.txt");
        private static readonly Dictionary<string, string[]> Strings = ReadStrings();
        internal static int Index { get; private set; }

        private static Dictionary<string, string[]> ReadStrings()
        {
            var strings = new Dictionary<string, string[]>(StringComparer.Ordinal);
            using (var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Translations.txt")))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                        continue;
                    var cells = line.Split('|');
                    if (cells.Length != Codes.Length + 1)
                        throw new InvalidDataException("Invalid translation entry: " + cells[0]);
                    strings.Add(cells[0], cells);
                }
            }
            return strings;
        }

        internal static int SavedIndex()
        {
            try { return Math.Max(0, Array.IndexOf(Codes, File.ReadAllText(SettingsFile).Trim())); }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        internal static void Apply(Window window, int index, bool save)
        {
            Index = index;
            foreach (var entry in Strings)
                window.Resources["L_" + entry.Key] = Get(entry.Key);
            if (!save)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, Codes[index]);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string Get(string key, params object[] arguments)
        {
            string value = Strings[key][Index + 1].Replace("\\n", "\n");
            return arguments.Length == 0 ? value : string.Format(value, arguments);
        }
    }
}
