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
using MsBox.Avalonia.Enums;
using MsBox.Avalonia;

namespace Lumper.UI.Updater
{
    internal sealed partial class Updater
    {
        [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
        private static partial Regex VersionRegex();
        /// <summary>
        /// record for deserializing JSON objects 
        /// given in the GitHub JSON API response 
        /// </summary>
        private record Asset
        {
            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set;  }
            [JsonProperty("name")]
            public string Name { get; set;  }
        }
        /// <summary>
        ///  record for deserializing Github API response data
        ///  see https://api.github.com/repos/momentum-mod/lumper/releases for the full format
        /// </summary>

        private record GithubUpdate
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set;  }
            [JsonProperty("assets")]
            public Asset[] Assets { get; set; }

        }
        /// <summary>
        /// Major/Minor/Patch format
        /// </summary>
        /// <param name="major">First digits of the version format</param>
        /// <param name="minor">Second digits of the version format</param>
        /// <param name="patch">Third digits of the version format</param>
        public sealed record Version(int major, int minor, int patch)
        {
            public int Major { get; } = major;
            public int Minor { get; } = minor;
            public int Patch { get; } = patch;
            public override string ToString() => $"{major}.{minor}.{patch}";
        }
        /// <summary>
        /// Parses a string to find the major/minor/patch versioning
        /// </summary>
        /// <param name="s">String containing a version number following the format 'xx.yy.zz'</param>
        private static Version? GetVersionFromString(string s)
        {
            //match pattern of xx.yy.zz
            Match match = VersionRegex().Match(s);
            if (match.Success)
            {
                GroupCollection currentVersion = match.Groups;

                int major, minor, patch;

                int.TryParse(currentVersion[1].ToString(), out major);
                int.TryParse(currentVersion[2].ToString(), out minor);
                int.TryParse(currentVersion[3].ToString(), out patch);

                return new Version(major, minor, patch);
            }
            throw new Exception("Could not parse Major/Minor/Patch version.");
            return null;

        }
        /// <summary>
        /// Runs the command line (windows) or shell (linux) and passes a command to it. 
        /// </summary>
        /// <param name="command"></param>
        static void ExecuteCommand(string command)
        {
            int exitCode;
            ProcessStartInfo processInfo = new ProcessStartInfo();
            Process process;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                processInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                processInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
                    { CreateNoWindow = true, UseShellExecute = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };

            }
            process = Process.Start(processInfo);
        }
        /// <summary>
        /// Grab the JSON from the Github API and deserializes it
        /// </summary>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private static async Task<GithubUpdate> GetGithubUpdates()
        {
            HttpClient client = new HttpClient();

            var request = new HttpRequestMessage() {
                RequestUri = new Uri("https://api.github.com/repos/momentum-mod/lumper/releases"),
                Method = HttpMethod.Get
            };
            client.DefaultRequestHeaders.Add("User-Agent", "Other");
            HttpResponseMessage response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                string jsonString = await response.Content.ReadAsStringAsync();

                return JsonConvert.DeserializeObject<GithubUpdate>(JsonObject.Parse(jsonString)[0].ToString());
            }
            else
                throw new Exception("Could not connect - error " + response.StatusCode);
        }
        /// <summary>
        /// Checks for possible update on the Github releases page by the tag name
        /// </summary>
        /// <returns>The current latest version, or null if it the latest</returns>
        public static async Task<Version?> CheckForUpdate()
        {
            GithubUpdate assets;
            try
            {
                assets = await GetGithubUpdates();
            } catch (Exception ex)
            {
                ButtonResult result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Error",
                    ex.Message, ButtonEnum.Ok)
                .ShowWindowDialogAsync(Program.Desktop.MainWindow);
                return null;
            }

            //parse tag name to find the current and latest version
            //finding the format of xx.yy.zz
            Version? current;
            Version? latest;
            try
            {
                current = GetVersionFromString(Assembly.GetExecutingAssembly().GetName().Version.ToString());
                latest = GetVersionFromString(assets.TagName);
            } catch (Exception ex)
            {
                ButtonResult result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Error",
                    "Could not parse version number.", ButtonEnum.Ok)
                .ShowWindowDialogAsync(Program.Desktop.MainWindow);
                return null;

            }
            if (current != latest)
                return latest;
            else
                return null;
        }
        /// <summary>
        /// Returns the URL to the download link for the OS-specific version.
        /// </summary>
        /// <returns></returns>
        private static string GetPath(GithubUpdate assets, OSPlatform OS)
        {
            string operatingSystem = "";

            if (OS == OSPlatform.Windows)
                operatingSystem = "win";
            else if (OS == OSPlatform.Linux)
                operatingSystem = "linux";
            else
                throw new Exception("Unsupported OS: "+ nameof(OS));

            foreach (Asset asset in assets.Assets)
            {
                if (asset.Name.Contains(operatingSystem, StringComparison.OrdinalIgnoreCase))
                {
                    return asset.BrowserDownloadUrl;
                }

            }
            throw new Exception("Could not find download link for " + nameof(OS));
        }

        /// <summary>
        /// Downloads a file from the given URI and places it in fileName
        /// </summary>
         public static async Task DownloadFile(Uri uri, HttpClient client, string fileName)
        { 
            using (var stream = await client.GetStreamAsync(uri))
            {
                if (File.Exists(fileName))
                    File.Delete(fileName);
                using (var fileStream = new FileStream(fileName, FileMode.CreateNew))
                {
                    await stream.CopyToAsync(fileStream);
                }
            }
        }
        /// <summary>
        /// Downloads an update for the program, applies it, and then restarts itself with the new version
        /// </summary>
        /// <returns></returns>
        public static async ValueTask Update()
        {
            GithubUpdate assets;
            try
            {
                assets = await GetGithubUpdates();

                
            } catch (Exception ex)
            {
                ButtonResult result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Error",
                    ex.Message, ButtonEnum.Ok)
                .ShowWindowDialogAsync(Program.Desktop.MainWindow);
                return;
            }

            Version? latest;
            try
            {
                latest = GetVersionFromString(assets.TagName);
            } catch (Exception ex)
            {
                ButtonResult result = await MessageBoxManager
                .GetMessageBoxStandard(
                    "Error",
                    "Could not parse version number.", ButtonEnum.Ok)
                .ShowWindowDialogAsync(Program.Desktop.MainWindow);
                return;
            }

            HttpClient client = new HttpClient();
            //NOTE: linux is untested
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                { 
                    string fileURL = GetPath(assets, OSPlatform.Linux);
                    string fileName = "linux_" + latest.major + "." + latest.minor + "." + latest.patch + ".zip";
                    string directoryName = fileName + "temp";

                    //download and unzip to a temp directory
                    await DownloadFile(new Uri(fileURL), client, fileName);

                    if (Directory.Exists(directoryName))
                    {
                        Directory.Delete(directoryName, true);
                    }

                    System.IO.Compression.ZipFile.ExtractToDirectory(fileName, directoryName);

                    string currentDirectory = System.IO.Directory.GetCurrentDirectory();

                    //wait 2 seconds for the process to fully exit before
                    //copying files from the temp directory to the root directory,
                    //then delete the temp directory and run the program again
                    string command =
                        $@"
                        sleep 2
                        && yes | cp -rf ""{currentDirectory}\{directoryName}"" 
                        && rm ""{currentDirectory}\{fileName}""  
                        && rm -rf ""{currentDirectory}\{directoryName}"" 
                        && ./Lumper.UI
                        ".Replace(Environment.NewLine, " ").Replace("\n", " ");

                    ExecuteCommand(command);

                    //exit so we can overwrite the executable
                    System.Environment.Exit(0);
                } catch (Exception ex)
                {
                    ButtonResult result = await MessageBoxManager
                    .GetMessageBoxStandard(
                        "Error",
                        "IO error: " + ex.Message, ButtonEnum.Ok)
                    .ShowWindowDialogAsync(Program.Desktop.MainWindow);
                    return;
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    string fileURL = GetPath(assets, OSPlatform.Windows);

                    string fileName = "windows_" + latest.major + "." + latest.minor + "." + latest.patch + ".zip";
                    string directoryName = fileName + "temp";
                    //download and unzip to a temp directory
                    await DownloadFile(new Uri(fileURL), client, fileName);

                    if (Directory.Exists(directoryName))
                    {
                        Directory.Delete(directoryName, true);
                    }
                    System.IO.Compression.ZipFile.ExtractToDirectory(fileName, directoryName);

                    string currentDirectory = System.IO.Directory.GetCurrentDirectory();

                    //wait 2 seconds for the process to fully exit before
                    //copying files from the temp directory to the root directory,
                    //then delete the temp directory and run the program again
                    string command =
                        $@"
                        sleep 2
                        && xcopy /s /Y ""{currentDirectory}\{directoryName}"" 
                        && rm ""{currentDirectory}\{fileName}""  
                        && rmdir /s /q ""{currentDirectory}\{directoryName}"" 
                        && Lumper.UI.exe
                        ".Replace(Environment.NewLine, " ").Replace("\n", " ");

                    ExecuteCommand(command);

                    //exit so we can overwrite the executable
                    System.Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    ButtonResult result = await MessageBoxManager
                    .GetMessageBoxStandard(
                        "Error",
                        "IO error: "+ex.Message, ButtonEnum.Ok)
                    .ShowWindowDialogAsync(Program.Desktop.MainWindow);
                    return;
                }
            }




        }
    }
}
