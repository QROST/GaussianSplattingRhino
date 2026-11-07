using UiText = global::GSRhino.Core.UiText;
using Eto.Forms;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace GSRhino.Rhino;

internal sealed record ImportTarget(uint Serial, global::Rhino.UnitSystem Units);

internal static class GSRhinoHost
{
    internal static void Ui(Action action)
    {
        if (global::Rhino.RhinoApp.InvokeRequired) Application.Instance.Invoke(action); else action();
    }
    internal static Task<T> OnUi<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui(() => { try { result.SetResult(action()); } catch (Exception e) { result.SetException(e); } });
        return result.Task;
    }
    internal static string FriendlyError(Exception e) => e is OutOfMemoryException
        ? UiText.Get("Out of memory. Operation not completed; original files retained. Free memory and try again.")
        : e is OperationCanceledException ? UiText.Get("Local operation cancelled; original files retained.") : GSRhino.Core.UiText.Get(e.Message);
    internal static ImportTarget CaptureTarget(RhinoDoc doc)
    {
        if (global::Rhino.RhinoApp.InvokeRequired) throw new InvalidOperationException(UiText.Get("Start this operation from the Rhino interface."));
        RequireTarget(doc.RuntimeSerialNumber);
        return new(doc.RuntimeSerialNumber, doc.ModelUnitSystem);
    }
    internal static RhinoDoc RequireTarget(uint serial)
    {
        RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(serial) ?? throw new InvalidOperationException(UiText.Get("The target document is closed."));
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != serial || doc.IsClosing || doc.IsReadOnly)
            throw new InvalidOperationException(UiText.Get("Activate a writable target Rhino document first."));
        if (doc.Views.ActiveView is null || doc.Views.ActiveView is global::Rhino.Display.RhinoPageView)
            throw new InvalidOperationException(UiText.Get("Switch to a native modeling viewport. GS display in layout details is not supported in this version."));
        return doc;
    }
}
