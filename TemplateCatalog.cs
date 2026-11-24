using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace AppDeployStudio
{
    internal sealed class TemplateRelease
    {
        internal const string ReleasesUrl = "https://github.com/PSAppDeployToolkit/PSAppDeployToolkit/releases";
        private static readonly HttpClient Client = CreateClient();

        internal static readonly TemplateRelease[] Catalog =
        {
            new TemplateRelease("V4 · 4.1.8 · ZIP", "4.1.8", "PSAppDeployToolkit_Template_v4.zip", "BuilderTemplate",
                "50cb8d32973fc7648060a48cad63912ecb5caca5a70754f37e83aa06bd380283"),
            new TemplateRelease("V3 · 4.1.8 · ZIP", "4.1.8", "PSAppDeployToolkit_Template_v3.zip", "Stable",
                "a0dc5b7df846b4524848530a2d6e36f6edfeec81ab5c09bf1afcb41fb80a2092"),
            new TemplateRelease("V3 · 3.10.2 · ZIP", "3.10.2", "PSAppDeployToolkit_3.10.2.zip", "Stable",
                "ce25a97f3a8a8bb7bea8ac848750c7aaFa2857cca702e258f12c2722320fcb74"),
            new TemplateRelease("V4 · 4.2.0 RC2 · 7Z", "4.2.0-rc2", "PSAppDeployToolkit_Template_v4_4.2.0-RC2.7z", "PreviewRelease",
                "e4833d6cf2d3b2599804ac0d0b5c65bb7a17b007c3af387400a0c978f3236e76"),
            new TemplateRelease("V3 · 4.2.0 RC2 · 7Z", "4.2.0-rc2", "PSAppDeployToolkit_Template_v3_4.2.0-RC2.7z", "PreviewRelease",
                "02dba5a763d209959c5366bcd858864b243f34c1bdeb857ca8f43c2b18e55d56"),
            new TemplateRelease("V4 ZeroConfig · 4.2.0 RC2 · 7Z", "4.2.0-rc2", "PSAppDeployToolkit_Template_v4_ZeroConfig_4.2.0-RC2.7z", "PreviewRelease",
                "6290c993f54178b3b334d05614ad245d123c7470d5503a012754ce70af1c9824")
        };

        private readonly string label, tag, checksum;
        internal readonly string FileName, StatusKey;
        internal string ReleaseUrl { get { return ReleasesUrl + "/tag/" + tag; } }

        private TemplateRelease(string label, string tag, string fileName, string statusKey, string checksum)
        {
            this.label = label;
            this.tag = tag;
            this.checksum = checksum;
            FileName = fileName;
            StatusKey = statusKey;
        }

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AppDeployStudio/" + typeof(TemplateRelease).Assembly.GetName().Version.ToString(3));
            return client;
        }

        internal async Task Download(string destination, bool replaceExisting)
        {
            string url = ReleasesUrl + "/download/" + tag + "/" + FileName;
            string temp = Path.Combine(Path.GetDirectoryName(destination), ".appdeploy-" + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                using (var response = await Client.GetAsync(url).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
                    {
                        await response.Content.CopyToAsync(output).ConfigureAwait(false);
                        output.Position = 0;
                        Verify(output);
                    }
                }

                if (replaceExisting)
                    File.Replace(temp, destination, null);
                else
                    File.Move(temp, destination);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        internal void Verify(Stream archive)
        {
            using (var sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(archive)).Replace("-", "");
                if (!string.Equals(checksum, actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Localization.Get("ChecksumError"));
            }
            archive.Position = 0;
        }

        public override string ToString()
        {
            return label;
        }
    }
}
