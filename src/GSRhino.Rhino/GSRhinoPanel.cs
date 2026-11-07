using System.Diagnostics;
using System.Globalization;
using GSRhino.Core;
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
    private readonly TextBox _path = new() { ReadOnly = true, PlaceholderText = UiText.Get("Select a local Gaussian Splat file") };
    private readonly TextBox _scale = new() { Text = "1" };
    private readonly DropDown _axis = new() { DataStore = new[] { UiText.Get("Auto · Source metadata; Z-up if unspecified"), UiText.Get("Z-up"), UiText.Get("Y-up") }, SelectedIndex = 0 };
    private readonly DropDown _units = new() { DataStore = new[] { UiText.Get("Auto · Source units; 1:1 if unspecified"), UiText.Get("1 file unit = 1 document unit") }, SelectedIndex = 0 };
    private readonly Label _document = Info("");
    private readonly Label _status = Info(UiText.Get("Ready · Select a local file to begin"));
    private readonly Label _elapsed = Info("");
    private readonly TextArea _feedback = new() { ReadOnly = true, Wrap = true, Height = 135 };
    private readonly Button _choose = new() { Text = UiText.Get("Choose file…") };
    private readonly Button _import = new() { Text = UiText.Get("Import into current document"), Enabled = false };
    private readonly Button _cancel = new() { Text = UiText.Get("Cancel operation"), Enabled = false };
    private readonly DropDown _exportScope = new() { DataStore = new[] { UiText.Get("Selected top-level GS"), UiText.Get("All top-level GS (including hidden / locked)") }, SelectedIndex = 0 };
    private readonly Button _export = new() { Text = UiText.Get("Export USD / USDZ…") };
    private readonly Button _mode = new() { Text = UiText.Get("Enable / reload GS display") };
    private readonly Button _diagnostics = new() { Text = UiText.Get("Diagnostics") };
    private readonly Label _usdStatus = Info(UiText.Get("USD / USDZ · Runtime not checked"));
    private readonly Button _checkUsd = new() { Text = UiText.Get("Check USD runtime") };
    private readonly Button _usdHelp = new() { Text = UiText.Get("USD setup instructions") };
    private readonly Button _copyUsdCommand = new() { Text = UiText.Get("Copy setup command") };
    private CancellationTokenSource? _runtimeCheck;
    private readonly UITimer _timer = new() { Interval = 1 };
    private readonly Stopwatch _clock = new();
    private CancellationTokenSource? _operation;
    private bool _disposed;

    public GSRhinoPanel(uint documentSerialNumber)
    {
        _serial = documentSerialNumber;
        _checkUsd.Click += (_, _) => _ = CheckUsdAsync();
        _usdHelp.Click += (_, _) => RunLocal(ShowUsdSetup);
        _copyUsdCommand.Click += (_, _) => RunLocal(() =>
        {
            string command = UsdRuntime.SetupCommand(UsdRuntime.SetupScript, OperatingSystem.IsWindows());
            Clipboard.Instance.Text = command;
            ShowUsdSetup();
            _usdStatus.Text = UiText.Get("Setup command copied · Run it in your terminal, then check again");
        });
        _choose.Click += (_, _) => RunLocal(ChooseFile);
        _import.Click += (_, _) => _ = ImportAsync();
        _export.Click += (_, _) => _ = ExportAsync();
        _cancel.Click += (_, _) => { _operation?.Cancel(); _status.Text = UiText.Get("Cancelling; waiting for the current read to stop…"); _cancel.Enabled = false; };
        _mode.Click += (_, _) => RunLocal(() => { GaussianViewport.Activate(Document()); _feedback.Text = UiText.Get("GSRhino display enabled. Bounding boxes appear during navigation; GS returns when the view settles."); });
        _diagnostics.Click += (_, _) => RunLocal(() => _feedback.Text = GaussianViewport.Diagnostics(Document()));
        _timer.Elapsed += (_, _) => { if (!_disposed) _elapsed.Text = UiText.Format("{0:N0} s", (int)_clock.Elapsed.TotalSeconds); };
        var body = new StackLayout
        {
            Padding = 12, Spacing = 9, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                new Label { Text = "GSRhino", Font = new Font(SystemFont.Bold, 19) },
                Info(UiText.Get("View complete Gaussian Splats in Rhino's modeling viewport")), _document,
                new Label { Text = UiText.Get("Local file") }, _path, _choose,
                Info("PLY · SPLAT · SPZ · USD / USDA / USDC · USDZ"),
                new Label { Text = UiText.Get("Source up axis") }, _axis,
                new Label { Text = UiText.Get("Source units") }, _units,
                new Label { Text = UiText.Get("Additional scale factor") }, _scale,
                _import,
                new Label { Text = UiText.Get("Export GS scene") }, _exportScope, _export,
                Info(UiText.Get("Exports top-level GS only; no Mesh / Brep. Export all includes hidden GS as visible assets.")),
                _cancel, _status, _elapsed, _feedback,
                _mode, _diagnostics,
                Info(UiText.Get("Full data retained, with no fixed count limit. Reading, projection and sorting run in the background; bounding boxes appear while preparing.")),
                Info(UiText.Get("3dm stores GS references. Keep or share the local cache with the file. Interleaved transparent geometry, layouts and printing are not supported yet.")),
                Info(UiText.Get("Offline occlusion sample: GSRhinoProbe. USD import / export requires the optional local runtime.")),
                _usdStatus, _checkUsd, Row(_usdHelp, _copyUsdCommand),
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
    private RhinoDoc Document() => RhinoDoc.FromRuntimeSerialNumber(_serial) ?? throw new InvalidOperationException(UiText.Get("The target document is closed. Reopen the panel in the target document."));
    private void UpdateDocument()
    {
        var doc = RhinoDoc.FromRuntimeSerialNumber(_serial);
        _document.Text = doc is null ? UiText.Get("Target document closed") : $"{(string.IsNullOrWhiteSpace(doc.Name) ? UiText.Get("Untitled document") : doc.Name)} · {doc.ModelUnitSystem}";
    }
    private void RunLocal(Action action)
    {
        if (_disposed || _operation is not null) return;
        try { action(); }
        catch (Exception ex) { _status.Text = UiText.Get("Operation not completed"); _feedback.Text = GSRhinoHost.FriendlyError(ex); }
    }
    private void ChooseFile()
    {
        using var dialog = new OpenFileDialog { Title = UiText.Get("Select a local Gaussian Splat scene"), MultiSelect = false };
        AddFilters(dialog);
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        _path.Text = dialog.FileName; _import.Enabled = true;
        _status.Text = UiText.Get("File selected · Confirm axis, units and scale before importing");
    }
    internal static void AddFilters(OpenFileDialog dialog)
    {
        dialog.Filters.Add(new FileFilter(UiText.Get("Gaussian Splat scene"), ".ply", ".splat", ".spz", ".usd", ".usda", ".usdc", ".usdz"));
        dialog.Filters.Add(new FileFilter("Gaussian PLY", ".ply"));
        dialog.Filters.Add(new FileFilter("SPLAT / SPZ", ".splat", ".spz"));
        dialog.Filters.Add(new FileFilter(UiText.Get("USD scene"), ".usd", ".usda", ".usdc", ".usdz"));
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
                throw new ArgumentException(UiText.Get("Enter a valid scale factor."));
            if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException(UiText.Get("The scale factor must be finite and greater than zero."));
            if (!File.Exists(_path.Text)) throw new FileNotFoundException(UiText.Get("Select an existing local file."));
            string axis = _axis.SelectedIndex switch { 1 => "Z", 2 => "Y", _ => "Auto" };
            bool sourceUnits = _units.SelectedIndex == 0;
            cancellation = new CancellationTokenSource(); _operation = cancellation;
            SetBusy(true); _clock.Restart(); _timer.Start(); _feedback.Text = ""; UpdateDocument();
            var progress = new Progress<string>(value => GSRhinoHost.Ui(() => { if (!_disposed && ReferenceEquals(_operation, cancellation)) _status.Text = value; }));
            string result = await GaussianDocument.ImportAsync(_path.Text, target, scale, axis, sourceUnits, cancellation.Token, progress).ConfigureAwait(false);
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = UiText.Get("Added to document · Viewport shows display preparation status"); _feedback.Text = result; } return true; }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = ex is OperationCanceledException ? UiText.Get("Cancelled") : UiText.Get("Import not completed"); _feedback.Text = GSRhinoHost.FriendlyError(ex); } return true; }).ConfigureAwait(false);
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
        dialog.Filters.Add(new FileFilter(UiText.Get("USDZ · Portable single-file scene"), ".usdz"));
        dialog.Filters.Add(new FileFilter(UiText.Get("USD · Binary scene"), ".usd"));
        dialog.Filters.Add(new FileFilter(UiText.Get("USDC · Binary scene"), ".usdc"));
        dialog.Filters.Add(new FileFilter(UiText.Get("USDA · Text scene"), ".usda"));
    }
    private async Task ExportAsync()
    {
        if (_disposed || _operation is not null) return;
        CancellationTokenSource? cancellation = null;
        try
        {
            GaussianDocument.ExportSnapshot snapshot = GaussianDocument.CaptureExport(Document(), _exportScope.SelectedIndex == 0);
            using var dialog = new SaveFileDialog { Title = UiText.Get("Export complete Gaussian Splat scene"), FileName = "gaussian-scene.usdz" };
            AddExportFilters(dialog);
            if (dialog.ShowDialog(this) != DialogResult.Ok) return;
            cancellation = new CancellationTokenSource(); _operation = cancellation;
            SetBusy(true); _clock.Restart(); _timer.Start(); _feedback.Text = "";
            var progress = new Progress<string>(value => GSRhinoHost.Ui(() => { if (!_disposed && ReferenceEquals(_operation, cancellation)) _status.Text = value; }));
            string result = await GaussianDocument.ExportAsync(dialog.FileName, snapshot, cancellation.Token, progress).ConfigureAwait(false);
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = UiText.Get("Export complete"); _feedback.Text = result; } return true; }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await GSRhinoHost.OnUi(() => { if (!_disposed) { _status.Text = ex is OperationCanceledException ? UiText.Get("Cancelled") : UiText.Get("Export not completed"); _feedback.Text = GSRhinoHost.FriendlyError(ex); } return true; }).ConfigureAwait(false);
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
    private void ShowUsdSetup()
    {
        string command = UsdRuntime.SetupCommand(UsdRuntime.SetupScript, OperatingSystem.IsWindows());
        string terminal = OperatingSystem.IsWindows() ? "PowerShell" : "Terminal";
        _feedback.Text = UiText.Get("USD / USDZ first-time setup\n") +
            UiText.Get("1. Install Python 3.12; adjust the command if using Python 3.11–3.14.\n") +
            UiText.Format("2. Run in {0}:\n{1}\n", terminal, command) +
            UiText.Get("3. When finished, click Check USD runtime.\n") +
            UiText.Get("Setup downloads usd-core 26.11 into a private environment. Import/export is then local.\n") +
            UiText.Get("PLY / SPLAT / SPZ work without this setup.");
    }
    private async Task CheckUsdAsync()
    {
        if (_disposed || _operation is not null || _runtimeCheck is not null) return;
        var cancellation = new CancellationTokenSource(); _runtimeCheck = cancellation;
        _checkUsd.Enabled = false; _usdStatus.Text = UiText.Get("USD / USDZ · Checking…");
        try
        {
            UsdRuntimeStatus result = await UsdRuntime.CheckAsync(ct: cancellation.Token).ConfigureAwait(false);
            await GSRhinoHost.OnUi(() =>
            {
                if (!_disposed) _usdStatus.Text = result.Ready ? UiText.Get("USD / USDZ · OpenUSD 26.11 is ready") : UiText.Get("USD / USDZ · Not ready; see setup instructions\n") + UiText.Get(result.Detail);
                return true;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await GSRhinoHost.OnUi(() => { if (!_disposed) _usdStatus.Text = UiText.Get("USD / USDZ · Check cancelled; you can check again"); return true; }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await GSRhinoHost.OnUi(() => { if (!_disposed) _usdStatus.Text = UiText.Get("USD runtime check not completed: ") + GSRhinoHost.FriendlyError(ex); return true; }).ConfigureAwait(false);
        }
        finally
        {
            await GSRhinoHost.OnUi(() =>
            {
                if (ReferenceEquals(_runtimeCheck, cancellation)) _runtimeCheck = null;
                cancellation.Dispose();
                if (!_disposed) _checkUsd.Enabled = _operation is null;
                return true;
            }).ConfigureAwait(false);
        }
    }
    private void SetBusy(bool busy)
    {
        _export.Enabled = _exportScope.Enabled = _choose.Enabled = _mode.Enabled = _diagnostics.Enabled = _axis.Enabled = _units.Enabled = _scale.Enabled = !busy;
        _import.Enabled = !busy && !string.IsNullOrWhiteSpace(_path.Text);
        _cancel.Enabled = busy;
        _usdHelp.Enabled = _copyUsdCommand.Enabled = !busy;
        _checkUsd.Enabled = !busy && _runtimeCheck is null;
    }
    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason) { if (!_disposed) UpdateDocument(); }
    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { }
    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument) { _operation?.Cancel(); _runtimeCheck?.Cancel(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) { _disposed = true; _operation?.Cancel(); _runtimeCheck?.Cancel(); _timer.Stop(); _timer.Dispose(); }
        base.Dispose(disposing);
    }
}
