using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using OpenFileDialog = Eto.Forms.OpenFileDialog;
using SaveFileDialog = Eto.Forms.SaveFileDialog;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace GSRhino.Rhino;

[Guid("94AA050D-4927-40A8-8B5E-9801DC198832")]
public sealed class GSRhinoPanel : Panel, IPanel
{
    private readonly uint _serial;
    private readonly TextBox _path = new() { ReadOnly = true, PlaceholderText = "选择本地 Gaussian Splat 文件" };
    private readonly TextBox _scale = new() { Text = "1" };
    private readonly DropDown _axis = new() { DataStore = new[] { "Auto · 使用源元数据，缺失时 Z 向上", "Z 向上", "Y 向上" }, SelectedIndex = 0 };
    private readonly DropDown _units = new() { DataStore = new[] { "Auto · 使用源单位，缺失时 1:1", "1 文件单位 = 1 文档单位" }, SelectedIndex = 0 };
    private readonly Label _document = Info("");
    private readonly Label _status = Info("就绪 · 选择本地文件开始");
    private readonly Label _elapsed = Info("");
    private readonly TextArea _feedback = new() { ReadOnly = true, Wrap = true, Height = 135 };
    private readonly Button _choose = new() { Text = "选择文件…" };
    private readonly Button _import = new() { Text = "导入当前文档", Enabled = false };
    private readonly Button _cancel = new() { Text = "取消操作", Enabled = false };
    private readonly DropDown _exportScope = new() { DataStore = new[] { "选中的顶层 GS", "全部顶层 GS（包含隐藏 / 锁定）" }, SelectedIndex = 0 };
    private readonly Button _export = new() { Text = "导出 USD / USDZ…" };
    private readonly Button _mode = new() { Text = "启用 / 重新加载 GS 显示" };
    private readonly Button _diagnostics = new() { Text = "诊断信息" };
    private readonly UITimer _timer = new() { Interval = 1 };
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _operation;
    private bool _disposed;

    public GSRhinoPanel(uint documentSerialNumber)
    {
        _serial = documentSerialNumber;
        _choose.Click += (_, _) => RunLocal(ChooseFile);
        _import.Click += (_, _) => _ = ImportAsync();
        _export.Click += (_, _) => _ = ExportAsync();
        _cancel.Click += (_, _) => { _operation?.Cancel(); _status.Text = "正在取消；等待当前读取退出…"; _cancel.Enabled = false; };
        _mode.Click += (_, _) => RunLocal(() => { GaussianViewport.Activate(Document()); _feedback.Text = "已启用 GSRhino 显示。导航期间显示包围盒，视图静止后恢复 GS。"; });
        _diagnostics.Click += (_, _) => RunLocal(() => _feedback.Text = GaussianViewport.Diagnostics(Document()));
        _timer.Elapsed += (_, _) => { if (!_disposed) _elapsed.Text = $"{(int)_clock.Elapsed.TotalSeconds:N0} 秒"; };
        var body = new StackLayout
        {
            Padding = 12, Spacing = 9, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label { Text = "GSRhino", Font = new Font(SystemFont.Bold, 19) },
                Info("在 Rhino 模型视窗中查看完整 Gaussian Splats"), _document,
                new Label { Text = "本地文件" }, _path, _choose,
                Info("PLY · SPLAT · SPZ · USD / USDA / USDC · USDZ"),
                new Label { Text = "源向上轴" }, _axis,
                new Label { Text = "源单位" }, _units,
                new Label { Text = "额外比例倍率" }, _scale,
                _import,
                new Label { Text = "GS 场景导出" }, _exportScope, _export,
                Info("仅导出顶层 GS；不包含 Mesh / Brep。全部导出会将隐藏 GS 也作为可见资产写入。"),
                _cancel, _status, _elapsed, _feedback,
                _mode, _diagnostics,
                Info("完整数据保留，不设固定数量上限。读取、投影和排序在后台执行；准备期间显示包围盒。"),
                Info("3dm 保存 GS 引用，本地缓存需一并保留或随文件传递。透明模型交错、布局和打印暂不支持。"),
                Info("离线遮挡小样：GSRhinoProbe。USD 导入 / 导出需要本机可用的 OpenUSD Python 环境。"),
                Info("GSRhino " + GSRhinoPlugin.DisplayVersion + " · CuriousArc · qrost")
            }
        };
        Content = new Scrollable { Content = body };
        UpdateDocument();
    }
    private static Label Info(string text) => new() { Text = text, Wrap = WrapMode.Word };
    private static Control Row(params Control[] controls)
    {
        var row = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in controls) row.Items.Add(new StackLayoutItem(control, true));
        return row;
    }
    private RhinoDoc Document() => RhinoDoc.FromRuntimeSerialNumber(_serial) ?? throw new InvalidOperationException("目标文档已关闭，请在目标文档重新打开面板。");
    private void UpdateDocument()
    {
        var doc = RhinoDoc.FromRuntimeSerialNumber(_serial);
        _document.Text = doc is null ? "目标文档已关闭" : $"{(string.IsNullOrWhiteSpace(doc.Name) ? "未命名文档" : doc.Name)} · {doc.ModelUnitSystem}";
    }
    private void RunLocal(Action action)
    {
        if (_disposed || _operation is not null) return;
        try { action(); }
        catch (Exception ex) { _status.Text = "操作未完成"; _feedback.Text = GSRhinoHost.FriendlyError(ex); }
    }
    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Title = "选择本地 Gaussian Splat 场景", MultiSelect = false };
        AddFilters(dialog);
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        _path.Text = dialog.FileName; _import.Enabled = true;
        _status.Text = "文件已选择 · 确认轴向、单位与比例后导入";
    }
    internal static void AddFilters(OpenFileDialog dialog)
    {
        dialog.Filters.Add(new FileFilter("Gaussian Splat 场景", ".ply", ".splat", ".spz", ".usd", ".usda", ".usdc", ".usdz"));
        dialog.Filters.Add(new FileFilter("Gaussian PLY", ".ply"));
        dialog.Filters.Add(new FileFilter("SPLAT / SPZ", ".splat", ".spz"));
        dialog.Filters.Add(new FileFilter("USD 场景", ".usd", ".usda", ".usdc", ".usdz"));
    }
    private async Task ImportAsync()
    {
        if (_disposed || _operation is not null) return;
        CancellationTokenSource? cancellation = null;
        try
        {
            ImportTarget target = GSRhinoHost.CaptureTarget(Document());
            if (!double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double scale) &&
                !double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
                throw new ArgumentException("请输入有效的比例倍率。");
            if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException("比例倍率必须大于零且有限。");
            if (!File.Exists(_path.Text)) throw new FileNotFoundException("请选择存在的本地文件。");
            string axis = _axis.SelectedIndex switch { 1 => "Z", 2 => "Y", _ => "Auto" };
            bool sourceUnits = _units.SelectedIndex == 0;
            cancellation = new CancellationTokenSource(); _operation = cancellation;
            SetBusy(true); _clock.Restart(); _timer.Start(); _feedback.Text = ""; UpdateDocument();
            var progress = new Progress<string>(value => GSRhinoHost.Ui(() => { if (!_disposed && ReferenceEquals(_operation, cancellation)) _status.Text = value; }));
            string result = await GaussianDocument.ImportAsync(_path.Text, target, scale, axis, sourceUnits, cancellation.Token, progress).ConfigureAwait(false);
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = "导入完成"; _feedback.Text = result; } return true; }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = ex is OperationCanceledException ? "已取消" : "导入未完成"; _feedback.Text = GSRhinoHost.FriendlyError(ex); } return true; }).ConfigureAwait(false);
        }
        finally
        {
            await GSRhinoHost.OnUi(() =>
            {
                if (ReferenceEquals(_operation, cancellation)) _operation = null;
                cancellation?.Dispose();
                if (!_disposed) { _timer.Stop(); _clock.Stop(); SetBusy(false); }
                return true;
            }).ConfigureAwait(false);
        }
    }
    internal static void AddExportFilters(SaveFileDialog dialog)
    {
        dialog.Filters.Add(new FileFilter("USDZ · 单文件便携场景", ".usdz"));
        dialog.Filters.Add(new FileFilter("USD · 二进制场景", ".usd"));
        dialog.Filters.Add(new FileFilter("USDC · 二进制场景", ".usdc"));
        dialog.Filters.Add(new FileFilter("USDA · 文本场景", ".usda"));
    }
    private async Task ExportAsync()
    {
        if (_disposed || _operation is not null) return;
        CancellationTokenSource? cancellation = null;
        try
        {
            GaussianDocument.ExportSnapshot snapshot = GaussianDocument.CaptureExport(Document(), _exportScope.SelectedIndex == 0);
            using var dialog = new SaveFileDialog { Title = "导出完整 Gaussian Splat 场景", FileName = "gaussian-scene.usdz" };
            AddExportFilters(dialog);
            if (dialog.ShowDialog(this) != DialogResult.Ok) return;
            cancellation = new CancellationTokenSource(); _operation = cancellation;
            SetBusy(true); _clock.Restart(); _timer.Start(); _feedback.Text = "";
            var progress = new Progress<string>(value => GSRhinoHost.Ui(() => { if (!_disposed && ReferenceEquals(_operation, cancellation)) _status.Text = value; }));
            string result = await GaussianDocument.ExportAsync(dialog.FileName, snapshot, cancellation.Token, progress).ConfigureAwait(false);
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = "导出完成"; _feedback.Text = result; } return true; }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = ex is OperationCanceledException ? "已取消" : "导出未完成"; _feedback.Text = GSRhinoHost.FriendlyError(ex); } return true; }).ConfigureAwait(false);
        }
        finally
        {
            await GSRhinoHost.OnUi(() =>
            {
                if (ReferenceEquals(_operation, cancellation)) _operation = null;
                cancellation?.Dispose();
                if (!_disposed) { _timer.Stop(); _clock.Stop(); SetBusy(false); }
                return true;
            }).ConfigureAwait(false);
        }
    }
    private void SetBusy(bool busy)
    {
        _export.Enabled = _exportScope.Enabled = _choose.Enabled = _mode.Enabled = _diagnostics.Enabled = _axis.Enabled = _units.Enabled = _scale.Enabled = !busy;
        _import.Enabled = !busy && !string.IsNullOrWhiteSpace(_path.Text);
        _cancel.Enabled = busy;
    }
    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason) { if (!_disposed) UpdateDocument(); }
    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }
    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { _operation?.Cancel(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _operation?.Cancel(); _timer.Stop(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
}
