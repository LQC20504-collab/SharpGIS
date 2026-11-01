using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// 运行时反射探针：把 GDAL 托管绑定里真实存在的关键签名打印出来，
    /// 避免凭记忆写错 API（不同版本的 GDAL C# 绑定命名差异很大）。
    /// </summary>
    internal static class ApiProbe
    {
        public static int Run(string outDir)
        {
            var sb = new StringBuilder();
            void P(string s) { sb.AppendLine(s); ConsoleBridge.Write(s); }

            P("=== SharpGIS API 探针 ===");
            P("时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            P("GDAL 版本: " + SafeVersion());
            P("程序目录: " + AppContext.BaseDirectory);
            P("原生库目录: " + (GdalBootstrap.NativeDirectory ?? "(未解析)"));
            P("进程位数: " + (Environment.Is64BitProcess ? "x64" : "x86"));
            P("驱动数量: " + SafeDriverCount());
            P("");

            DumpTypes(P);
            DumpBandReadRaster(P);
            DumpGdalStatics(P);
            DumpDatasetMembers(P);
            DumpFullMembers(P);

            string dir = string.IsNullOrEmpty(outDir) ? Path.Combine(AppContext.BaseDirectory, "artifacts") : outDir;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "api_probe.log");
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            ConsoleBridge.Write("探针日志: " + file);
            return GdalBootstrap.Usable ? 0 : 2;
        }

        private static string SafeVersion()
        {
            try { return OSGeo.GDAL.Gdal.VersionInfo("RELEASE_NAME"); } catch (Exception ex) { return "<失败: " + ex.GetType().Name + ">"; }
        }

        private static int SafeDriverCount()
        {
            try { return OSGeo.GDAL.Gdal.GetDriverCount(); } catch (Exception ex) { return -1 * (int)ex.HResult; }
        }

        private static void DumpTypes(Action<string> p)
        {
            p("--- 关键类型是否存在 ---");
            var asm = typeof(OSGeo.GDAL.Gdal).Assembly;
            var wanted = new[]
            {
                "OSGeo.GDAL.RasterIOExtraArg", "OSGeo.GDAL.Gdal+GDALWarpAppOptions",
                "OSGeo.GDAL.Gdal+GDALTranslateOptions", "OSGeo.GDAL.Gdal+GDALDEMProcessingOptions",
                "OSGeo.GDAL.Gdal+WarpOptions", "OSGeo.GDAL.GDALWarpAppOptions",
                "OSGeo.GDAL.GDALTranslateOptions", "OSGeo.GDAL.GDALDEMProcessingOptions",
                "OSGeo.GDAL.ColorTableEntry", "OSGeo.GDAL.GDALProgressFuncDelegate"
            };
            foreach (var name in wanted)
            {
                var t = asm.GetType(name, false);
                p((t != null ? "  [有] " : "  [无] ") + name);
            }

            p("");
            p("--- 名字含 Color / Palette / Progress 的公开类型 ---");
            foreach (var t in SafeTypes(asm).Where(t => t.IsPublic &&
                        (t.Name.Contains("Color") || t.Name.Contains("Palette") || t.Name.Contains("Progress")))
                        .OrderBy(t => t.FullName))
                p("  " + t.FullName + (t.IsNested ? " (nested)" : ""));
            p("");
            p("--- 名字含 Options 的公开类型 ---");
            foreach (var t in SafeTypes(asm).Where(t => t.IsPublic && t.Name.Contains("Options")).OrderBy(t => t.FullName))
                p("  " + t.FullName + (t.IsNested ? " (nested)" : ""));
            p("");
        }

        private static void DumpBandReadRaster(Action<string> p)
        {
            p("--- Band.ReadRaster 重载 ---");
            var band = typeof(OSGeo.GDAL.Band);
            foreach (var m in band.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "ReadRaster"))
                p("  " + Sig(m));
            p("");
        }

        private static void DumpGdalStatics(Action<string> p)
        {
            p("--- Gdal 静态方法（Warp/Translate/DEM/Overview/Reproject/BuildVRT）---");
            var gdal = typeof(OSGeo.GDAL.Gdal);
            string[] names = { "Warp", "WarpOptions", "Translate", "DEMProcessing", "BuildOverviews", "ReprojectImage", "AutoCreateWarpedVRT", "BuildVRT", "Open", "OpenShared" };
            foreach (var m in gdal.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                   .Where(m => names.Contains(m.Name))
                                   .OrderBy(m => m.Name))
                p("  " + Sig(m));
            p("");
        }

        private static void DumpDatasetMembers(Action<string> p)
        {
            p("--- Dataset 关键方法 ---");
            var ds = typeof(OSGeo.GDAL.Dataset);
            string[] names = { "BuildOverviews", "GetRasterBand", "ReadRaster", "GetGeoTransform", "SetGeoTransform", "GetProjection", "SetProjection", "GetProjectionRef", "GetMetadata", "GetGCPProjection" };
            foreach (var m in ds.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => names.Contains(m.Name)).OrderBy(m => m.Name))
                p("  " + Sig(m));
            p("");
        }

        private static void DumpFullMembers(Action<string> p)
        {
            p("--- Gdal 静态方法（含 DEM/Dem/Progress）---");
            foreach (var m in typeof(OSGeo.GDAL.Gdal).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(m => m.Name.IndexOf("DEM", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.Contains("Progress")))
                p("  " + Sig(m));
            p("--- GDALProgressFuncDelegate ---");
            var del = typeof(OSGeo.GDAL.Gdal).GetNestedTypes().FirstOrDefault(t => t.Name.Contains("Progress"));
            if (del != null)
                foreach (var m in del.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "Invoke"))
                    p("  Invoke(" + string.Join(", ", m.GetParameters().Select(x => Pretty(x.ParameterType) + " " + x.Name)) + ") -> " + m.ReturnType.Name);
            p("");
            p("--- 枚举成员 ---");
            foreach (var t in new[] { typeof(OSGeo.GDAL.RIOResampleAlg), typeof(OSGeo.GDAL.ResampleAlg), typeof(OSGeo.GDAL.ColorInterp) })
                p("  " + t.Name + ": " + string.Join(", ", Enum.GetNames(t)));
            p("--- RasterIOExtraArg 字段 ---");
            foreach (var f in typeof(OSGeo.GDAL.RasterIOExtraArg).GetFields())
                p("  " + f.FieldType.Name + " " + f.Name);
            p("");
            p("--- Band 全部公开成员 ---");
            foreach (var m in typeof(OSGeo.GDAL.Band).GetMethods(BindingFlags.Public | BindingFlags.Instance).OrderBy(m => m.Name))
                p("  " + Sig(m));
            p("");
            p("--- ColorTable 全部公开成员 ---");
            foreach (var m in typeof(OSGeo.GDAL.ColorTable).GetMethods(BindingFlags.Public | BindingFlags.Instance).OrderBy(m => m.Name))
                p("  " + Sig(m));
            p("");
            p("--- ColorEntry 字段/属性 ---");
            foreach (var f in typeof(OSGeo.GDAL.ColorEntry).GetFields(BindingFlags.Public | BindingFlags.Instance))
                p("  field " + Pretty(f.FieldType) + " " + f.Name);
            foreach (var pr in typeof(OSGeo.GDAL.ColorEntry).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                p("  prop " + Pretty(pr.PropertyType) + " " + pr.Name);
            p("");
            p("--- GDALWarpAppOptions / GDALTranslateOptions / GDALDEMProcessingOptions 构造 ---");
            foreach (var t in new[] { typeof(OSGeo.GDAL.GDALWarpAppOptions), typeof(OSGeo.GDAL.GDALTranslateOptions), typeof(OSGeo.GDAL.GDALDEMProcessingOptions) })
            {
                p("  [" + t.Name + "] 字段: " + string.Join(", ", t.GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name + ":" + Pretty(f.FieldType))));
                foreach (var c in t.GetConstructors())
                    p("    ctor(" + string.Join(", ", c.GetParameters().Select(x => Pretty(x.ParameterType) + " " + x.Name)) + ")");
            }
            p("");
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        }

        private static string Sig(MethodInfo m)
        {
            var ps = m.GetParameters().Select(x => Pretty(x.ParameterType) + " " + x.Name + (x.HasDefaultValue ? " = ..." : ""));
            return m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", ps) + ")";
        }

        private static string Pretty(Type t)
        {
            if (!t.IsGenericType) return t.Name;
            var name = t.Name.Substring(0, t.Name.IndexOf('`'));
            return name + "<" + string.Join(",", t.GetGenericArguments().Select(Pretty)) + ">";
        }
    }
}
