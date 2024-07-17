namespace Lumper.UI.Updater;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HarfBuzzSharp;
using Lumper.Lib.BSP.IO;
using Lumper.UI.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Newtonsoft.Json;
using NLog;
using NLog.Targets;
using SharpCompress.Archives;
using SharpCompress.Common;

sealed partial class Updater
{
    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionRegex();

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const float DownloadProgressPercentage = 80;

    /// <summary>
    /// record for deserializing JSON objects 
    /// given in the GitHub JSON API response 
    /// </summary>
    private sealed record Asset
    {
        [JsonProperty("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
        [JsonProperty("name")]
        public string Name { get; set; }
    }
    /// <summary>
    ///  record for deserializing Github API response data
    ///  see https://api.github.com/repos/momentum-mod/lumper/releases for the full format
    /// </summary>

    private sealed record GithubUpdate
    {
        [JsonProperty("tag_name")]
        public string TagName { get; set; }
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

            return new Version(
            int.Parse(currentVersion[1].ToString()),
            int.Parse(currentVersion[2].ToString()),
            int.Parse(currentVersion[3].ToString()));
        }
        throw new Exception("Could not parse Major/Minor/Patch version.");

    }

    /// <summary>
    /// Runs the command line (windows) or shell (linux) and passes a command to it. 
    /// </summary>
    /// <param name="command"></param>
    private static void ExecuteCommand(string command)
    {
        int exitCode;
        var processInfo = new ProcessStartInfo();
        Process process;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            processInfo = new ProcessStartInfo("cmd.exe", "/c " + command) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Directory.GetCurrentDirectory() };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            processInfo = new ProcessStartInfo(command) { CreateNoWindow = true, UseShellExecute = true, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = Directory.GetCurrentDirectory() };

        }
        process = Process.Start(processInfo);
    }
    /// <summary>
    /// Grab the JSON from the Github API and deserializes it
    /// </summary>
    private static async Task<GithubUpdate> GetGithubUpdates()
    {
        var client = new HttpClient();

        var request = new HttpRequestMessage() {
            RequestUri = new Uri("https://api.github.com/repos/momentum-mod/lumper/releases"),
            Method = HttpMethod.Get
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Other");
        HttpResponseMessage response = await client.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var jsonString = await response.Content.ReadAsStringAsync();

            return JsonConvert.DeserializeObject<GithubUpdate>(JsonNode.Parse(jsonString)[0].ToString());
        }
        else
        {
            throw new HttpRequestException("Could not connect to Github API.",null,response.StatusCode);
        }
    }
    /// <summary>
    /// Checks for possible update on the Github releases page by the tag name
    /// </summary>
    /// <returns>A tuple where the first value defines if there is an update ready, and the second value is the latest Version</returns>
    public static async Task<Tuple<bool, Version>> CheckForUpdate()
    {
        GithubUpdate assets;
        assets = await GetGithubUpdates();

        //parse tag name to find the current and latest version
        //finding the format of xx.yy.zz
        Version current;
        Version latest;
        current = GetVersionFromString(Assembly.GetExecutingAssembly().GetName().Version.ToString());
        latest = GetVersionFromString(assets.TagName);

       return new Tuple<bool, Version>(current != latest, latest);
    }
    public static async Task<Stream?> HttpDownload(string url, string fileName, IoProgressWindow progressWindow, IoHandler handler, CancellationTokenSource cts)
    {
        //the amount the progress bar should go up to while downloading - we still have to unzip for the other percentage
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(80 * 1024);

        if (File.Exists(fileName))
            File.Delete(fileName);

        var stream = new FileStream(fileName, FileMode.CreateNew);
        try
        {

            using var httpClient = new HttpClient();
            using HttpResponseMessage response =
                await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            await using Stream downloadStream = await response.Content.ReadAsStreamAsync(cts.Token);

            if (response.Content.Headers.ContentLength is null)
            {
                handler.UpdateProgress(0, "Downloading (unknown length)");
                await downloadStream.CopyToAsync(stream, cts.Token);
            }
            else
            {
                int read;
                var length = (int)response.Content.Headers.ContentLength.Value;
                var remaining = length;
                while (!handler.Cancelled &&
                       (read = await downloadStream.ReadAsync(
                           buffer.AsMemory(0, int.Min(buffer.Length, remaining)),
                           cts.Token)) >
                       0)
                {
                    var prog = (float)read / length * DownloadProgressPercentage;
                    handler.UpdateProgress(prog, $"{float.Floor((1 - ((float)remaining / length)) * DownloadProgressPercentage)}%");
                    await stream.WriteAsync(buffer.AsMemory(0, read), cts.Token);
                    remaining -= read;
                }
            }
        }
        catch (Exception ex)
        {
            if (ex is TaskCanceledException)
                Logger.Info("Download cancelled by user");
            else
                Logger.Error(ex, "Failed to download!");

            await stream.DisposeAsync();
            return null;
        }
        finally
        {
            stream.Close();
        }

        if (handler.Cancelled)
            return null;

        return stream;
    }
    /// <summary>
    /// Returns the URL to the download link for the OS-specific version.
    /// </summary>
    /// <returns></returns>
    private static string GetPath(GithubUpdate assets, OSPlatform OS)
    {
        var operatingSystem = "";

        if (OS == OSPlatform.Windows)
            operatingSystem = "win";
        else if (OS == OSPlatform.Linux)
            operatingSystem = "linux";
        else
            throw new ApplicationException("Unsupported OS: " + OS);

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
    public static async Task DownloadFile(Uri uri, string fileName, IoProgressWindow progressWindow, IoHandler handler, CancellationTokenSource cts) => await HttpDownload(uri.AbsoluteUri, fileName, progressWindow,  handler, cts);
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

            Version? latest;
            latest = GetVersionFromString(assets.TagName);

            IoProgressWindow progressWindow;
            var cts = new CancellationTokenSource();
            var handler = new IoHandler(cts);

            //NOTE: linux is untested
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var fileURL = GetPath(assets, OSPlatform.Linux);
                var fileName = "linux_{latest}.zip";
                var directoryName = fileName + "temp";

                progressWindow = new IoProgressWindow {
                    Title = $"Downloading {fileURL}",
                    Handler = handler
                };
                _ = progressWindow.ShowDialog(Program.Desktop.MainWindow);


                //download and unzip to a temp directory
                await DownloadFile(new Uri(fileURL), fileName, progressWindow, handler, cts);
                if (Directory.Exists(directoryName))
                {
                    Directory.Delete(directoryName, true);
                }

                float progress = DownloadProgressPercentage / 100.0f;

                Directory.CreateDirectory(directoryName);
                List<Task> tasks = new List<Task>();
                List<FileStream> fileStreams = new List<FileStream>();
                using (ZipArchive archive = ZipFile.OpenRead(fileName))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        using (Stream zipStream = entry.Open())
                        {
                            float deltaProgress = 1 / (float)archive.Entries.Count * (100 - DownloadProgressPercentage);
                            progress += deltaProgress;
                            handler.UpdateProgress(deltaProgress, $"{float.Floor(DownloadProgressPercentage + progress)}%");

                            FileStream f = new FileStream($"{directoryName}/{entry.Name}", FileMode.CreateNew);
                            await zipStream.CopyToAsync(f);
                            f.Close();
                        }
                    }
                }


                var currentDirectory = Directory.GetCurrentDirectory();

                //wait 2 seconds for the process to fully exit before
                //copying files from the temp directory to the root directory,
                //then delete the temp directory and run the program again
                var command =
                    $@"
                    sleep 2
                    && yes | cp -rf ""{currentDirectory}\{directoryName}"" 
                    && rm ""{currentDirectory}\{fileName}""  
                    && rm -rf ""{currentDirectory}\{directoryName}"" 
                    && ./Lumper.UI
                    ".Replace(Environment.NewLine, " ").Replace("\n", " ");

                ExecuteCommand(command);

                //exit so we can overwrite the executable
                Environment.Exit(0);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var fileURL = GetPath(assets, OSPlatform.Windows);

                var fileName = "windows_{latest}.zip";
                var directoryName = fileName + "temp";

                progressWindow = new IoProgressWindow {
                    Title = $"Downloading {fileURL}",
                    Handler = handler
                };
                _ = progressWindow.ShowDialog(Program.Desktop.MainWindow);


                //await ShowProgressWindow(fileURL, handler);
                //download and unzip to a temp directory
                await DownloadFile(new Uri(fileURL), fileName, progressWindow, handler, cts);

                if (Directory.Exists(directoryName))
                {
                    Directory.Delete(directoryName, true);
                }

                float progress = DownloadProgressPercentage / 100.0f;

                Directory.CreateDirectory(directoryName);
                List<Task> tasks = new List<Task>();
                List<FileStream> fileStreams = new List<FileStream>();
                using (ZipArchive archive = ZipFile.OpenRead(fileName))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        using (Stream zipStream = entry.Open())
                        {
                            float deltaProgress = 1 / (float)archive.Entries.Count * (100 - DownloadProgressPercentage);
                            progress += deltaProgress;
                            handler.UpdateProgress(deltaProgress, $"{float.Floor(DownloadProgressPercentage + progress)}%");

                            FileStream f = new FileStream($"{directoryName}/{entry.Name}", FileMode.CreateNew);
                            await zipStream.CopyToAsync(f);
                            f.Close();
                        }
                    }
                }

                var currentDirectory = Directory.GetCurrentDirectory();

                //wait 2 seconds for the process to fully exit before
                //copying files from the temp directory to the root directory,
                //then delete the temp directory and run the program again
                var command =
                    $@"
                    sleep 2
                    && xcopy /s /Y ""{currentDirectory}\{directoryName}"" 
                    && rm ""{currentDirectory}\{fileName}""  
                    && rmdir /s /q ""{currentDirectory}\{directoryName}"" 
                    && Lumper.UI.exe
                    ".Replace(Environment.NewLine, " ").Replace("\n", " ");

                ExecuteCommand(command);

                //exit so we can overwrite the executable
                Environment.Exit(0);
            }



        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to download update");
        }
    }
}
