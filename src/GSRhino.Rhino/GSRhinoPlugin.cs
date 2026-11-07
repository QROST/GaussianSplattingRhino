using UiText = global::GSRhino.Core.UiText;
using System.Reflection;
using System.Runtime.InteropServices;
using Rhino.Commands;
using Rhino.PlugIns;
namespace GSRhino.Rhino;

[Guid("A4A66804-8222-4C0A-B126-CFA51A914863")]
public sealed class GSRhinoPlugin : PlugIn
{
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
    internal static string DisplayVersion => typeof(GSRhinoPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0-beta.1";
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        int? language = null;
        try { language = Convert.ToInt32(global::Rhino.UI.Localization.CurrentLanguageId); }
        catch (Exception) { /* A missing host locale falls back to the process UI language. */ }
        GSRhino.Core.UiText.Initialize(language, System.Globalization.CultureInfo.CurrentUICulture.Name);
        global::Rhino.UI.Panels.RegisterPanel(this, typeof(GSRhinoPanel), "GSRhino", null!, global::Rhino.UI.PanelType.PerDoc);
        GaussianViewport.Initialize(this);
        global::Rhino.RhinoApp.WriteLine(UiText.Format("GSRhino {0} · CuriousArc · qrost. Run GSRhinoPanel to open the local Gaussian Splat panel.", DisplayVersion));
        return LoadReturnCode.Success;
    }
    protected override void OnShutdown()
    {
        GaussianViewport.Shutdown();
        GaussianDocument.Shutdown();
        base.OnShutdown();
    }
}

[Guid("0DDA208F-B010-4F86-A790-A46A4F93794B")]
public sealed class GSRhinoPanelCommand : Command
{
    public override string EnglishName => "GSRhinoPanel";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode)
    {
        global::Rhino.UI.Panels.OpenPanel(typeof(GSRhinoPanel));
        return Result.Success;
    }
}
