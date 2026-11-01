using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Gdal = OSGeo.GDAL.Gdal;
using Ogr = OSGeo.OGR.Ogr;

using SharpGIS.Services;
namespace SharpGIS
{
    /// <summary>
    /// GDAL 运行时引导：定位并注册原生库目录，设置 GDAL 配置项后注册驱动。
    /// 逻辑参考 OSGeo GDAL C# 绑定自带的 GdalConfiguration，另加只读数据保护（关闭 PAM）。
    /// </summary>
    public static class GdalBootstrap
    {
        private static readonly object Sync = new object();
        private static bool _initialized;
        private static bool _usable;

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint directoryFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddDllDirectory(string lpPathName);

        private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

        public static bool Usable { get { return _usable; } }
        public static string LastError { get; private set; }
        public static string NativeDirectory { get; private set; }

        public static string GdalVersion
        {
            get
            {
                try { return Gdal.VersionInfo("RELEASE_NAME"); }
                catch { return "unknown"; }
            }
        }

        /// <summary>确保 GDAL 已完成初始化；已初始化后重复调用直接返回（首次失败不会自动重试）。</summary>
        public static void Ensure()
        {
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;
                try
                {
                    ConfigurePaths();
                    Gdal.AllRegister();
                    Ogr.RegisterAll();
                    _usable = true;
                }
                catch (Exception ex)
                {
                    _usable = false;
                    LastError = DescribeFailure(ex);
                    Trace.WriteLine(LastError, "SharpGIS");
                }
            }
        }

        private static void ConfigurePaths()
        {
            string baseDir = AppContext.BaseDirectory;
            string gdalPath = Path.Combine(baseDir, "gdal");
            string nativePath = Path.Combine(gdalPath, Environment.Is64BitProcess ? "x64" : "x86");
            NativeDirectory = nativePath;

            if (!Directory.Exists(nativePath))
                throw new DirectoryNotFoundException(Lang.T("未找到 GDAL 原生库目录: ") + nativePath);
            if (!File.Exists(Path.Combine(nativePath, "gdal_wrap.dll")))
                throw new FileNotFoundException(Lang.T("未找到 GDAL 托管包装原生库: ") + Path.Combine(nativePath, "gdal_wrap.dll"));

            // 限定 DLL 搜索路径顺序，避免被系统同名 DLL 抢占
            try { SetDefaultDllDirectories(LoadLibrarySearchDefaultDirs); } catch { /* 老系统无此 API */ }
            AddDllDirectory(nativePath);
            string pluginPath = Path.Combine(nativePath, "plugins");
            if (Directory.Exists(pluginPath)) AddDllDirectory(pluginPath);

            string dataPath = Path.Combine(gdalPath, "data");
            SetEnv("GDAL_DATA", dataPath);
            SetEnv("GEOTIFF_CSV", dataPath);

            if (Directory.Exists(pluginPath)) SetEnv("GDAL_DRIVER_PATH", pluginPath);

            string projPath = Path.Combine(gdalPath, "share");
            if (Directory.Exists(projPath))
            {
                SetEnv("PROJ_LIB", projPath);
                try { OSGeo.OSR.Osr.SetPROJSearchPaths(new[] { projPath }); } catch { }
            }

            string caBundle = Path.Combine(gdalPath, "curl-ca-bundle.crt");
            if (File.Exists(caBundle)) Gdal.SetConfigOption("GDAL_CURL_CA_BUNDLE", caBundle);

            // 只读数据保护：禁止在源目录写 .aux.xml
            Gdal.SetConfigOption("GDAL_PAM_ENABLED", "NO");
            // 块缓存 512MB，显著改善大图反复读窗的命中率
            Gdal.SetConfigOption("GDAL_CACHEMAX", "512");
            Gdal.SetConfigOption("GDAL_NUM_THREADS", "ALL_CPUS");
        }

        private static void SetEnv(string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            try { Environment.SetEnvironmentVariable(name, value); } catch { }
            Gdal.SetConfigOption(name, value);
        }

        private static string DescribeFailure(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Lang.T("GDAL 初始化失败，程序无法打开影像。"));
            sb.AppendLine(Lang.T("原因: ") + ex.Message);
            sb.AppendLine(Lang.T("类型: ") + ex.GetType().Name);
            sb.AppendLine(Lang.T("程序目录: ") + AppContext.BaseDirectory);
            sb.AppendLine(Lang.T("原生库目录: ") + (NativeDirectory ?? Lang.T("(未解析)")));
            sb.AppendLine(Lang.T("进程位数: ") + (Environment.Is64BitProcess ? "x64" : "x86"));
            try
            {
                string gdalDir = Path.Combine(AppContext.BaseDirectory, "gdal");
                sb.AppendLine(Lang.T("gdal 目录存在: ") + Directory.Exists(gdalDir));
                if (Directory.Exists(gdalDir))
                    sb.AppendLine(Lang.T("gdal 子项: ") + string.Join(", ", Directory.GetDirectories(gdalDir)));
            }
            catch { }
            return sb.ToString();
        }

        /// <summary>已注册驱动声明的扩展名列表（形如 "*.tif"），用于拼 OpenFileDialog 过滤器；不含驱动短名。</summary>
        public static List<string> RasterDriverExtensions()
        {
            var list = new List<string>();
            try
            {
                int count = Gdal.GetDriverCount();
                for (int i = 0; i < count; i++)
                {
                    using (var drv = Gdal.GetDriver(i))
                    {
                        if (drv == null) continue;
                        string ext = drv.GetMetadataItem("DMD_EXTENSION", null);
                        if (!string.IsNullOrEmpty(ext)) list.Add("*." + ext.TrimStart('.'));
                    }
                }
            }
            catch { }
            return list;
        }
    }
}
