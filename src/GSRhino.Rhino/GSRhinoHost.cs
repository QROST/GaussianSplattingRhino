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
        ? "内存不足，操作未完成；原始文件保留，可释放内存后重试。"
        : e is OperationCanceledException ? "本地导入已取消；原始文件保留。" : e.Message;
    internal static ImportTarget CaptureTarget(RhinoDoc doc)
    {
        if (global::Rhino.RhinoApp.InvokeRequired) throw new InvalidOperationException("请从 Rhino 界面发起此操作。");
        RequireTarget(doc.RuntimeSerialNumber);
        return new(doc.RuntimeSerialNumber, doc.ModelUnitSystem);
    }
    internal static RhinoDoc RequireTarget(uint serial)
    {
        RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(serial) ?? throw new InvalidOperationException("目标文档已关闭。");
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != serial || doc.IsClosing || doc.IsReadOnly)
            throw new InvalidOperationException("请先激活可写的目标 Rhino 文档。");
        if (doc.Views.ActiveView is null || doc.Views.ActiveView is global::Rhino.Display.RhinoPageView)
            throw new InvalidOperationException("请切换到原生模型视窗；当前版本不在布局细节中显示 GS。");
        return doc;
    }
}
