using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Lumper.UI.ViewModels;
using Avalonia.Controls;
using System.IO;

namespace Lumper.UI.Updater
{
    internal sealed class Updater
    {
        //struct for deserializing JSON objects
        private struct Asset
        {
            [JsonProperty("browser_download_url")]
            public string browser_download_url;
            [JsonProperty("name")]
            public string name;
        }
        //struct for deserializing JSON objects
        private struct GHUpdate
        {
            [JsonProperty("tag_name")]
            public string tag_name;
            [JsonProperty("assets")]
            public Asset[] assets;


        }
        //Major/Minor/Patch format
        public sealed class MMP
        {
            public int major;
            public int minor;
            public int patch;
            public MMP(int Major, int Minor, int Patch)
            {
                major = Major;
                minor = Minor;
                patch = Patch;
            }
        }

        private static MMP GetMMPVersion(string s)
        {
            //match pattern of xx.yy.zz
            Match match = Regex.Match(s, "[0-9]+\\.[0-9]+\\.[0-9]+");
            MMP version = new MMP(0, 0, 0);
            if (match.Success)
            {
                MatchCollection currentVersion = Regex.Matches(match.ToString(), "[0-9]+");


                if (int.TryParse(currentVersion[0].ToString(), out version.major)
                    && int.TryParse(currentVersion[1].ToString(), out version.minor)
                    && int.TryParse(currentVersion[2].ToString(), out version.patch))
                {
                }
                else
                {
                    throw new Exception("Could not parse Major/Minor/Patch version.");
                }

            }
            return version;

        }
        static void ExecuteCommand(string command)
        {
            int exitCode;
            ProcessStartInfo processInfo = new ProcessStartInfo();
            Process process;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                processInfo = new ProcessStartInfo("cmd.exe", "/c " + command);
                processInfo.CreateNoWindow = true;
                processInfo.UseShellExecute = false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                processInfo = new ProcessStartInfo(command);
                processInfo.CreateNoWindow = true;
                processInfo.UseShellExecute = true;
            }
            processInfo.RedirectStandardError = true;
            processInfo.RedirectStandardOutput = true;
            processInfo.WorkingDirectory = System.IO.Directory.GetCurrentDirectory();
            process = Process.Start(processInfo);
        }

        //Grab the JSON from the github API and deserialize
        private static async Task<GHUpdate> GetGithubUpdates()
        {
            HttpClient client = new HttpClient();

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri("https://api.github.com/repos/momentum-mod/lumper/releases"),
                Method = HttpMethod.Get
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Other");
            HttpResponseMessage m = client.Send(request);

            if (m.IsSuccessStatusCode)
            {
                string response = await m.Content.ReadAsStringAsync();

                var assets = JsonConvert.DeserializeObject<GHUpdate>(JsonObject.Parse(response)[0].ToString());
                return assets;
            }
            else
                throw new Exception("Could not connect - error " + m.StatusCode);
        }
        
        public static async Task<bool> CheckForUpdate()
        {
            GHUpdate assets = await GetGithubUpdates();

            //parse tag name to find the current and latest version
            //finding the format of xx.yy.zz
            string newestVersionSplit = Regex.Match(assets.tag_name, "[0-9]+\\.[0-9]+\\.[0-9]+").ToString();
            MMP current = GetMMPVersion(Assembly.GetExecutingAssembly().GetName().Version.ToString());
            MMP latest = GetMMPVersion(newestVersionSplit);

            return (current.major != latest.major || current.minor != latest.minor || current.patch != latest.patch);
        }
        //returns the URL to the download link for the OS-specific version
        private static string GetPath(GHUpdate assets, string OS)
        {
            for (int i = 0; i < assets.assets.Length; i++)
            {
                if (assets.assets[i].name.Contains(OS, StringComparison.CurrentCultureIgnoreCase))
                {
                    return assets.assets[i].browser_download_url;
                }

            }
            return null;
        }

        public static async ValueTask Update()
        {

            GHUpdate assets = await GetGithubUpdates();

            string newestVersionSplit = Regex.Match(assets.tag_name, "[0-9]+\\.[0-9]+\\.[0-9]+").ToString();
            MMP latest = GetMMPVersion(newestVersionSplit);


            //NOTE: linux is untested
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string fileURL = GetPath(assets, "linux");
                string fileName = "linux_" + latest.major + "." + latest.minor + "." + latest.patch + ".zip";
                string directoryName = fileName + "temp";

                //download and unzip to a temp directory
                WebClient webClient = new WebClient();
                webClient.DownloadFile(new Uri(fileURL), fileName);

                if (Directory.Exists(directoryName))
                {
                    Directory.Delete(directoryName, true);
                }

                System.IO.Compression.ZipFile.ExtractToDirectory(fileName, directoryName);

                //copy files from the temp directory to the root directory, then delete the temp directory and run the program again
                ExecuteCommand("yes | cp -rf  \"" + System.IO.Directory.GetCurrentDirectory() + "/" + fileName + "temp\"" + "&& rm \"" + fileName + "\" && rm -rf \"" + System.IO.Directory.GetCurrentDirectory() + "/" + directoryName + "\"" + " && ./Lumper.UI");

                //exit so we can overwrite the executable
                System.Environment.Exit(0);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                string fileURL = GetPath(assets, "win");

                string fileName = "windows_" + latest.major + "." + latest.minor + "." + latest.patch + ".zip";
                string directoryName = fileName + "temp";
                //download and unzip to a temp directory
                WebClient webClient = new WebClient();
                webClient.DownloadFile(new Uri(fileURL), fileName);

                if (Directory.Exists(directoryName))
                {
                    Directory.Delete(directoryName, true);
                }
                System.IO.Compression.ZipFile.ExtractToDirectory(fileName, directoryName);

                //copy files from the temp directory to the root directory, then delete the temp directory and run the program again
                ExecuteCommand("xcopy /s /Y \"" + System.IO.Directory.GetCurrentDirectory() + "\\" + fileName + "temp\"" + "&& rm \""+fileName+"\" && rmdir /s /q \"" + System.IO.Directory.GetCurrentDirectory() + "\\" + directoryName + "\"" + " && Lumper.UI.exe");

                //exit so we can overwrite the executable
                System.Environment.Exit(0);

            }




        }
    }
}
