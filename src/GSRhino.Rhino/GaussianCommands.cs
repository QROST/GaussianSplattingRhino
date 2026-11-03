using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using GSRhino.Core;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace GSRhino.Rhino;

[Guid("D973F27C-A64B-49A9-A2CD-E160EC8A5BB5")]
public sealed class GSRhinoImportCommand : Command
{
    public override string EnglishName => "GSRhinoImport";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            ImportTarget target=GSRhinoHost.CaptureTarget(doc);
            using var dialog=new Eto.Forms.OpenFileDialog { Title="导入本地 Gaussian Splat 场景（自动轴向和单位）",MultiSelect=false };
            GSRhinoPanel.AddFilters(dialog);
            if(dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc))!=Eto.Forms.DialogResult.Ok)return Result.Cancel;
            global::Rhino.RhinoApp.WriteLine("正在以自动轴向、自动单位和 1 倍比例导入；自定义设置请使用 GSRhinoPanel。后台准备期间显示包围盒，请保持目标文档单位不变。");
            _=ImportAsync(dialog.FileName,target); return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(GSRhinoHost.FriendlyError(ex));return Result.Failure;}
    }

    private static async Task ImportAsync(string path,ImportTarget target)
    {
        try
        {
            string result=await GaussianDocument.ImportAsync(path,target,1,"Auto",true,CancellationToken.None).ConfigureAwait(false);
            GSRhinoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(result));
        }
        catch(Exception ex){GSRhinoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine("GS 导入未完成："+GSRhinoHost.FriendlyError(ex)));}
    }
}

[Guid("5E803B24-B6A4-49BE-8035-BDC7990C4842")]
public sealed class GSRhinoModeCommand : Command
{
    public override string EnglishName=>"GSRhinoMode";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            GaussianViewport.Activate(doc);
            global::Rhino.RhinoApp.WriteLine("已启用 GSRhino (Experimental)。使用原生深度遮挡；详细效果需按本机验收。透明物体交错尚未支持，可运行 GSRhinoProbe 创建验收小样。资源缺失时显示包围盒。");
            return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(GSRhinoHost.FriendlyError(ex));return Result.Failure;}
    }
}

[Guid("40609F4F-B2A5-4B07-890D-6AC55E7A1E9E")]
public sealed class GSRhinoDiagnosticsCommand : Command
{
    public override string EnglishName=>"GSRhinoDiagnostics";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        global::Rhino.RhinoApp.WriteLine($"GSRhino {GSRhinoPlugin.DisplayVersion}; Rhino {(global::Rhino.RhinoApp.Version)}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
        global::Rhino.RhinoApp.WriteLine(GaussianViewport.Diagnostics(doc));
        return Result.Success;
    }
}

[Guid("1D521EC2-0B39-4B8B-8273-405E5F4B9B2F")]
public sealed class GSRhinoProbeCommand : Command
{
    public override string EnglishName=>"GSRhinoProbe";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            ImportTarget target=GSRhinoHost.CaptureTarget(doc);
            global::Rhino.RhinoApp.WriteLine("正在创建离线 GS 遮挡试验：两组完整高斯、一个不透明 Brep 立方体和一个 Mesh。此命令会添加试验对象，可 Undo 撤销。运行后请旋转相机检查前后遮挡与柔和透明边缘。");
            _=CreateAsync(target); return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(GSRhinoHost.FriendlyError(ex));return Result.Failure;}
    }

    private static async Task CreateAsync(ImportTarget target)
    {
        try
        {
            string path=await Task.Run(WriteFixture).ConfigureAwait(false);
            string result=await GaussianDocument.ImportAsync(path,target,1,"Z",false,CancellationToken.None).ConfigureAwait(false);
            await GSRhinoHost.OnUi(()=>
            {
                RhinoDoc doc=RhinoDoc.FromRuntimeSerialNumber(target.Serial)??throw new InvalidOperationException("试验目标文档已关闭。");
                if(RhinoDoc.ActiveDoc?.RuntimeSerialNumber!=target.Serial||doc.ModelUnitSystem!=target.Units||doc.IsClosing||doc.IsReadOnly||doc.IsCommandRunning||doc.UndoRecordingIsActive)
                    throw new InvalidOperationException("目标文档状态改变。已导入 GS；遮挡测试几何未添加。");
                uint undo=doc.BeginUndoRecord("添加 GS 遮挡测试几何");
                if(undo==0)throw new InvalidOperationException("无法建立测试几何撤销记录。");
                try
                {
                    using Brep cube=new BoundingBox(new Point3d(-.35,-.8,-1.5),new Point3d(.35,.8,1.5)).ToBrep();
                    using var attrs=new ObjectAttributes{LayerIndex=GaussianDocument.VisibleLayer(doc),Name="GSRhino probe · opaque Brep",ObjectColor=Color.FromArgb(65,95,160),ColorSource=ObjectColorSource.ColorFromObject};
                    if(doc.Objects.AddBrep(cube,attrs)==Guid.Empty) throw new InvalidOperationException("无法添加 Brep 遮挡试验几何。");
                    using var mesh=Mesh.CreateFromBox(new BoundingBox(new Point3d(1.2,-1.0,-.25),new Point3d(2.3,1.0,.25)),1,1,1);
                    attrs.Name="GSRhino probe · opaque Mesh";attrs.ObjectColor=Color.FromArgb(170,140,65);
                    if(doc.Objects.AddMesh(mesh,attrs)==Guid.Empty) throw new InvalidOperationException("无法添加 Mesh 遮挡试验几何。已添加的几何可 Undo 撤销。");
                }
                finally{doc.EndUndoRecord(undo);}
                var viewport=doc.Views.ActiveView?.ActiveViewport;
                if(viewport is not null)
                {
                    viewport.ChangeToPerspectiveProjection(true,50);
                    viewport.SetCameraLocations(Point3d.Origin,new Point3d(5,-12,4));
                    viewport.ZoomBoundingBox(new BoundingBox(new Point3d(-3,-2,-2.5),new Point3d(3,2,2.5)));
                }
                doc.Objects.UnselectAll();doc.Views.Redraw();global::Rhino.RhinoApp.WriteLine(result);
                global::Rhino.RhinoApp.WriteLine("试验采用两次 Undo：先移除遮挡几何，再移除 GS 块。此处未自动判断视觉通过；可运行 GSRhinoDiagnostics 查看状态。");
                return true;
            }).ConfigureAwait(false);
        }
        catch(Exception ex){GSRhinoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine("GS 试验未完成："+GSRhinoHost.FriendlyError(ex)));}
    }
    private static string WriteFixture()
    {
        const double sh0=.28209479177387814;
        var data=new StringBuilder("ply\nformat ascii 1.0\ncomment GSRhino local GS occlusion probe by CuriousArc and qrost\nelement vertex 18\nproperty float x\nproperty float y\nproperty float z\nproperty float f_dc_0\nproperty float f_dc_1\nproperty float f_dc_2\nproperty float opacity\nproperty float scale_0\nproperty float scale_1\nproperty float scale_2\nproperty float rot_0\nproperty float rot_1\nproperty float rot_2\nproperty float rot_3\nend_header\n");
        for(int layer=0;layer<2;layer++)for(int row=0;row<3;row++)for(int column=0;column<3;column++)
        {
            double angle=(column-1)*.3;
            double[] values={ (column-1)*1.6,layer==0?-.45:.45,(row-1)*1.15,
                ((layer==0?.95:.15)-.5)/sh0,((layer==0?.25:.85)-.5)/sh0,((layer==0?.15:.75)-.5)/sh0,
                1.2,Math.Log(.55),Math.Log(.12),Math.Log(.28),Math.Cos(angle/2),0,Math.Sin(angle/2),0 };
            data.AppendLine(string.Join(" ",values.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
        }
        string path=Path.Combine(LocalStorage.Root,"inputs","gsrhino-gs-occlusion-probe.ply");
        LocalStorage.WriteAtomic(path,Encoding.ASCII.GetBytes(data.ToString()));return path;
    }
}

[Guid("270EA05A-39CF-4850-9EC2-A8D676BD465C")]
public sealed class GSRhinoExportCommand : Command
{
    public override string EnglishName => "GSRhinoExport";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        try
        {
            GaussianDocument.ExportSnapshot snapshot = GaussianDocument.CaptureExport(doc, selectedOnly: true);
            using var dialog = new Eto.Forms.SaveFileDialog { Title = "导出选中的 GS 块", FileName = "gaussian-scene.usdz" };
            GSRhinoPanel.AddExportFilters(dialog);
            if (dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)) != Eto.Forms.DialogResult.Ok) return Result.Cancel;
            global::Rhino.RhinoApp.WriteLine("正在后台导出选中 GS；使用当前文档坐标与单位。如需全部导出或取消功能，请从 GSRhinoPanel 发起。");
            _ = ExportAsync(dialog.FileName, snapshot);
            return Result.Success;
        }
        catch (Exception ex) { global::Rhino.RhinoApp.WriteLine(GSRhinoHost.FriendlyError(ex)); return Result.Failure; }
    }
    private static async Task ExportAsync(string path, GaussianDocument.ExportSnapshot snapshot)
    {
        try
        {
            string result = await GaussianDocument.ExportAsync(path, snapshot, CancellationToken.None).ConfigureAwait(false);
            GSRhinoHost.Ui(() => global::Rhino.RhinoApp.WriteLine(result));
        }
        catch (Exception ex) { GSRhinoHost.Ui(() => global::Rhino.RhinoApp.WriteLine("GS 导出未完成：" + GSRhinoHost.FriendlyError(ex))); }
    }
}
