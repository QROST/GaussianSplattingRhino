using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GSRhino.Core;

/// <summary>Reads the official static OpenUSD Gaussian schema through an isolated, version-pinned local process.</summary>
public static class UsdGaussianReader
{
    public const string PythonEnvironmentVariable = "GSRHINO_USD_PYTHON";
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromMinutes(15);

    public static GaussianScene Read(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrEmpty(path);
        string input = Path.GetFullPath(path);
        if (!File.Exists(input)) throw new FileNotFoundException("USD input file does not exist.", input);
        if (!new[] { ".usd", ".usda", ".usdc", ".usdz" }.Contains(Path.GetExtension(input), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Expected a USD, USDA, USDC or USDZ file containing official Gaussian splat primitives.");
        string python = ResolvePython(), script = ResolveBridge();
        string output = Path.Combine(Path.GetTempPath(), "GSRhino", "usd-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(output);
        else Directory.CreateDirectory(output, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            RunBridge(python, script, input, output, ct);
            return ReadBridgeOutput(output, ct);
        }
        finally
        {
            try { Directory.Delete(output, recursive: true); }
            catch (IOException) { /* The import result must not be hidden by a temporary-file cleanup failure. */ }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Validates the bridge manifest and local PLY payloads. This entry point also permits offline protocol tests.</summary>
    public static GaussianScene ReadBridgeOutput(string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(Path.Combine(directory, "manifest.json"));
        using JsonDocument json = JsonDocument.Parse(stream);
        JsonElement root = json.RootElement;
        if (root.GetProperty("protocol").GetInt32() != 1) throw new InvalidDataException("Unsupported USD bridge protocol.");
        if (root.GetProperty("openUsdVersion").GetString() != "26.11") throw new InvalidDataException("Unexpected OpenUSD bridge version.");
        string? upAxis = root.GetProperty("upAxis").GetString();
        double units = root.GetProperty("metersPerUnit").GetDouble();
        if (upAxis is not ("Y" or "Z") || !double.IsFinite(units) || units <= 0) throw new InvalidDataException("Invalid USD stage axis or units.");
        var parts = new List<GaussianPart>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (JsonElement part in root.GetProperty("parts").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            string name = part.GetProperty("name").GetString() ?? "";
            string file = part.GetProperty("file").GetString() ?? "";
            if (name.Length == 0 || !name.StartsWith('/') || !names.Add(name)) throw new InvalidDataException("USD bridge prim paths must be distinct absolute paths.");
            if (file.Length == 0 || file.Contains('/') || file.Contains('\\') || file.Contains(':') || file is "." or ".." ||
                Path.IsPathRooted(file) || !file.EndsWith(".ply", StringComparison.Ordinal) || !files.Add(file))
                throw new InvalidDataException("USD bridge payload must be a distinct local PLY file.");
            string payload = Path.Combine(directory, file);
            if (new FileInfo(payload).LinkTarget is not null) throw new InvalidDataException("USD bridge payload cannot be a symbolic link.");
            double[] matrix = part.GetProperty("transform").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (matrix.Length != 12 || matrix.Any(value => !double.IsFinite(value))) throw new InvalidDataException("USD bridge affine transform must contain twelve finite values.");
            var transform = new GaussianTransform(new(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7], matrix[8]), new(matrix[9], matrix[10], matrix[11]));
            try { _ = transform.Linear.Inverse(); }
            catch (ArgumentException ex) { throw new InvalidDataException("USD contains a singular or numerically unsupported Gaussian transform.", ex); }
            int degree = part.GetProperty("degree").GetInt32();
            long count = part.GetProperty("count").GetInt64();
            if (degree is < 0 or > 3 || count < 0) throw new InvalidDataException("Unsupported USD Gaussian degree or count.");
            GaussianSplat[] splats = PlyReader.ReadGaussians(payload, ct).ToArray();
            if (splats.LongLength != count || splats.Any(splat => splat.Degree != degree)) throw new InvalidDataException("USD bridge payload does not match its manifest.");
            total = checked(total + splats.LongLength);
            parts.Add(new GaussianPart(name, splats, transform));
        }
        if (total == 0) throw new InvalidDataException("The USD stage contains no supported Gaussian splats.");
        string[] warnings = root.GetProperty("warnings").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        return new GaussianScene(parts, upAxis, units, "OpenUSD ParticleField3DGaussianSplat", warnings);
    }

    private static string ResolvePython()
    {
        string? configured = Environment.GetEnvironmentVariable(PythonEnvironmentVariable);
        string python = !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured) : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GSRhino", "usd-runtime",
            OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python3");
        if (!File.Exists(python)) throw new InvalidOperationException("OpenUSD runtime is not installed. Run the GSRhino USD setup or set GSRHINO_USD_PYTHON to a Python executable containing usd-core==26.11.");
        return python;
    }

    private static string ResolveBridge()
    {
        string assemblyDirectory = Path.GetDirectoryName(typeof(UsdGaussianReader).Assembly.Location) ?? AppContext.BaseDirectory;
        foreach (string start in new[] { assemblyDirectory, AppContext.BaseDirectory })
        {
            string packaged = Path.Combine(start, "usd", "usd_bridge.py");
            if (File.Exists(packaged)) return packaged;
        }
        // Source-tree fallback is for development; never load scripts next to an imported asset.
        foreach (string start in new[] { assemblyDirectory })
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
            {
                string project = Path.Combine(current.FullName, "src", "GSRhino.Core", "GSRhino.Core.csproj");
                string script = Path.Combine(current.FullName, "scripts", "usd_bridge.py");
                if (File.Exists(project) && File.Exists(script)) return script;
            }
        throw new FileNotFoundException("The installed GSRhino package is missing usd/usd_bridge.py.");
    }

    private static void RunBridge(string python, string script, string input, string output, CancellationToken ct)
    {
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string argument in new[] { "-I", script, "--input", input, "--output", output }) start.ArgumentList.Add(argument);
        start.Environment.Remove("PXR_PLUGINPATH_NAME");
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException("OpenUSD process could not start."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("OpenUSD runtime could not start. Check GSRHINO_USD_PYTHON and the USD runtime installation.", ex); }
        Task<string> stdout = Capture(process.StandardOutput), stderr = Capture(process.StandardError);
        using var timeout = new CancellationTokenSource(BridgeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { process.WaitForExitAsync(linked.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new TimeoutException("OpenUSD import exceeded the 15-minute conversion timeout. The conversion process was stopped.");
        }
        finally { if (!process.HasExited) Kill(process); }
        string diagnostics = stderr.GetAwaiter().GetResult();
        string standardOutput = stdout.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            string message = ReadError(output) ?? diagnostics.Trim();
            if (message.Length == 0) message = standardOutput.Trim();
            throw new InvalidDataException("OpenUSD import failed: " + (message.Length == 0 ? "the decoder returned no diagnostic." : message));
        }
    }

    private static string? ReadError(string directory)
    {
        string path = Path.Combine(directory, "error.json");
        if (!File.Exists(path)) return null;
        try { using JsonDocument error = JsonDocument.Parse(File.ReadAllText(path)); return error.RootElement.GetProperty("message").GetString(); }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private static async Task<string> Capture(StreamReader reader)
    {
        var text = new StringBuilder(); char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            if (text.Length < 16384) text.Append(buffer, 0, Math.Min(count, 16384 - text.Length));
        return text.ToString();
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        try { process.WaitForExit(5000); }
        catch (InvalidOperationException) { }
    }
}
