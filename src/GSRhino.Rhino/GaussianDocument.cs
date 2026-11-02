using System.Globalization;
using System.Security.Cryptography;
using GSRhino.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace GSRhino.Rhino;

/// <summary>GS data stays in an immutable PLY; a normal Rhino block stores placement and undo state.</summary>
internal static class GaussianDocument
{
    internal const string Marker = "GSRhino.GS.Version";
    internal const string HashKey = "GSRhino.GS.Sha256";
    internal const string PathKey = "GSRhino.GS.AssetPath";
    internal const string ProxyKey = "GSRhino.GS.Proxy";
    private static readonly Dictionary<uint, Dictionary<string, AssetEntry>> Documents = new();
    private static readonly Dictionary<uint, HashSet<CancellationTokenSource>> Imports = new();
    private static DateTime _lastRefresh;
    private static bool _initialized;
    internal sealed record AssetData(GaussianSplat[] Splats, GaussianPreparedAsset Prepared, BoundingBox Bounds, string Hash, string Path);
    internal sealed class AssetEntry
    {
        internal AssetData? Data;
        internal string? Error;
        internal bool Loading;
        internal CancellationTokenSource? Cancellation;
    }
    internal sealed record Placement(InstanceObject Object, AssetData Asset, GaussianTransform Transform);
    private static string CacheRoot => System.IO.Path.Combine(LocalStorage.Root, "assets", "gaussians");

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        global::Rhino.RhinoApp.Idle += OnIdle;
        RhinoDoc.CloseDocument += OnClose;
    }
    internal static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        global::Rhino.RhinoApp.Idle -= OnIdle;
        RhinoDoc.CloseDocument -= OnClose;
        foreach (var assets in Documents.Values) foreach (var entry in assets.Values) entry.Cancellation?.Cancel();
        Documents.Clear();
        foreach (var pending in Imports.Values) foreach (var cancellation in pending) cancellation.Cancel();
        Imports.Clear();
    }
    private static void OnClose(object? sender, global::Rhino.DocumentEventArgs e)
    {
        if (Documents.Remove(e.DocumentSerialNumber, out var assets))
            foreach (var entry in assets.Values) entry.Cancellation?.Cancel();
        if (Imports.Remove(e.DocumentSerialNumber, out var pending)) foreach (var cancellation in pending) cancellation.Cancel();
        GaussianViewport.DocumentClosed(e.DocumentSerialNumber);
    }
    private static void OnIdle(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(500)) return;
        _lastRefresh = DateTime.UtcNow;
        foreach (RhinoDoc doc in RhinoDoc.OpenDocuments())
        {
            if (doc.IsClosing) continue;
            GaussianViewport.PruneViews(doc);
            InstanceObject[] instances = Instances(doc, null, includeHidden: true).ToArray();
            // Undo restores the native reference and reloads the immutable asset on the next idle pass.
            // Deleted assets need not retain all splats in managed memory until document close.
            if (Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets))
            {
                var hashes = instances.Select(o => Reference(o)?.Hash).ToHashSet();
                string[] unused = assets.Keys.Where(hash => !hashes.Contains(hash)).ToArray();
                foreach (string hash in unused) { assets[hash].Cancellation?.Cancel(); assets.Remove(hash); }
                if (unused.Length > 0) GaussianViewport.ForgetDocument(doc.RuntimeSerialNumber);
            }
            foreach (InstanceObject obj in instances) EnsureLoaded(doc, obj);
        }
    }
    internal static IEnumerable<InstanceObject> Instances(RhinoDoc doc, global::Rhino.Display.RhinoViewport? viewport, bool includeHidden = false)
    {
        var settings = new ObjectEnumeratorSettings
        {
            ObjectTypeFilter = ObjectType.InstanceReference,
            HiddenObjects = includeHidden, NormalObjects = true, LockedObjects = true,
            IdefObjects = false, DeletedObjects = false, ActiveObjects = true,
            ReferenceObjects = false, VisibleFilter = !includeHidden
        };
        if (viewport is not null) settings.ViewportFilter = viewport;
        foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
            if (obj is InstanceObject instance && IsGaussian(instance) && (includeHidden || instance.Visible)) yield return instance;
    }
    private static GaussianReferenceMetadata? Reference(RhinoObject obj) => GaussianReferenceMetadata.Resolve(
        obj.Attributes.GetUserString, () => obj.Attributes.GetUserStrings().AllKeys.OfType<string>());
    internal static bool IsGaussian(RhinoObject obj) => Reference(obj) is not null;
    internal static AssetData? Ready(RhinoDoc doc, RhinoObject obj)
    {
        string? hash = Reference(obj)?.Hash;
        return hash is not null && Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets) && assets.TryGetValue(hash, out var entry) ? entry.Data : null;
    }
    internal static List<Placement> Placements(RhinoDoc doc, global::Rhino.Display.RhinoViewport viewport)
    {
        var result = new List<Placement>();
        foreach (InstanceObject obj in Instances(doc, viewport))
        {
            AssetData? asset = Ready(doc, obj);
            if (asset is null) continue;
            Transform t = obj.InstanceXform;
            var linear = new GaussianMatrix(t.M00,t.M01,t.M02,t.M10,t.M11,t.M12,t.M20,t.M21,t.M22);
            // Singular placement cannot represent an ellipsoid; keep its native proxy visible.
            try { _ = linear.Inverse(); }
            catch (ArgumentException) { continue; }
            result.Add(new Placement(obj, asset, new GaussianTransform(linear, new GaussianVector(t.M03,t.M13,t.M23))));
        }
        // Rhino's object enumeration order is not a stable scene identity or transparency tie-break order.
        result.Sort((a,b)=>a.Object.Id.CompareTo(b.Object.Id));
        return result;
    }
    internal static void Reload(RhinoDoc doc)
    {
        if (Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets))
            foreach (string key in assets.Where(p => p.Value.Error is not null).Select(p => p.Key).ToArray()) assets.Remove(key);
        foreach (InstanceObject obj in Instances(doc, null, includeHidden: true)) EnsureLoaded(doc, obj);
        GaussianViewport.ForgetDocument(doc.RuntimeSerialNumber);
    }
    private static void EnsureLoaded(RhinoDoc doc, InstanceObject obj)
    {
        GaussianReferenceMetadata? reference = Reference(obj);
        if (reference is null) return;
        string hash = reference.Hash;
        if (!Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets)) Documents.Add(doc.RuntimeSerialNumber, assets = new());
        if (assets.ContainsKey(hash)) return;
        var entry = new AssetEntry { Loading = true, Cancellation = new CancellationTokenSource() }; assets.Add(hash, entry);
        uint serial = doc.RuntimeSerialNumber;
        _ = LoadAsync(serial, reference, entry);
    }
    private static async Task LoadAsync(uint serial, GaussianReferenceMetadata reference, AssetEntry entry)
    {
        string hash = reference.Hash;
        CancellationTokenSource cancellation = entry.Cancellation!;
        CancellationToken ct = cancellation.Token;
        try
        {
            AssetData data = await Task.Run(() =>
            {
                GaussianReferenceData loaded = GaussianReferenceAsset.Load(reference, CacheRoot, ct);
                if (loaded.CopiedToCache)
                    GaussianLog.Write("reference_cached", $"hash={hash}; compatible={!reference.IsNative}; splats={loaded.Splats.Length}");
                return PrepareAsset(loaded.Splats, loaded.Path, hash, ct);
            }, ct).ConfigureAwait(false);
            await GSRhinoHost.OnUi(() =>
            {
                if (!IsCurrent(serial, hash, entry)) return false;
                entry.Data = data; entry.Loading = false;
                GaussianViewport.ForgetDocument(serial);
                RhinoDoc.FromRuntimeSerialNumber(serial)?.Views.Redraw();
                return true;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await GSRhinoHost.OnUi(() =>
            {
                if (!IsCurrent(serial, hash, entry)) return false;
                entry.Error = GSRhinoHost.FriendlyError(ex); entry.Loading = false;
                global::Rhino.RhinoApp.WriteLine("GSRhino GS：资源未加载，保留包围盒。" + entry.Error);
                RhinoDoc.FromRuntimeSerialNumber(serial)?.Views.Redraw(); return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            // Cancel and Dispose are both serialized on Rhino's UI thread.
            await GSRhinoHost.OnUi(() =>
            {
                if (ReferenceEquals(entry.Cancellation, cancellation)) entry.Cancellation = null;
                cancellation.Dispose(); return true;
            }).ConfigureAwait(false);
        }
    }
    private static bool IsCurrent(uint serial, string hash, AssetEntry entry) => _initialized && Documents.TryGetValue(serial, out var assets) && assets.TryGetValue(hash, out var current) && ReferenceEquals(entry, current);
    private sealed record SourceArchive(string Hash, string Path, string Name);
    private sealed record PreparedPart(string Name, AssetData Asset, GaussianTransform Transform, bool Antialiased);
    private sealed record PreparedImport(SourceArchive Source, GaussianScene Scene, PreparedPart[] Parts);

    internal static async Task<string> ImportAsync(string path, ImportTarget target, double scale, string upAxis,
        bool useSourceUnits, CancellationToken ct, IProgress<string>? progress = null)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException("GS 比例必须大于零且有限。");
        if (upAxis is not ("Auto" or "Y" or "Z")) throw new ArgumentException("向上轴必须是 Auto、Y 或 Z。");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = cancellation.Token;
        try
        {
            await GSRhinoHost.OnUi(() =>
            {
                RhinoDoc doc = GSRhinoHost.RequireTarget(target.Serial);
                if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException("文档单位已改变，请重新确认导入设置。");
                Initialize();
                if (!Imports.TryGetValue(target.Serial, out var pending)) Imports.Add(target.Serial, pending = new());
                pending.Add(cancellation);
                return true;
            }).ConfigureAwait(false);
            PreparedImport prepared = await Task.Run(() => PrepareImport(path, token, progress), token).ConfigureAwait(false);
            progress?.Report("完整资源已准备；正在写入目标文档…");
            return await GSRhinoHost.OnUi(() => CommitImport(prepared, target, scale, upAxis, useSourceUnits, token)).ConfigureAwait(false);
        }
        finally
        {
            await GSRhinoHost.OnUi(() =>
            {
                if (Imports.TryGetValue(target.Serial, out var pending))
                {
                    pending.Remove(cancellation);
                    if (pending.Count == 0) Imports.Remove(target.Serial);
                }
                cancellation.Dispose();
                return true;
            }).ConfigureAwait(false);
        }
    }
    private static PreparedImport PrepareImport(string path, CancellationToken ct, IProgress<string>? progress)
    {
        path = System.IO.Path.GetFullPath(path);
        progress?.Report("正在存档原始文件…");
        SourceArchive original = ArchiveSource(path, ct);
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        bool usd = extension is ".usd" or ".usda" or ".usdc" or ".usdz";
        progress?.Report("正在读取完整 Gaussian Splat 场景…");
        // USD composition uses the original resolver context; its root bytes must remain unchanged.
        GaussianScene scene = GaussianSceneReader.Read(usd ? path : original.Path, ct);
        if (usd && Hash(path, ct) != original.Hash) throw new IOException("读取期间源场景文件改变，导入已停止；请保存文件后重试。");
        if (scene.Parts.Count == 0) throw new InvalidDataException("文件中没有 Gaussian Splat 数据。");
        GaussianPart[] nonempty = scene.Parts.Where(part => part.Splats.Length > 0).ToArray();
        if (nonempty.Length == 0) throw new InvalidDataException("文件中没有 Gaussian Splat 数据。");
        if (nonempty.Length != scene.Parts.Count)
            scene = scene with { Warnings = (scene.Warnings ?? Array.Empty<string>()).Concat(new[] { $"已略过 {scene.Parts.Count - nonempty.Length:N0} 个空部分；没有删除任何 GS。" }).ToArray() };
        var parts = new PreparedPart[nonempty.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            GaussianPart part = nonempty[index];
            _ = part.Transform.Linear.Inverse();
            progress?.Report($"准备部分 {index + 1:N0}/{parts.Length:N0} · {part.Splats.Length:N0} 个 GS…");
            string sourceName = System.IO.Path.GetFileNameWithoutExtension(original.Name);
            string name = usd ? part.Name : scene.Parts.Count == 1 ? sourceName : $"{sourceName} [{index + 1}]";
            parts[index] = new(name, PublishPart(part.Splats, ct), part.Transform, part.Antialiased);
        }
        return new(original, scene, parts);
    }
    private static string CommitImport(PreparedImport prepared, ImportTarget target, double scale, string axis,
        bool useSourceUnits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RhinoDoc doc = GSRhinoHost.RequireTarget(target.Serial);
        if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException("读取期间文档单位改变，请重新确认导入比例。");
        if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException("请先结束当前 Rhino 命令再导入 GS。");
        string upAxis = axis == "Auto" ? prepared.Scene.UpAxis ?? "Z" : axis;
        if (upAxis is not ("Y" or "Z")) throw new InvalidDataException("场景向上轴不是受支持的 Y 或 Z；请选择轴向后重试。");
        double unitScale = 1;
        if (useSourceUnits && prepared.Scene.MetersPerUnit is double meters)
        {
            if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
                throw new InvalidOperationException("目标文档没有可换算的标准长度单位；请使用标准单位，或选择“文件单位 = 文档单位”。");
            unitScale = meters * global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters, doc.ModelUnitSystem);
        }
        double effectiveScale = scale * unitScale;
        if (!double.IsFinite(effectiveScale) || effectiveScale <= 0) throw new InvalidDataException("源单位与比例组合后不是有效的正数。");
        Transform import = Transform.Scale(Point3d.Origin, effectiveScale);
        if (upAxis == "Y") import *= Transform.Rotation(Math.PI / 2, Vector3d.XAxis, Point3d.Origin);
        var transforms = new Transform[prepared.Parts.Length];
        BoundingBox overall = BoundingBox.Empty;
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i] = import * ToRhino(prepared.Parts[i].Transform);
            if (!transforms[i].IsValid) throw new InvalidDataException("部分的变换无效，尚未修改文档。");
            BoundingBox box = prepared.Parts[i].Asset.Bounds; box.Transform(transforms[i]);
            if (!box.IsValid) throw new InvalidDataException("变换后的 GS 包围盒超出有效数值范围，尚未修改文档。");
            overall.Union(box);
        }
        uint undo = doc.BeginUndoRecord("导入 GSRhino Gaussian Splat");
        if (undo == 0) throw new InvalidOperationException("无法建立导入撤销记录，尚未修改文档。");
        var added = new List<(Guid Id, int Definition)>();
        int layer = -1;
        bool createdLayer = !(doc.Layers.CurrentLayer.IsVisible && !doc.Layers.CurrentLayer.IsLocked);
        try
        {
            layer = VisibleLayer(doc);
            for (int i = 0; i < prepared.Parts.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                added.Add(AddBlock(doc, prepared.Parts[i], transforms[i], prepared.Source, prepared.Scene,
                    effectiveScale, upAxis, layer));
            }
        }
        catch (Exception failure)
        {
            bool rollbackFailed = false;
            foreach (var item in added.AsEnumerable().Reverse())
            {
                try
                {
                    if (!doc.Objects.Delete(item.Id, true)) rollbackFailed = true;
                    if (!doc.InstanceDefinitions.Delete(item.Definition, false, true)) rollbackFailed = true;
                }
                catch (Exception) { rollbackFailed = true; }
            }
            if (createdLayer && layer >= 0)
            {
                try { if (!doc.Layers.Delete(layer, true)) rollbackFailed = true; }
                catch (Exception) { rollbackFailed = true; }
            }
            if (rollbackFailed) throw new InvalidOperationException("导入未完成，Rhino 未能完全撤回本次对象；请使用 Undo 撤销本次导入。" + GSRhinoHost.FriendlyError(failure), failure);
            throw;
        }
        finally { doc.EndUndoRecord(undo); }
        if (!Documents.TryGetValue(target.Serial, out var assets)) Documents.Add(target.Serial, assets = new());
        foreach (PreparedPart part in prepared.Parts)
        {
            if (assets.TryGetValue(part.Asset.Hash, out var existing)) existing.Cancellation?.Cancel();
            assets[part.Asset.Hash] = new AssetEntry { Data = part.Asset };
        }
        GaussianViewport.ForgetDocument(target.Serial);
        doc.Objects.UnselectAll(); foreach (var item in added) doc.Objects.Select(item.Id);
        var messages = new List<string>();
        long count = prepared.Parts.Sum(part => (long)part.Asset.Splats.Length);
        messages.Add($"已导入 {count:N0} 个 GS，共 {added.Count:N0} 个部分，可一次 Undo 撤销。轴向 {upAxis}，有效比例 {effectiveScale:G8}。");
        messages.Add("原始文件已存档；完整本地 GS 缓存保留。显示准备期间显示包围盒，静止后恢复完整 GS。");
        if (prepared.Parts.Any(part => part.Antialiased))
            messages.Add("此场景声明了 antialiased 模式；当前视口尚未实现该模式的补偿公式，视觉结果可能不同。");
        if (prepared.Scene.Warnings is not null) messages.AddRange(prepared.Scene.Warnings);
        try
        {
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(overall);
            GaussianViewport.Activate(doc);
        }
        catch (Exception ex) { messages.Add("模型已写入，显示模式尚未就绪；包围盒保留。" + GSRhinoHost.FriendlyError(ex)); }
        doc.Views.Redraw();
        return string.Join(System.Environment.NewLine, messages);
    }
    private static Transform ToRhino(GaussianTransform source)
    {
        var m = source.Linear; var t = source.Translation;
        var value = Transform.Identity;
        value.M00=m.M11; value.M01=m.M12; value.M02=m.M13; value.M03=t.X;
        value.M10=m.M21; value.M11=m.M22; value.M12=m.M23; value.M13=t.Y;
        value.M20=m.M31; value.M21=m.M32; value.M22=m.M33; value.M23=t.Z;
        return value;
    }
    private static SourceArchive ArchiveSource(string path, CancellationToken ct)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ply" or ".splat" or ".spz" or ".usd" or ".usda" or ".usdc" or ".usdz"))
            throw new ArgumentException("请选择 PLY、SPLAT、SPZ、USD、USDA、USDC 或 USDZ 文件。");
        string directory = System.IO.Path.Combine(LocalStorage.Root, "sources");
        LocalStorage.CreatePrivateDirectory(directory);
        string temporary = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                LocalStorage.MakePrivate(temporary);
                var buffer = new byte[131072]; int read;
                while ((read = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                output.Flush(true);
            }
            string hash = Hash(temporary, ct);
            string destination = System.IO.Path.Combine(directory, hash + extension);
            Publish(temporary, destination, hash, ct);
            return new(hash, destination, System.IO.Path.GetFileName(path));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static AssetData PublishPart(GaussianSplat[] splats, CancellationToken ct)
    {
        LocalStorage.CreatePrivateDirectory(CacheRoot);
        string temporary = System.IO.Path.Combine(CacheRoot, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            GaussianPlyWriter.Write(temporary, splats, ct);
            LocalStorage.MakePrivate(temporary);
            string hash = Hash(temporary, ct);
            AssetData asset = PrepareAsset(splats, temporary, hash, ct);
            string destination = System.IO.Path.Combine(CacheRoot, hash + ".ply");
            Publish(temporary, destination, hash, ct);
            return asset with { Path = destination };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Publish(string temporary, string destination, string hash, CancellationToken ct)
    {
        if (!File.Exists(destination))
        {
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { }
        }
        if (Hash(destination, ct) != hash) throw new IOException("本地缓存校验失败，原始文件未修改。");
    }
    private static string Hash(string path, CancellationToken ct)
    {
        using var input = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072]; int read;
        while ((read = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static AssetData PrepareAsset(GaussianSplat[] source, string path, string hash, CancellationToken ct)
    {
        BoundingBox bounds = BoundingBox.Empty;
        foreach (GaussianSplat splat in source)
        {
            ct.ThrowIfCancellationRequested();
            GaussianMatrix covariance = GaussianMath.Covariance(splat, GaussianTransform.Identity);
            double x=3*Math.Sqrt(covariance.M11), y=3*Math.Sqrt(covariance.M22), z=3*Math.Sqrt(covariance.M33);
            var extent = new BoundingBox(new Point3d(splat.Center.X-x,splat.Center.Y-y,splat.Center.Z-z), new Point3d(splat.Center.X+x,splat.Center.Y+y,splat.Center.Z+z));
            if (!extent.IsValid) throw new InvalidDataException("GS 包围盒超出有效数值范围。");
            bounds.Union(extent);
        }
        if (source.Length == 0 || !bounds.IsValid) throw new InvalidDataException("此部分没有有效的完整 Gaussian Splat 数据。");
        if (bounds.Diagonal.Length == 0) bounds.Inflate(1);
        return new(source, GaussianPreparedAsset.Prepare(source, ct), bounds, hash, path);
    }
    private static (Guid Id, int Definition) AddBlock(RhinoDoc doc, PreparedPart part, Transform transform,
        SourceArchive source, GaussianScene scene, double scale, string upAxis, int layer)
    {
        AssetData data = part.Asset;
        var geometry = data.Bounds.GetEdges().Select(line => (GeometryBase)new LineCurve(line)).ToArray();
        var proxyAttributes = geometry.Select(_ => new ObjectAttributes { LayerIndex = layer, ObjectColor = Color.FromArgb(105,170,200), ColorSource = ObjectColorSource.ColorFromObject }).ToArray();
        int definition = -1;
        Guid instance = Guid.Empty;
        try
        {
            foreach (var a in proxyAttributes) a.SetUserString(ProxyKey, data.Hash);
            definition = doc.InstanceDefinitions.Add("GSRhinoGS_" + data.Hash[..10] + "_" + Guid.NewGuid().ToString("N")[..6], "GSRhino Gaussian reference; bounding box placeholder.", Point3d.Origin, geometry, proxyAttributes);
            if (definition < 0) throw new InvalidOperationException("无法建立 GS 块定义。");
            using var attrs = new ObjectAttributes { Name = "GSRhino · " + System.IO.Path.GetFileNameWithoutExtension(source.Name) + " · " + part.Name, LayerIndex = layer };
            attrs.SetUserString(Marker,"1"); attrs.SetUserString(HashKey,data.Hash); attrs.SetUserString(PathKey,data.Path);
            attrs.SetUserString("GSRhino.GS.SourceName",source.Name);
            attrs.SetUserString("GSRhino.GS.SourceSha256",source.Hash);
            attrs.SetUserString("GSRhino.GS.SourceArchivePath",source.Path);
            attrs.SetUserString("GSRhino.GS.SourceFormat",scene.Format);
            attrs.SetUserString("GSRhino.GS.PartName",part.Name);
            attrs.SetUserString("GSRhino.GS.SourceUpAxis",scene.UpAxis ?? "unspecified");
            attrs.SetUserString("GSRhino.GS.ImportUpAxis",upAxis);
            var linear = part.Transform.Linear; var offset = part.Transform.Translation;
            attrs.SetUserString("GSRhino.GS.SourceTransform",string.Join(",",new[] { linear.M11,linear.M12,linear.M13,linear.M21,linear.M22,linear.M23,linear.M31,linear.M32,linear.M33,offset.X,offset.Y,offset.Z }.Select(value => value.ToString("R",CultureInfo.InvariantCulture))));
            attrs.SetUserString("GSRhino.GS.InitialScale",scale.ToString("R",CultureInfo.InvariantCulture));
            if (scene.MetersPerUnit.HasValue) attrs.SetUserString("GSRhino.GS.SourceMetersPerUnit",scene.MetersPerUnit.Value.ToString("R",CultureInfo.InvariantCulture));
            attrs.SetUserString("GSRhino.GS.Antialiased",part.Antialiased ? "true" : "false");
            attrs.SetUserString("GSRhino.GS.Count",data.Splats.Length.ToString(CultureInfo.InvariantCulture));
            instance = doc.Objects.AddInstanceObject(definition,transform,attrs);
            if (instance == Guid.Empty) throw new InvalidOperationException("Rhino 未能建立 GS 块实例。");
            return (instance, definition);
        }
        finally
        {
            if (definition >= 0 && instance == Guid.Empty) doc.InstanceDefinitions.Delete(definition, false, true);
            foreach(var item in geometry) item.Dispose(); foreach(var item in proxyAttributes) item.Dispose();
        }
    }
    internal static int VisibleLayer(RhinoDoc doc)
    {
        var current=doc.Layers.CurrentLayer;
        if(current.IsVisible && !current.IsLocked) return current.Index;
        int index=doc.Layers.Add("GSRhino GS "+Guid.NewGuid().ToString("N")[..6],Color.FromArgb(105,170,200));
        if(index<0) throw new InvalidOperationException("无法创建可见 GS 图层。");
        return index;
    }
    internal sealed record DocumentStatus(int Instances, int CompatibleInstances, int Loaded, int Loading, int Errors);
    internal static DocumentStatus Status(RhinoDoc doc)
    {
        var references = Instances(doc, null, true).Select(Reference).Where(r => r is not null).ToArray();
        Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets);
        return new(references.Length, references.Count(r => !r!.IsNative),
            assets?.Values.Count(a => a.Data is not null) ?? 0,
            assets?.Values.Count(a => a.Loading) ?? 0, assets?.Values.Count(a => a.Error is not null) ?? 0);
    }
    internal static string Diagnostics(RhinoDoc doc)
    {
        DocumentStatus status = Status(doc);
        string message = $"GS 块实例：{status.Instances}（兼容引用：{status.CompatibleInstances}）；已加载资源：{status.Loaded}；加载中：{status.Loading}；资源错误：{status.Errors}。";
        if (Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets))
            foreach (string error in assets.Values.Where(a => a.Error is not null).Select(a => a.Error!).Distinct().Take(3)) message += "\n" + error;
        return message;
    }
}
