using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Unity.SharpZipLib.Utils;
using System.Collections.Generic;

namespace Sonic853.PackageInstaller
{
    public class PackageInstallerMain : EditorWindow
    {
        static TranslateReader translateReader = null;
        static readonly string _name = "PackageInstaller";
        static readonly string path = Path.Combine("Assets", "Sonic853", "Editor", "PackageInstaller");
        static readonly string zipPackagesPath = Path.Combine(path, "ZipPackages");
        static readonly string cachePath = Path.Combine("Temp", "com.sonic853.packageinstaller");
        static readonly string cacheOldPackagePath = Path.Combine(cachePath, "oldPackage");
        static readonly string cacheUnpackagePath = Path.Combine(cachePath, "Package");
        static void Init()
        {
            // 读取编辑器语言
            var language = EditorPrefs.GetString("Editor.kEditorLocale", "English");
            Debug.Log($"[{_name}] Editor Language: {language}");
            var file = Path.Combine(path, "Language", $"{language}.po.txt");
            if (!File.Exists(file))
            {
                Debug.Log($"[{_name}] Language not found, use English");
                file = Path.Combine(path, "Language", "English.po.txt");
            }
            translateReader ??= new TranslateReader(file);
        }
        [InitializeOnLoadMethod]
        static void AutoRun()
        {
            Init();
            if (File.Exists(Path.Combine(path, "codespace.txt")))
            {
                Debug.LogWarning($"[{_name}] {_("Codespace detected, skip install")}");
                return;
            }
            InstallPackages(true);
        }
        [MenuItem("853Lab/InstallPackages", false, 101)]
        static void InstallPackages()
        {
            InstallPackages(false);
        }
        static async void InstallPackages(bool auto)
        {
            var vpmPackageListFile = Path.Combine(path, "Data", "VPMPackages.asset");
            if (!File.Exists(vpmPackageListFile))
            {
                Debug.LogError($"[{_name}] {_("VPMPackages.asset not found")}");
                return;
            }
            var vpmPackages = VPMPackages.Instance;
            // 从本地获取 Zip 包
            if (vpmPackages.installZipPackages
            && Directory.Exists(zipPackagesPath))
            {
                var zipPackages = Directory.GetFiles(zipPackagesPath, "*.zip");
                foreach (var zipPackage in zipPackages)
                {
                    Debug.Log($"[{_name}] {string.Format(_("Install zip package {0}"), zipPackage)}");
                    var filename = Path.GetFileName(zipPackage);
                    var cachePackagePath = Path.Combine(cachePath, filename);
                    var filenameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
                    var unpackagePath = Path.Combine(cacheUnpackagePath, filenameWithoutExtension);
                    // 将 zipPackage 移动到缓存区 cachePackagePath
                    if (!Directory.Exists(cachePath))
                        Directory.CreateDirectory(cachePath);
                    var retryMax = 20;
                    var retryCount = 0;
                    var isDone = false;
                    while (File.Exists(cachePackagePath))
                    {
                        isDone = false;
                        if (retryCount++ > retryMax)
                        {
                            Debug.LogError($"[{_name}] {string.Format(_("Failed to Delete {0}, retry count exceed {1}"), cachePackagePath, retryMax)}");
                            return;
                        }
                        try
                        {
                            File.Delete(cachePackagePath);
                            isDone = true;
                        }
                        catch (Exception e)
                        {
                            isDone = false;
                            Debug.LogWarning($"[{_name}] {string.Format(_("Failed to Delete {0}, retry after 100ms. Error msg: {1}"), cachePackagePath, e.Message)}");
                        }
                        await Task.Delay(100);
                    }
                    if (!isDone)
                        continue;
                    retryCount = 0;
                    isDone = false;
                    if (File.Exists(Path.Combine(path, "codespace.txt")))
                    {
                        File.Copy(zipPackage, cachePackagePath);
                        isDone = true;
                    }
                    else
                        while (File.Exists(zipPackage))
                        {
                            isDone = false;
                            if (retryCount++ > retryMax)
                            {
                                Debug.LogError($"[{_name}] {string.Format(_("Failed to Move {0}, retry count exceed {1}"), zipPackage, retryMax)}");
                                return;
                            }
                            try
                            {
                                File.Move(zipPackage, cachePackagePath);
                                isDone = true;
                            }
                            catch (Exception e)
                            {
                                isDone = false;
                                Debug.LogWarning($"[{_name}] {string.Format(_("Failed to Move {0}, retry after 100ms. Error msg: {1}"), zipPackage, e.Message)}");
                            }
                            await Task.Delay(100);
                        }
                    if (!isDone)
                        continue;
                    if (!await InstallPackage(cachePackagePath, !vpmPackages.forceInstallZipPackages))
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Failed to install zip package {0}"), zipPackage)}");
                        continue;
                    }
                    Debug.Log($"[{_name}] {string.Format(_("Install zip package {0} success"), zipPackage)}");
                }
            }
            // 从 url 下载 vpm 包列表
            if (vpmPackages.installVPMPackages
            && !string.IsNullOrWhiteSpace(vpmPackages.url))
            {
                var vpmData = await GetVPMData(vpmPackages.url);
                if (vpmData == null)
                {
                    Debug.LogError($"[{_name}] {_("Failed to get VPMData")}");
                    return;
                }
                foreach (var package in vpmPackages.installPackages)
                {
                    if (string.IsNullOrWhiteSpace(package.name))
                        continue;
                    if (!vpmData.packages.TryGetValue(package.name, out var vpmPackage))
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Package {0} not found"), package.name)}");
                        continue;
                    }
                    PackageVersion _package;
                    if (!string.IsNullOrWhiteSpace(package.version)
                    && package.version != "latest"
                    && !package.version.StartsWith("^"))
                    {
                        if (!vpmPackage.versions.TryGetValue(package.version, out _package))
                        {
                            Debug.LogError($"[{_name}] {string.Format(_("Package {0} version {1} not found, use latest version"), package.name, package.version)}");
                            _package = vpmPackage.GetLatestVersion();
                        }
                    }
                    else
                        _package = vpmPackage.GetLatestVersion();
                    if (_package == null)
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Package {0} version not found"), package.name)}");
                        continue;
                    }
                    var localPackagePath = Path.Combine("Packages", $"{package.name}");
                    var localPackageJsonFile = Path.Combine(localPackagePath, "package.json");
                    string localPackageVersion = null;
                    if (Directory.Exists(localPackagePath)
                    && File.Exists(localPackageJsonFile))
                    {
                        var localPackageJson = JObject.Parse(File.ReadAllText(localPackageJsonFile));
                        localPackageVersion = localPackageJson["version"].ToString();
                    }
                    if (!string.IsNullOrWhiteSpace(localPackageVersion)
                    && VersionCompare(localPackageVersion, _package.version) >= 0
                    && !package.forceInstall)
                    {
                        Debug.Log($"[{_name}] {string.Format(_("Loacl package {0} version {1} is latest version of {2}"), package.name, localPackageVersion, _package.version)}");
                        continue;
                    }
                    // _package.url
                    var _packagePath = await DownloadPackage(_package.url);
                    if (string.IsNullOrWhiteSpace(_packagePath))
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Failed to download package {0} version {1}"), package.name, _package.version)}");
                        continue;
                    }
                    if (!await InstallPackage(_packagePath))
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Failed to install package {0} version {1}"), package.name, _package.version)}");
                        continue;
                    }
                    Debug.Log($"[{_name}] {string.Format(_("Install package {0} version {1} success"), package.name, _package.version)}");
                }
            }
            // 删除文件夹
            if (Directory.Exists(cachePath))
                Directory.Delete(cachePath, true);
            if (File.Exists(Path.Combine(path, "codespace.txt")))
            {
                AssetDatabase.Refresh();
                return;
            }
            if (vpmPackages.deleteSelf)
            {
                foreach (var folder in vpmPackages.deleteFolders)
                {
                    if (string.IsNullOrWhiteSpace(folder))
                        continue;
                    var _f = folder.Split('\\');
                    if (_f.Length <= 1)
                        continue;
                    var _folder = Path.Combine(_f);
                    if (Directory.Exists(_folder))
                        Directory.Delete(_folder, true);
                }
            }
            AssetDatabase.Refresh();
        }
        /// <summary>
        /// 获取 VPMData 数据
        /// </summary>
        /// <returns>当下载成功时返回 VPMData，否则为 null</returns>
        static async Task<VPMData> GetVPMData(string vpmurl)
        {
            if (vpmurl.Trim().ToLower().StartsWith("http://")
                && PlayerSettings.insecureHttpOption == InsecureHttpOption.NotAllowed)
                PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            Debug.Log($"[{_name}] {string.Format(_("GetVPMData: {0}"), vpmurl)}");
            string url = $"{vpmurl.Trim()}?t={DateTime.Now.Ticks}";
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.SendWebRequest();
            while (!request.isDone)
                await Task.Yield();
            if (request.result == UnityWebRequest.Result.ConnectionError
            || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[{_name}] {string.Format(_("GetVPMData Error: {0}"), request.error)}");
                return null;
            }
            var vpmData = JsonConvert.DeserializeObject<VPMData>(request.downloadHandler.text);
            return vpmData;
        }
        /// <summary>
        /// 下载包到本地缓存区
        /// </summary>
        /// <param name="packageurl"></param>
        /// <returns></returns>
        static async Task<string> DownloadPackage(string packageurl)
        {
            if (packageurl.Trim().ToLower().StartsWith("http://")
                && PlayerSettings.insecureHttpOption == InsecureHttpOption.NotAllowed)
                PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
            Debug.Log($"[{_name}] {string.Format(_("DownloadPackage: {0}"), packageurl)}");
            string url = packageurl.Trim();
            UnityWebRequest request = UnityWebRequest.Get(url);
            request.SendWebRequest();
            while (!request.isDone)
                await Task.Yield();
            if (request.result == UnityWebRequest.Result.ConnectionError
            || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[{_name}] {string.Format(_("DownloadPackage Error: {0}"), request.error)}");
                return null;
            }
            var filename = Path.GetFileName(packageurl);
            if (!Directory.Exists(cachePath))
                Directory.CreateDirectory(cachePath);
            var filepath = Path.Combine(cachePath, filename);
            File.WriteAllBytes(filepath, request.downloadHandler.data);
            return filepath;
        }
        /// <summary>
        /// 解压包到 Package 文件夹
        /// </summary>
        /// <param name="_cachePackageFile"></param>
        /// <returns></returns>
        static async Task<bool> InstallPackage(string _cachePackageFile, bool checkVersion = false)
        {
            if (!File.Exists(_cachePackageFile))
            {
                Debug.LogError($"[{_name}] {string.Format(_("Package file {0} not found"), _cachePackageFile)}");
                return false;
            }
            var filename = Path.GetFileName(_cachePackageFile);
            var filenameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            var oldPackagePath = Path.Combine(cacheOldPackagePath, filenameWithoutExtension);
            var unpackagePath = Path.Combine(cacheUnpackagePath, filenameWithoutExtension);
            var localPackagePath = Path.Combine("Packages", filenameWithoutExtension);
            if (!Directory.Exists(cacheUnpackagePath))
                Directory.CreateDirectory(cacheUnpackagePath);
            var retryMax = 20;
            var retryCount = 0;
            var isDone = false;
            while (Directory.Exists(unpackagePath))
            {
                isDone = false;
                if (retryCount++ > retryMax)
                {
                    Debug.LogError($"[{_name}] {string.Format(_("Failed to Delete {0}, retry count exceed {1}"), unpackagePath, retryMax)}");
                    return false;
                }
                try
                {
                    Directory.Delete(unpackagePath, true);
                    isDone = true;
                }
                catch (Exception e)
                {
                    isDone = false;
                    Debug.LogWarning($"[{_name}] {string.Format(_("Failed to Delete {0}, retry after 100ms. Error msg: {1}"), unpackagePath, e.Message)}");
                }
                await Task.Delay(100);
            }
            if (!isDone)
                return false;
            retryCount = 0;
            isDone = false;
            ZipUtility.UncompressFromZip(_cachePackageFile, string.Empty, unpackagePath);
            if (checkVersion)
            {
                var packageJsonFile = Path.Combine(unpackagePath, "package.json");
                if (File.Exists(packageJsonFile))
                {
                    var packageJson = JObject.Parse(File.ReadAllText(packageJsonFile));
                    var version = packageJson["version"].ToString();
                    var localPackageJsonFile = Path.Combine(localPackagePath, "package.json");
                    if (File.Exists(localPackageJsonFile))
                    {
                        var localPackageJson = JObject.Parse(File.ReadAllText(localPackageJsonFile));
                        var localVersion = localPackageJson["version"].ToString();
                        if (VersionCompare(localVersion, version) >= 0)
                        {
                            Debug.Log($"[{_name}] {string.Format(_("Loacl package {0} version {1} is latest version of zip package version {2}"), filenameWithoutExtension, localVersion, version)}");
                            return true;
                        }
                    }
                }
            }
            if (Directory.Exists(localPackagePath))
            {
                // 移动旧包到缓存区
                if (File.Exists(Path.Combine(path, "codespace.txt")))
                {
                    Debug.LogWarning($"[{_name}] {string.Format(_("Codespace detected, skip install {0}"), filenameWithoutExtension)}");
                    return true;
                }
                if (!Directory.Exists(cacheOldPackagePath))
                    Directory.CreateDirectory(cacheOldPackagePath);
                while (Directory.Exists(oldPackagePath))
                {
                    isDone = false;
                    if (retryCount++ > retryMax)
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Failed to Delete {0}, retry count exceed {1}"), oldPackagePath, retryMax)}");
                        return false;
                    }
                    try
                    {
                        Directory.Delete(oldPackagePath, true);
                        isDone = true;
                    }
                    catch (Exception e)
                    {
                        isDone = false;
                        Debug.LogWarning($"[{_name}] {string.Format(_("Failed to Delete {0}, retry after 100ms. Error msg: {1}"), oldPackagePath, e.Message)}");
                    }
                    await Task.Delay(100);
                }
                if (!isDone)
                    return false;
                retryCount = 0;
                isDone = false;
                while (Directory.Exists(localPackagePath))
                {
                    isDone = false;
                    if (retryCount++ > retryMax)
                    {
                        Debug.LogError($"[{_name}] {string.Format(_("Failed to Move {0}, retry count exceed {1}"), localPackagePath, retryMax)}");
                        return false;
                    }
                    try
                    {
                        Directory.Move(localPackagePath, oldPackagePath);
                        isDone = true;
                    }
                    catch (Exception e)
                    {
                        isDone = false;
                        Debug.LogWarning($"[{_name}] {string.Format(_("Failed to Move {0}, retry after 100ms. Error msg: {1}"), localPackagePath, e.Message)}");
                    }
                    await Task.Delay(100);
                }
                if (!isDone)
                    return false;
            }
            Directory.Move(unpackagePath, localPackagePath);
            // 删除缓存区
            if (Directory.Exists(cacheOldPackagePath))
                Directory.Delete(cacheOldPackagePath, true);
            // if (Directory.Exists(cacheUnpackagePath))
            //     Directory.Delete(cacheUnpackagePath, true);
            return true;
        }
        /// <summary>
        /// 版本比较
        /// </summary>
        /// /// <param name="version1"></param>
        /// <param name="version2"></param>
        /// <returns></returns>
        static int VersionCompare(string version1, string version2)
        {
            version1 = version1.Trim();
            if (version1.ToLower().StartsWith("v"))
                version1 = version1[1..];
            version2 = version2.Trim();
            if (version2.ToLower().StartsWith("v"))
                version2 = version2[1..];
            var version1Array = version1.Split('.');
            var version2Array = version2.Split('.');
            for (int i = 0; i < version1Array.Length; i++)
            {
                if (version2Array.Length <= i)
                    return 1;
                // tryparse
                if (int.TryParse(version1Array[i], out var version1Int)
                && int.TryParse(version2Array[i], out var version2Int))
                {
                    if (version1Int > version2Int)
                        return 1;
                    else if (version1Int < version2Int)
                        return -1;
                }
                else
                {
                    if (string.Compare(version1Array[i], version2Array[i], StringComparison.Ordinal) > 0)
                        return 1;
                    else if (string.Compare(version1Array[i], version2Array[i], StringComparison.Ordinal) < 0)
                        return -1;
                }
            }
            if (version2Array.Length > version1Array.Length)
                return -1;
            return 0;
        }
        static string _(string text)
        {
            return translateReader?._(text) ?? text;
        }
    }
    public class ScriptableLoader<T> : ScriptableObject where T : ScriptableLoader<T>, new()
    {
        /// <summary>
        /// Default: Assets/Sonic853/Data
        /// </summary>
        protected static string savePath = Path.Combine("Assets", "Sonic853", "Data");
        /// <summary>
        /// Default: {typeof(T).Name}.asset
        /// </summary>
        protected static string fileName = null;
        protected static T instance;
        public static T Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = CreateInstance<T>();
                    instance.Load();
                }
                return instance;
            }
        }
        public virtual void Load()
        {
            if (!Directory.Exists(savePath))
                Directory.CreateDirectory(savePath);
            if (string.IsNullOrEmpty(fileName))
                fileName = $"{typeof(T).Name}.asset";
            string filePath = Path.Combine(savePath, fileName);
            var _instance = AssetDatabase.LoadAssetAtPath<T>(filePath);
            if (_instance == null)
            {
                _instance = CreateInstance<T>();
                AssetDatabase.CreateAsset(_instance, filePath);
                AssetDatabase.SaveAssets();
            }
            instance = _instance;
        }
        public virtual void Save()
        {
            if (string.IsNullOrEmpty(fileName))
                fileName = $"{typeof(T).Name}.asset";
            string filePath = Path.Combine(savePath, fileName);
            if (File.Exists(filePath))
                EditorUtility.SetDirty(instance);
            else
            {
                if (!Directory.Exists(savePath))
                    Directory.CreateDirectory(savePath);
                AssetDatabase.CreateAsset(instance, filePath);
            }
            AssetDatabase.SaveAssets();
        }
    }
    /// <summary>
    /// VPM 数据
    /// </summary>
    public class VPMData
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string name = string.Empty;
        /// <summary>
        /// 作者
        /// </summary>
        public string author = string.Empty;
        /// <summary>
        /// ID
        /// </summary>
        public string id = string.Empty;
        /// <summary>
        /// 链接
        /// </summary>
        public string url = string.Empty;
        /// <summary>
        /// 包列表
        /// </summary>
        /// <typeparam name="string">包名称</typeparam>
        /// <typeparam name="VPMDataPackage">包</typeparam>
        /// <returns>包</returns>
        public Dictionary<string, VPMDataPackage> packages = new();
    }
    /// <summary>
    /// 包
    /// </summary>
    public class VPMDataPackage
    {
        /// <summary>
        /// 版本列表
        /// </summary>
        /// <typeparam name="string">版本号</typeparam>
        /// <typeparam name="PackageVersion">该版本的包</typeparam>
        /// <returns>该版本的包</returns>
        public Dictionary<string, PackageVersion> versions = new();
    }
    /// <summary>
    /// 包扩展
    /// </summary>
    public static class VPMDataPackageExtensions
    {
        /// <summary>
        /// 获取所有包版本
        /// </summary>
        /// <param name="package">包</param>
        /// <returns>包的所有版本</returns>
        public static List<KeyValuePair<string, PackageVersion>> GetVersions(this VPMDataPackage package)
        {
            List<KeyValuePair<string, PackageVersion>> _versions = new();
            foreach (var version in package.versions)
            {
                _versions.Add(version);
            }
            return _versions;
        }
        /// <summary>
        /// 写入包版本
        /// </summary>
        /// <param name="package">包</param>
        /// <param name="version">版本</param>
        /// <param name="packageVersion">包版本</param>
        /// <returns>是否写入成功</returns>
        public static bool InsertVersion(this VPMDataPackage package, string version, PackageVersion packageVersion)
        {
            if (package.versions.ContainsKey(version))
            {
                return false;
            }
            var _versions = GetVersions(package);
            _versions.Insert(0, new KeyValuePair<string, PackageVersion>(version, packageVersion));
            package.versions.Clear();
            foreach (var _version in _versions)
            {
                package.versions.Add(_version.Key, _version.Value);
            }
            return true;
        }
        public static PackageVersion GetLatestVersion(this VPMDataPackage package)
        {
            PackageVersion latestVersion = null;
            foreach (var version in package.versions)
            {
                if (latestVersion == null)
                {
                    latestVersion = version.Value;
                    continue;
                }
                if (VersionCompare(version.Value.version, latestVersion.version) > 0)
                {
                    latestVersion = version.Value;
                }
            }
            return latestVersion;
        }
        /// <summary>
        /// 版本比较
        /// </summary>
        /// /// <param name="version1"></param>
        /// <param name="version2"></param>
        /// <returns></returns>
        static int VersionCompare(string version1, string version2)
        {
            version1 = version1.Trim();
            if (version1.ToLower().StartsWith("v"))
                version1 = version1[1..];
            version2 = version2.Trim();
            if (version2.ToLower().StartsWith("v"))
                version2 = version2[1..];
            var version1Array = version1.Split('.');
            var version2Array = version2.Split('.');
            for (int i = 0; i < version1Array.Length; i++)
            {
                if (version2Array.Length <= i)
                    return 1;
                // tryparse
                if (int.TryParse(version1Array[i], out var version1Int)
                && int.TryParse(version2Array[i], out var version2Int))
                {
                    if (version1Int > version2Int)
                        return 1;
                    else if (version1Int < version2Int)
                        return -1;
                }
                else
                {
                    if (string.Compare(version1Array[i], version2Array[i], StringComparison.Ordinal) > 0)
                        return 1;
                    else if (string.Compare(version1Array[i], version2Array[i], StringComparison.Ordinal) < 0)
                        return -1;
                }
            }
            if (version2Array.Length > version1Array.Length)
                return -1;
            return 0;
        }
    }
    /// <summary>
    /// 包版本
    /// </summary>
    public class PackageVersion
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string name = string.Empty;
        /// <summary>
        /// 显示名称
        /// </summary>
        public string displayName = string.Empty;
        /// <summary>
        /// 版本号
        /// </summary>
        public string version = string.Empty;
        /// <summary>
        /// Unity 版本
        /// </summary>
        public string unity = string.Empty;
        /// <summary>
        /// 描述
        /// </summary>
        public string description = string.Empty;
        /// <summary>
        /// 文档地址
        /// </summary>
        public string documentationUrl = string.Empty;
        /// <summary>
        /// 更新日志地址
        /// </summary>
        public string changelogUrl = string.Empty;
        /// <summary>
        /// 许可地址
        /// </summary>
        public string licensesUrl = string.Empty;
        /// <summary>
        /// 许可
        /// </summary>
        public string license = string.Empty;
        /// <summary>
        /// 许可
        /// </summary>
        public string[] keywords = new string[0];
        /// <summary>
        /// 依赖
        /// </summary>
        /// <typeparam name="string">名称</typeparam>
        /// <typeparam name="string">版本号</typeparam>
        /// <returns>版本号</returns>
        public Dictionary<string, string> vpmDependencies = new();
        /// <summary>
        /// 示例
        /// </summary>
        public VersionSamples[] samples = new VersionSamples[0];
        /// <summary>
        /// 作者信息
        /// </summary>
        public VersionAuthor author = new();
        /// <summary>
        /// ZIP SHA256
        /// </summary>
        public string zipSHA256 = string.Empty;
        /// <summary>
        /// ZIP 下载地址
        /// </summary>
        public string url = string.Empty;
        /// <summary>
        /// 仓库地址
        /// </summary>
        public string repo = string.Empty;
        /// <summary>
        /// 旧版文件夹
        /// </summary>
        /// <typeparam name="string">文件夹路径</typeparam>
        /// <typeparam name="string">guid（在文件夹附属的.meta文件）</typeparam>
        /// <returns>guid（在文件夹附属的.meta文件）</returns>
        public Dictionary<string, string> legacyFolders = new();
    }
    /// <summary>
    /// 示例
    /// </summary>
    public class VersionSamples
    {
        /// <summary>
        /// 显示名称
        /// </summary>
        public string displayName = string.Empty;
        /// <summary>
        /// 描述
        /// </summary>
        public string description = string.Empty;
        /// <summary>
        /// 路径
        /// </summary>
        public string path = string.Empty;
    }
    /// <summary>
    /// 作者信息
    /// </summary>
    public class VersionAuthor
    {
        /// <summary>
        /// 名称
        /// </summary>
        public string name = string.Empty;
        /// <summary>
        /// 邮箱
        /// </summary>
        public string email = string.Empty;
        /// <summary>
        /// 地址
        /// </summary>
        public string url = string.Empty;
    }
    public class TranslateReader
    {
        public TranslateReader(string path)
        {
            ReadFile(path);
        }
        private string[] lines = new string[0];
        private readonly List<string> msgid = new();
        private readonly List<string> msgstr = new();
        public string language = "en_US";
        public string lastTranslator = "anonymous";
        public string languageTeam = "anonymous";
        public string[] ReadFile(string path, bool parse = true)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"File {path} not found");
                return null;
            }
            var text = File.ReadAllText(path);
            lines = text.Split('\n');
            if (text.Contains("\r\n"))
                lines = text.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (parse)
                ParseFile(lines);
            return lines;
        }
        public void ParseFile(string path)
        {
            ReadFile(path, true);
        }
        public void ParseFile(string[] _lines)
        {
            msgid.Clear();
            msgstr.Clear();
            var msgidIndex = -1;
            var msgstrIndex = -1;
            var msgidStr = "msgid \"";
            var msgidLength = msgidStr.Length;
            var msgstrStr = "msgstr \"";
            var msgstrLength = msgstrStr.Length;
            var languageStr = "\"Language: ";
            var languageLength = languageStr.Length;
            var lastTranslatorStr = "\"Last-Translator: ";
            var lastTranslatorLength = lastTranslatorStr.Length;
            var languageTeamStr = "\"Language-Team: ";
            var languageTeamLength = languageTeamStr.Length;
            var doubleQuotationStr = "\"";
            var doubleQuotationLength = doubleQuotationStr.Length;
            foreach (var line in _lines)
            {
                var _line = line.Trim();
                if (_line.StartsWith(msgidStr))
                {
                    msgid.Add(ReturnText(_line[msgidLength.._line.LastIndexOf('"')]));
                    msgidIndex = msgid.Count - 1;
                    msgstrIndex = -1;
                    continue;
                }
                if (_line.StartsWith(msgstrStr))
                {
                    msgstr.Add(ReturnText(_line[msgstrLength.._line.LastIndexOf('"')]));
                    msgstrIndex = msgstr.Count - 1;
                    continue;
                }
                if (_line.StartsWith(languageStr) && msgstrIndex == 0)
                {
                    language = _line[languageLength.._line.LastIndexOf('"')];
                    // 找到并去除\n
                    if (language.Contains("\\n"))
                        language = language.Replace("\\n", "");
                    continue;
                }
                if (_line.StartsWith(lastTranslatorStr) && msgstrIndex == 0)
                {
                    lastTranslator = _line[lastTranslatorLength.._line.LastIndexOf('"')];
                    // 找到并去除\n
                    if (lastTranslator.Contains("\\n"))
                        lastTranslator = lastTranslator.Replace("\\n", "");
                    // 将<和>替换为＜和＞
                    lastTranslator = lastTranslator.Replace("<", "＜").Replace(">", "＞");
                    continue;
                }
                if (_line.StartsWith(languageTeamStr) && msgstrIndex == 0)
                {
                    languageTeam = _line[languageTeamLength.._line.LastIndexOf('"')];
                    // 找到并去除\n
                    if (languageTeam.Contains("\\n"))
                        languageTeam = languageTeam.Replace("\\n", "");
                    // 将<和>替换为＜和＞
                    languageTeam = languageTeam.Replace("<", "＜").Replace(">", "＞");
                    continue;
                }
                if (_line.StartsWith(doubleQuotationStr))
                {
                    if (msgidIndex != -1 && msgidIndex != 0)
                    {
                        msgid[msgidIndex] += ReturnText(_line[doubleQuotationLength.._line.LastIndexOf('"')]);
                        continue;
                    }
                    if (msgstrIndex != -1 && msgstrIndex != 0)
                    {
                        msgstr[msgstrIndex] += ReturnText(_line[doubleQuotationLength.._line.LastIndexOf('"')]);
                        continue;
                    }
                }
            }
            if (msgid.Count != msgstr.Count)
            {
                Debug.LogError("msgid.Count != msgstr.Count");
                return;
            }
        }
        string ReturnText(string text)
        {
            if (text.EndsWith("\\\\n"))
            {
                text = text[..^3];
                text += "\\n";
            }
            else if (text.EndsWith("\\n"))
            {
                text = text[..^2];
                text += "\n";
            }
            return text;
        }
        public string GetText(string text)
        {
            var index = msgid.IndexOf(text);
            if (index == -1)
                return text;
            return string.IsNullOrWhiteSpace(msgstr[index]) ? text : msgstr[index];
        }
        public string _(string text)
        {
            return GetText(text);
        }
    }
}
