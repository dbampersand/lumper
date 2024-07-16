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
        private static partial Regex MMPRegex();
        //struct for deserializing JSON objects
        private record Asset
        {
            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set;  }
            [JsonProperty("name")]
            public string Name { get; set;  }
        }
        //struct for deserializing JSON objects
        private record GHUpdate
        {
            [JsonProperty("tag_name")]
            public string TagName { get; set;  }
            [JsonProperty("assets")]
            public Asset[] Assets { get; set; }

        }
        //Major/Minor/Patch format
        public sealed record MMP(int major, int minor, int patch)
        {
            public int Major { get; } = major;
            public int Minor { get; } = minor;
            public int Patch { get; } = patch;
            public override string ToString() => $"{major}.{minor}.{patch}";
        }

        private static MMP? GetMMPVersion(string s)
        {
            //match pattern of xx.yy.zz
            Match match = MMPRegex().Match(s);
            if (match.Success)
            {
                GroupCollection currentVersion = match.Groups;

                int major, minor, patch;

                int.TryParse(currentVersion[1].ToString(), out major);
                int.TryParse(currentVersion[2].ToString(), out minor);
                int.TryParse(currentVersion[3].ToString(), out patch);

                return new MMP(major, minor, patch);
            }
            throw new Exception("Could not parse Major/Minor/Patch version.");
            return null;

        }
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
        
        public static async Task<MMP?> CheckForUpdate()
        {
            GHUpdate assets;
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
            MMP? current;
            MMP? latest;
            try
            {
                current = GetMMPVersion(Assembly.GetExecutingAssembly().GetName().Version.ToString());
                latest = GetMMPVersion(assets.TagName);
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
        //returns the URL to the download link for the OS-specific version
        private static string GetPath(GHUpdate assets, string OS)
        {
            for (int i = 0; i < assets.Assets.Length; i++)
            {
                if (assets.Assets[i].Name.Contains(OS, StringComparison.CurrentCultureIgnoreCase))
                {
                    return assets.Assets[i].BrowserDownloadUrl;
                }

            }
            return null;
        }
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
        public static async ValueTask Update()
        {
            GHUpdate assets;
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

            MMP? latest;
            try
            {
                latest = GetMMPVersion(assets.TagName);
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
                    string fileURL = GetPath(assets, "linux");
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
                    string fileURL = GetPath(assets, "win");

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
