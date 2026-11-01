using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SharpGIS.Models;
using SharpGIS.Processing;
using SharpGIS.Services;
using SharpGIS.UI;
using SharpGIS.UI.Dialogs;

namespace SharpGIS.SelfTest
{
    /// <summary>
    /// 英文界面自检：切到英文后，遍历主窗口与各对话框的可见文案，
    /// 断言不残留中文，并把关键界面截图存盘供目视核对。
    /// </summary>
    internal static class LangTests
    {
        public static void Run(TestLog log, string dataDir, string outDir)
        {
            log.Info("--- step=lang ---");

            // 记录原语言，结束时还原（自检进程内会退出，但保持整洁）
            string original = Lang.Current;
            Lang.Use(Lang.English);

            try
            {
                // ---- 译文表本身 ----
                log.Check(Lang.EntryCount > 600, "英文译文表条目数充足",
                    Lang.EntryCount + " 条");

                // 抽查若干关键文案的译文
                string[] samples =
                {
                    "文件(&F)", "视图(&V)", "图层(&L)", "处理(&P)", "帮助(&H)",
                    "语言(&G)", "打开栅格影像…", "波段计算器…", "土地利用变化预测（CA-Markov）…"
                };
                int bad = 0;
                var leftovers = new List<string>();
                foreach (var s in samples)
                {
                    string t = Lang.T(s);
                    if (Lang.HasChinese(t)) { bad++; leftovers.Add(s + " → " + t); }
                }
                log.Check(bad == 0, "关键菜单文案已翻译", bad == 0 ? "抽查 " + samples.Length + " 条" :
                    string.Join("; ", leftovers.ToArray()));

                // 全量检查译文表：任何一条译文里含中文都算漏翻。
                // 例外：语言自称「中文」本身就是中文，那是有意保留的。
                int untranslated = 0;
                int cjkPunct = 0;
                var untranslatedList = new List<string>();
                var punctList = new List<string>();
                foreach (var kv in Lang.SampleEntries(int.MaxValue))
                {
                    if (kv.Key == "中文") continue;
                    if (Lang.HasChinese(kv.Value))
                    {
                        untranslated++;
                        if (untranslatedList.Count < 12) untranslatedList.Add(kv.Key + " → " + kv.Value);
                    }
                    // 译文里出现中文标点也算没翻干净
                    if (Lang.HasChinesePunctuation(kv.Value))
                    {
                        cjkPunct++;
                        if (punctList.Count < 12) punctList.Add(kv.Key + " → " + kv.Value);
                    }
                }
                log.Check(untranslated == 0, "全部译文均不含中文",
                    untranslated == 0 ? ("全量 " + Lang.EntryCount + " 条") :
                    untranslated + " 条仍是中文：" + string.Join(" / ", untranslatedList.ToArray()));
                log.Check(cjkPunct == 0, "全部译文均不含中文标点",
                    cjkPunct == 0 ? ("全量 " + Lang.EntryCount + " 条") :
                    cjkPunct + " 条含中文标点：" + string.Join(" / ", punctList.ToArray()));

                // ---- 界面文案实际扫描 ----
                string tm = Path.Combine(dataDir, "TMtest.tif");
                if (!File.Exists(tm)) { log.Fail("TMtest.tif 存在", tm); return; }

                MainForm form = null;
                try
                {
                    form = new MainForm();
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(0, 0);
                    form.AutoScaleMode = AutoScaleMode.None;
                    form.Size = new Size(1440, 900);
                    form.Show();
                    Pump(3);

                    var layer = form.OpenRaster(tm, true);
                    if (layer == null) { log.Fail("英文界面中打开影像", "null"); return; }
                    Pump(2);

                // ---- 按名称反查的算子：英文下必须仍能映射到正确的 GDAL 标识 ----
                // 这类反查把显示名（小写化后）与 Lang.T 的译文做子串比较，
                // 若译文含大写字母就会失配，必须逐个断言。
                var dem = new DemDialog(layer);
                var filter = new FilterDialog(layer);
                var fusion = new FusionDialog(layer, form.Stack);
                try
                {
                    bool demOk = DemService.AlgorithmKey(Lang.T("坡度 slope")) == "slope"
                        && DemService.AlgorithmKey(Lang.T("坡向 aspect")) == "aspect"
                        && DemService.AlgorithmKey(Lang.T("彩色渲染 color-relief")) == "color-relief"
                        && DemService.AlgorithmKey(Lang.T("山体阴影 hillshade")) == "hillshade";
                    log.Check(demOk, "英文模式 DEM 算法反查正确",
                        "slope/aspect/color-relief/hillshade");

                    bool filterOk = FilterService.KindFromName(Lang.T("中值去噪")) == FilterKind.Median
                        && FilterService.KindFromName(Lang.T("高斯模糊")) == FilterKind.Gauss
                        && FilterService.KindFromName(Lang.T("拉普拉斯锐化")) == FilterKind.Sharpen
                        && FilterService.KindFromName(Lang.T("Sobel 边缘")) == FilterKind.Sobel
                        && FilterService.KindFromName(Lang.T("均值平滑")) == FilterKind.Mean;
                    log.Check(filterOk, "英文模式滤波类型反查正确", "median/gauss/sharpen/sobel/mean");

                    bool fusionOk = FusionService.MethodFromName(Lang.T("IHS 变换")) == FusionMethod.IHS
                        && FusionService.MethodFromName(Lang.T("HPF 高通滤波")) == FusionMethod.Hpf
                        && FusionService.MethodFromName(Lang.T("Brovey 变换")) == FusionMethod.Brovey;
                    log.Check(fusionOk, "英文模式融合方法反查正确", "ihs/hpf/brovey");

                    string rs = WarpService.NormalizeResample(Lang.T("双线性"));
                    log.Check(rs == "bilinear", "英文模式重采样反查正确", rs);
                }
                finally
                {
                    dem.Dispose(); filter.Dispose(); fusion.Dispose();
                }

                    // 菜单文案扫描
                    var menuText = new StringBuilder();
                    if (form.MainMenuStrip != null)
                    {
                        foreach (ToolStripItem item in form.MainMenuStrip.Items)
                            CollectMenuText(item, menuText);
                    }
                    string menu = menuText.ToString();
                    // 语言菜单里的「中文」是语言自称（不翻译），属于预期文案
                    string menuProbe = menu.Replace("中文\n", string.Empty);
                    bool menuClean = !Lang.HasChinese(menuProbe);
                    log.Check(menuClean, "英文模式菜单无中文残留",
                        menuClean ? "菜单 " + menu.Length + " 字符" : FirstChinese(menuProbe));
                    log.Check(!Lang.HasChinesePunctuation(menuProbe), "英文模式菜单无中文标点",
                        Lang.HasChinesePunctuation(menuProbe) ? FirstChinesePunct(menuProbe) : "菜单 " + menu.Length + " 字符");

                    // 截图
                    string shot = Path.Combine(outDir, "lang_mainform_en.png");
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(shot, ImageFormat.Png);
                        double sd = RenderTests.StdDev(bmp);
                        log.Check(sd > 5, "英文主窗体截图非空白",
                            "stdev=" + sd.ToString("F2") + " → " + Path.GetFileName(shot));
                    }

                    // 面板文案扫描
                    var panelText = new StringBuilder();
                    CollectText(form, panelText);
                    string panels = panelText.ToString();
                    bool panelClean = !Lang.HasChinese(panels);
                    log.Check(panelClean, "英文模式面板无中文残留",
                        panelClean ? "面板 " + panels.Length + " 字符" : FirstChinese(panels));
                    log.Check(!Lang.HasChinesePunctuation(panels), "英文模式面板无中文标点",
                        Lang.HasChinesePunctuation(panels) ? FirstChinesePunct(panels) : "面板 " + panels.Length + " 字符");

                    // ---- 各对话框文案扫描 ----
                    var dlgBad = new List<string>();
                    var dlgPunct = new List<string>();
                    int dialogCount = 0;
                    foreach (var pair in BuildDialogs(form, layer))
                    {
                        dialogCount++;
                        var dlg = pair.Item2;
                        try
                        {
                            dlg.Show();
                            Pump(1);
                            var sb = new StringBuilder();
                            sb.Append(dlg.Text).Append('\n');
                            CollectText(dlg, sb);
                            string t = sb.ToString();
                            if (Lang.HasChinese(t)) dlgBad.Add(pair.Item1 + " → " + FirstChinese(t));
                            if (Lang.HasChinesePunctuation(t))
                                dlgPunct.Add(pair.Item1 + " → " + FirstChinesePunct(t));
                            // 逐个标签页也要看
                            foreach (Control c in dlg.Controls)
                            {
                                var tabs = c as TabControl;
                                if (tabs == null) continue;
                                foreach (TabPage page in tabs.TabPages)
                                {
                                    var psb = new StringBuilder();
                                    psb.Append(page.Text).Append('\n');
                                    CollectText(page, psb);
                                    string pt = psb.ToString();
                                    if (Lang.HasChinese(pt))
                                        dlgBad.Add(pair.Item1 + "/" + page.Text + " → " + FirstChinese(pt));
                                    if (Lang.HasChinesePunctuation(pt))
                                        dlgPunct.Add(pair.Item1 + "/" + page.Text + " → " + FirstChinesePunct(pt));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            dlgBad.Add(pair.Item1 + " 抛异常 " + ex.Message);
                        }
                        finally
                        {
                            dlg.Hide();
                            dlg.Dispose();
                        }
                    }
                    log.Check(dlgBad.Count == 0, "英文模式对话框无中文残留",
                        dlgBad.Count == 0 ? ("扫描 " + dialogCount + " 个对话框") :
                        string.Join(" | ", dlgBad.ToArray()));
                    log.Check(dlgPunct.Count == 0, "英文模式对话框无中文标点",
                        dlgPunct.Count == 0 ? ("扫描 " + dialogCount + " 个对话框") :
                        string.Join(" | ", dlgPunct.ToArray()));
                }
                catch (Exception ex)
                {
                    log.Fail("英文界面自检异常", ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    if (form != null) form.Dispose();
                }
            }
            finally
            {
                Lang.Use(original);
            }
        }

        /// <summary>构造全部待检查的对话框（与界面巡检保持同一份清单）。</summary>
        private static List<Tuple<string, Form>> BuildDialogs(MainForm form, RasterLayer layer)
        {
            var stack = form.Stack;
            return new List<Tuple<string, Form>>
            {
                Tuple.Create<string, Form>("ExportDialog", new ExportDialog(layer)),
                Tuple.Create<string, Form>("WarpDialog", new WarpDialog(layer, layer.Extent, layer.PixelSizeX, 0, stack)),
                Tuple.Create<string, Form>("OverviewDialog", new OverviewDialog(layer)),
                Tuple.Create<string, Form>("BandCalcDialog", new BandCalcDialog(layer)),
                Tuple.Create<string, Form>("DemDialog", new DemDialog(layer)),
                Tuple.Create<string, Form>("FilterDialog", new FilterDialog(layer)),
                Tuple.Create<string, Form>("EnhanceDialog", new EnhanceDialog(layer)),
                Tuple.Create<string, Form>("KMeansDialog", new KMeansDialog(layer)),
                Tuple.Create<string, Form>("FusionDialog", new FusionDialog(layer, stack)),
                Tuple.Create<string, Form>("CaMarkovDialog", new CaMarkovDialog(layer, stack)),
                Tuple.Create<string, Form>("CaMarkovResultDialog",
                    new CaMarkovResultDialog(CaMarkovResultDialog.Sample(), Lang.T("结果图表"))),
                Tuple.Create<string, Form>("ProgressForm", new ProgressForm(Lang.T("进度框布局检查"))),
                // 标题是构造参数，这里传英文，验证调用方是否都已包 Lang.T
                Tuple.Create<string, Form>("CaMarkovResultDialog_EN",
                    new CaMarkovResultDialog(CaMarkovResultDialog.Sample(), "Result charts"))
            };
        }

        private static void CollectMenuText(ToolStripItem item, StringBuilder sb)
        {
            sb.Append(item.Text).Append('\n');
            var dd = item as ToolStripDropDownItem;
            if (dd != null)
                foreach (ToolStripItem child in dd.DropDownItems)
                    CollectMenuText(child, sb);
        }

        /// <summary>收集窗体上所有控件的可见文本。</summary>
        private static void CollectText(Control root, StringBuilder sb)
        {
            foreach (Control c in root.Controls)
            {
                string text = null;
                if (c is Label) text = ((Label)c).Text;
                else if (c is Button) text = ((Button)c).Text;
                else if (c is CheckBox) text = ((CheckBox)c).Text;
                else if (c is GroupBox) text = ((GroupBox)c).Text;
                else if (c is TabControl)
                {
                    foreach (TabPage p in ((TabControl)c).TabPages) text += p.Text + "|";
                }
                else if (c is ComboBox)
                {
                    foreach (object item in ((ComboBox)c).Items)
                    {
                        string s = item as string;
                        if (!string.IsNullOrEmpty(s)) text += s + "|";
                    }
                }
                else if (c is ListBox)
                {
                    foreach (object item in ((ListBox)c).Items)
                    {
                        string s = item as string;
                        if (!string.IsNullOrEmpty(s)) text += s + "|";
                    }
                }
                else if (c is TreeView)
                {
                    foreach (TreeNode n in ((TreeView)c).Nodes) text += n.Text + "|";
                }
                else if (c is TabPage) text = ((TabPage)c).Text;
                if (!string.IsNullOrEmpty(text)) sb.Append(text).Append('\n');
                CollectText(c, sb);
            }
        }

        private static string FirstChinese(string text)
        {
            foreach (var line in text.Split('\n'))
                if (Lang.HasChinese(line)) return line.Trim();
            return "(未找到)";
        }

        private static string FirstChinesePunct(string text)
        {
            foreach (var line in text.Split('\n'))
                if (Lang.HasChinesePunctuation(line)) return line.Trim();
            return "(未找到)";
        }

        private static void Pump(int rounds)
        {
            for (int i = 0; i < rounds; i++)
            {
                Application.DoEvents();
                System.Threading.Thread.Sleep(120);
            }
        }
    }
}
