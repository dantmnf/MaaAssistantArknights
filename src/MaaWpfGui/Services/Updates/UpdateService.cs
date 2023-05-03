using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MaaWpfGui.Constants;
using MaaWpfGui.Helper;
using MaaWpfGui.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Semver;
using Stylet;

namespace MaaWpfGui.Services.Updates
{
    public sealed class UpdateService : INotifyPropertyChanged
    {
        [DllImport("MaaCore.dll")]
        private static extern IntPtr AsstGetVersion();

        private readonly string _curVersion = Marshal.PtrToStringAnsi(AsstGetVersion());

        public string Version => _curVersion;

        public UpdateStatus UpdateStatus { get; private set; } = UpdateStatus.None;

        public UpdateInfo UpdateInfo { get; private set; } = null;

        public long UpdateDownloadPackageSize { get; private set; }

        public long UpdateDownloadTransferredSize { get; private set; }

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Gets the OS architecture.
        /// </summary>
        public static string OSArchitecture => RuntimeInformation.OSArchitecture.ToString().ToLower();

        /// <summary>
        /// Gets a value indicating whether the OS is arm.
        /// </summary>
        public static bool IsArm => OSArchitecture.StartsWith("arm");

        private const string RequestUrl = "repos/MaaAssistantArknights/MaaRelease/releases";
        private const string StableRequestUrl = "repos/MaaAssistantArknights/MaaAssistantArknights/releases/latest";
        private const string MaaReleaseRequestUrlByTag = "repos/MaaAssistantArknights/MaaRelease/releases/tags/";
        private const string InfoRequestUrl = "repos/MaaAssistantArknights/MaaAssistantArknights/releases/tags/";

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChangedFreeThreaded(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // This will be picked up by PropertyChanged.Fody
        private void NotifyPropertyChanged(string propertyName)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null)
            {
                // post the event to dispatcher (returns immediately)
                dispatcher.BeginInvoke((Action)(() =>
                {
                    NotifyPropertyChangedFreeThreaded(propertyName);
                }), null);
            }
            else
            {
                NotifyPropertyChangedFreeThreaded(propertyName);
            }
        }

        /// <summary>
        /// Cancels the in-progress check or download.
        /// </summary>
        public void Cancel()
        {
            _cts.Cancel();
        }

        /// <summary>
        /// 检查更新。
        /// </summary>
        /// <param name="force">是否强制检查。</param>
        /// <returns>检查到更新返回 <see langword="true"/>，反之则返回 <see langword="false"/>。</returns>
        public async Task<CheckUpdateStatus> CheckUpdate(CancellationToken ct = default)
        {
            // 调试版不检查更新
            if (isDebugVersion())
            {
                return CheckUpdateStatus.FailedToGetInfo;
            }

            const int RequestRetryMaxTimes = 2;
            try
            {
                JObject latestJson;
                string latestVersion;
                if (!Instances.SettingsViewModel.UpdateBeta && !Instances.SettingsViewModel.UpdateNightly)
                {
                    // 稳定版更新使用主仓库 /latest 接口
                    // 直接使用 MaaRelease 的话，30 个可能会找不到稳定版，因为有可能 Nightly 发了很多
                    var stableResponse = await RequestGithubApi(StableRequestUrl, RequestRetryMaxTimes, ct);
                    if (string.IsNullOrEmpty(stableResponse))
                    {
                        return CheckUpdateStatus.NetworkError;
                    }

                    latestJson = JsonConvert.DeserializeObject(stableResponse) as JObject;
                    latestVersion = latestJson["tag_name"].ToString();
                    stableResponse = await RequestGithubApi(MaaReleaseRequestUrlByTag + latestVersion, RequestRetryMaxTimes, ct);

                    // 主仓库能找到版，但是 MaaRelease 找不到，说明 MaaRelease 还没有同步（一般过个十分钟就同步好了）
                    if (string.IsNullOrEmpty(stableResponse))
                    {
                        return CheckUpdateStatus.NewVersionIsBeingBuilt;
                    }

                    latestJson = JsonConvert.DeserializeObject(stableResponse) as JObject;
                }
                else
                {
                    // 非稳定版更新使用 MaaRelease/releases 接口
                    var response = await RequestGithubApi(RequestUrl, RequestRetryMaxTimes, ct);
                    if (string.IsNullOrEmpty(response))
                    {
                        return CheckUpdateStatus.NetworkError;
                    }

                    var releaseArray = JsonConvert.DeserializeObject(response) as JArray;

                    latestJson = null;
                    foreach (var item in releaseArray)
                    {
                        if (!Instances.SettingsViewModel.UpdateNightly && !isStdVersion(item["tag_name"].ToString()))
                        {
                            continue;
                        }

                        latestJson = item as JObject;
                        break;
                    }
                }

                if (latestJson == null)
                {
                    return CheckUpdateStatus.AlreadyLatest;
                }

                latestVersion = latestJson["tag_name"].ToString();
                var releaseAssets = latestJson["assets"] as JArray;

                if (Instances.SettingsViewModel.UpdateNightly)
                {
                    if (_curVersion == latestVersion)
                    {
                        return CheckUpdateStatus.AlreadyLatest;
                    }
                }
                else
                {
                    bool curParsed = SemVersion.TryParse(_curVersion, SemVersionStyles.AllowLowerV, out var curVersionObj);
                    bool latestPared = SemVersion.TryParse(latestVersion, SemVersionStyles.AllowLowerV, out var latestVersionObj);
                    if (curParsed && latestPared)
                    {
                        if (curVersionObj.CompareSortOrderTo(latestVersionObj) >= 0)
                        {
                            return CheckUpdateStatus.AlreadyLatest;
                        }
                    }
                    else if (string.CompareOrdinal(_curVersion, latestVersion) >= 0)
                    {
                        return CheckUpdateStatus.AlreadyLatest;
                    }
                }

                // 从主仓库获取changelog等信息
                // 非稳定版本是 Nightly 下载的，主仓库没有它的更新信息，不必请求
                if (isStdVersion(latestVersion))
                {
                    var infoResponse = await RequestGithubApi(InfoRequestUrl + latestVersion, RequestRetryMaxTimes, ct);
                    if (string.IsNullOrEmpty(infoResponse))
                    {
                        return CheckUpdateStatus.FailedToGetInfo;
                    }

                    latestJson = JsonConvert.DeserializeObject(infoResponse) as JObject;
                }

                JObject assetObject = null;
                foreach (var curAsset in releaseAssets)
                {
                    string name = curAsset["name"].ToString().ToLower();
                    if (name.Contains("ota") && name.Contains("win") && name.Contains($"{_curVersion}_{latestVersion}"))
                    {
                        assetObject = curAsset as JObject;
                        if (IsArm ^ name.Contains("arm"))
                        {
                            continue; // 兼容旧版本，以前 ota 不区分指令集架构
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                var body = latestJson["body"].ToString();

                if (string.IsNullOrEmpty(body))
                {
                    string ComparableHash(string version)
                    {
                        if (isStdVersion(version))
                        {
                            return version;
                        }
                        else if (SemVersion.TryParse(version, SemVersionStyles.AllowLowerV, out var semVersion) &&
                            isNightlyVersion(semVersion))
                        {
                            // v4.6.6-1.g{Hash}
                            // v4.6.7-beta.2.8.g{Hash}
                            var commitHash = semVersion.PrereleaseIdentifiers.Last().ToString();
                            if (commitHash.StartsWith("g"))
                            {
                                commitHash = commitHash.Remove(0, 1);
                            }

                            return commitHash;
                        }

                        return null;
                    }

                    var curHash = ComparableHash(_curVersion);
                    var latestHash = ComparableHash(latestVersion);

                    if (curHash != null && latestHash != null)
                    {
                        body = $"**Full Changelog**: [{curHash} -> {latestHash}](https://github.com/MaaAssistantArknights/MaaAssistantArknights/compare/{curHash}...{latestHash})";
                    }
                }

                var packageName = assetObject?["name"]?.ToString() ?? string.Empty;

                var updateInfo = new UpdateInfo
                {
                    Name = latestJson["name"].ToString(),
                    Tag = latestVersion,
                    ReleaseNotes = latestJson["body"].ToString(),
                    ReleaseWebPageUrl = latestJson["html_url"].ToString(),
                    GitHubReleaseAsset = assetObject,
                    AssetName = packageName,
                };

                UpdateInfo = updateInfo;
                UpdateStatus = UpdateStatus.Available;

                return CheckUpdateStatus.OK;
            }
            catch (Exception)
            {
                // Refactor pending
                return CheckUpdateStatus.UnknownError;
            }
        }

        /// <summary>
        /// 下载更新包。
        /// </summary>
        /// <param name="updateInfo">updateInfo</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task DownloadUpdate(CancellationToken ct)
        {
            var assetObject = UpdateInfo?.GitHubReleaseAsset;

            if (string.IsNullOrWhiteSpace(UpdateInfo?.AssetName))
            {
                throw new InvalidOperationException("No asset name in UpdateInfo");
            }

            string rawUrl = assetObject["browser_download_url"]?.ToString();

            async Task<bool> download_from_mirror(Tuple<string, string> rep)
            {
                var url = string.Copy(rawUrl);
                if (rep != null)
                {
                    url = url.Replace(rep.Item1, rep.Item2);
                }

                if (await DownloadGithubAssets(url, assetObject, ct))
                {
                    return true;
                }

                return false;
            }

            // 下载压缩包
            var mirroredReplaceMap = new List<Tuple<string, string>>
            {
                new Tuple<string, string>("github.com", "agent.imgg.dev"),
                new Tuple<string, string>("github.com", "maa.r2.imgg.dev"),
                new Tuple<string, string>("github.com", "ota.maa.plus"),
                null,
            };

            UpdateStatus = UpdateStatus.Downloading;

            // 0, 1 两个镜像流量比较充足，优先用
            var rand_index = new Random().Next(0, 2);   // 前闭后开 [0, 2)
            bool downloaded = await download_from_mirror(mirroredReplaceMap[rand_index]);

            if (!downloaded)
            {
                mirroredReplaceMap.RemoveAt(rand_index);
                for (int i = 0; i < mirroredReplaceMap.Count && !downloaded; i++)
                {
                    downloaded = await download_from_mirror(mirroredReplaceMap[i]);
                    if (downloaded)
                    {
                        break;
                    }
                }
            }

            if (!downloaded)
            {
                UpdateStatus = UpdateStatus.DownloadError;
                throw new IOException("Failed to download update package");
            }

            UpdateStatus = UpdateStatus.Downloaded;
            ConfigurationHelper.SetValue(ConfigurationKeys.NewVersionName, UpdateInfo.Tag);
            ConfigurationHelper.SetValue(ConfigurationKeys.NewVersionUpdateBody, UpdateInfo.ReleaseNotes);
            ConfigurationHelper.SetValue(ConfigurationKeys.VersionUpdatePackage, UpdateInfo.AssetName);
        }

        public bool CheckPendingUpdate()
        {
            var name = ConfigurationHelper.GetValue(ConfigurationKeys.NewVersionName, null);
            var notes = ConfigurationHelper.GetValue(ConfigurationKeys.NewVersionUpdateBody, null);
            var package = ConfigurationHelper.GetValue(ConfigurationKeys.VersionUpdatePackage, null);
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrEmpty(package) && File.Exists(package))
            {

                return true;
            }

            return false;
        }

        public static void PerformUpdate()
        {
            var package = ConfigurationHelper.GetValue(ConfigurationKeys.VersionUpdatePackage, null);
            string curDir = Directory.GetCurrentDirectory();
            string extractDir = Path.Combine(curDir, "NewVersionExtract");
            string oldFileDir = Path.Combine(curDir, ".old");

            // 解压
            try
            {
                if (Directory.Exists(extractDir))
                {
                    Directory.Delete(extractDir, true);
                }

                ZipFile.ExtractToDirectory(package, extractDir);
            }
            catch (InvalidDataException)
            {
                File.Delete(package);
                throw;
            }

            string removeListFile = Path.Combine(extractDir, "removelist.txt");
            if (File.Exists(removeListFile))
            {
                string[] removeList = File.ReadAllLines(removeListFile);
                foreach (string file in removeList)
                {
                    string path = Path.Combine(curDir, file);
                    if (File.Exists(path))
                    {
                        string moveTo = Path.Combine(oldFileDir, file);
                        if (File.Exists(moveTo))
                        {
                            File.Delete(moveTo);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(moveTo));
                        }

                        File.Move(path, moveTo);
                    }
                }
            }

            Directory.CreateDirectory(oldFileDir);
            foreach (var dir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(extractDir, curDir));
                Directory.CreateDirectory(dir.Replace(extractDir, oldFileDir));
            }

            // 复制新版本的所有文件到当前路径下
            foreach (var file in Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                if (fileName == "removelist.txt" || fileName == "filelist.txt")
                {
                    continue;
                }

                string curFileName = file.Replace(extractDir, curDir);
                if (File.Exists(curFileName))
                {
                    string moveTo = file.Replace(extractDir, oldFileDir);
                    if (File.Exists(moveTo))
                    {
                        File.Delete(moveTo);
                    }

                    File.Move(curFileName, moveTo);
                }

                File.Move(file, curFileName);
            }

            foreach (var oldFile in Directory.GetFiles(curDir, "*.old"))
            {
                File.Delete(oldFile);
            }

            // 操作完了，把解压的文件删了
            Directory.Delete(extractDir, true);
            File.Delete(package);

            // 保存更新信息，下次启动后会弹出已更新完成的提示
            var name = ConfigurationHelper.GetValue(ConfigurationKeys.NewVersionName, null);
            var notes = ConfigurationHelper.GetValue(ConfigurationKeys.NewVersionUpdateBody, null);
            ConfigurationHelper.DeleteValue(ConfigurationKeys.NewVersionName);
            ConfigurationHelper.DeleteValue(ConfigurationKeys.NewVersionUpdateBody);
            ConfigurationHelper.SetValue(ConfigurationKeys.VersionName, name);
            ConfigurationHelper.SetValue(ConfigurationKeys.VersionUpdateBody, notes);
            ConfigurationHelper.SetValue(ConfigurationKeys.VersionUpdatePackage, string.Empty);
            ConfigurationHelper.Release();

            // 重启进程（启动的是更新后的程序了）
            var newProcess = new Process();
            newProcess.StartInfo.FileName = AppDomain.CurrentDomain.FriendlyName;
            newProcess.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
            newProcess.Start();
            Application.Current.Shutdown();
        }

        private async Task<string> RequestGithubApi(string url, int retryTimes, CancellationToken ct)
        {
            string response = string.Empty;
            string[] requestSource = { "https://api.github.com/", "https://api.kgithub.com/" };
            do
            {
                for (var i = 0; i < requestSource.Length; i++)
                {
                    // prevent current thread
                    response = await Instances.HttpService.GetStringAsync(new Uri(requestSource[i] + url), cancellationToken: ct).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(response))
                    {
                        break;
                    }
                }
            }
            while (string.IsNullOrEmpty(response) && retryTimes-- > 0);
            return response;
        }

        /// <summary>
        /// 获取 GitHub Assets 对象对应的文件
        /// </summary>
        /// <param name="url">下载链接</param>
        /// <param name="assetsObject">Github Assets 对象</param>
        /// <returns>操作成功返回 true，反之则返回 false</returns>
        private async Task<bool> DownloadGithubAssets(string url, JObject assetsObject, CancellationToken ct)
        {
            try
            {
                void progressCallback(long xferd, long total)
                {
                    UpdateDownloadTransferredSize = xferd;
                    UpdateDownloadPackageSize = total;
                }

                return await Instances.HttpService.DownloadFileAsync(
                    new Uri(url),
                    assetsObject["name"].ToString(),
                    progressCallback,
                    assetsObject["content_type"].ToString(), cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool isDebugVersion(string version = null)
        {
            version ??= _curVersion;
            return version == "DEBUG VERSION";
        }

        private bool isStdVersion(string version = null)
        {
            // 正式版：vX.X.X
            // DevBuild (CI)：yyyy-MM-dd-HH-mm-ss-{CommitHash[..7]}
            // DevBuild (Local)：yyyy-MM-dd-HH-mm-ss-{CommitHash[..7]}-Local
            // Release (Local Commit)：v.{CommitHash[..7]}-Local
            // Release (Local Tag)：{Tag}-Local
            // Debug (Local)：DEBUG VERSION
            // Script Compiled：c{CommitHash[..7]}
            version ??= _curVersion;

            if (isDebugVersion(version))
            {
                return false;
            }
            else if (version.StartsWith("c") || version.StartsWith("20") || version.Contains("Local"))
            {
                return false;
            }
            else if (!SemVersion.TryParse(version, SemVersionStyles.AllowLowerV, out var semVersion))
            {
                return false;
            }
            else if (isNightlyVersion(semVersion))
            {
                return false;
            }

            return true;
        }

        private bool isNightlyVersion(SemVersion version)
        {
            if (!version.IsPrerelease)
            {
                return false;
            }

            // v{Major}.{Minor}.{Patch}-{Prerelease}.{CommitDistance}.g{CommitHash}
            // v4.6.7-beta.2.1.g1234567
            // v4.6.8-5.g1234567
            var lastId = version.PrereleaseIdentifiers.LastOrDefault().ToString();
            return lastId.StartsWith("g") && lastId.Length >= 7;
        }

        /// <summary>
        /// 复制文件夹内容并覆盖已存在的相同名字的文件
        /// </summary>
        /// <param name="sourcePath">源文件夹</param>
        /// <param name="targetPath">目标文件夹</param>
        public static void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            Directory.CreateDirectory(targetPath);

            // Now Create all of the directories
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }

            // Copy all the files & Replaces any files with the same name
            foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }
    }
}
