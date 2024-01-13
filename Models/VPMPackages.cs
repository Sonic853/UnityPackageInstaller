using System;
using System.IO;

namespace Sonic853.PackageInstaller
{
    public class VPMPackages : ScriptableLoader<VPMPackages>
    {
        /// <summary>
        /// VPM 地址
        /// </summary>
        public string url = "";
        /// <summary>
        /// 安装 VPM 包
        /// </summary>
        public bool installVPMPackages = true;
        /// <summary>
        /// 要安装的包
        /// </summary>
        public Package[] installPackages = new Package[0];
        /// <summary>
        /// 安装 Zip 包
        /// </summary>
        public bool installZipPackages = true;
        /// <summary>
        /// 无视版本号检查强制安装
        /// </summary>
        public bool forceInstallZipPackages = false;
        /// <summary>
        /// 完成安装后删除自身（codespace.txt 存在时无效）
        /// </summary>
        public bool deleteSelf = false;
        /// <summary>
        /// 要删除的文件夹
        /// </summary>
        public string[] deleteFolders = new string[0];
        public override void Load()
        {
            savePath = Path.Combine("Assets", "Sonic853", "Editor", "PackageInstaller", "Data");
            base.Load();
        }
    }
    /// <summary>
    /// 要安装的包
    /// </summary>
    [Serializable]
    public class Package
    {
        /// <summary>
        /// 包名
        /// </summary>
        public string name;
        /// <summary>
        /// 版本号
        /// </summary>
        public string version;
        /// <summary>
        /// 无视版本号检查强制安装
        /// </summary>
        public bool forceInstall = false;
    }
}
