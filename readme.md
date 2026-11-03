# GSRhino

**An independent local Gaussian splat viewer for Rhino 8.** By **CuriousArc · qrost**.

[简体中文](readme.zh-CN.md) · Version **0.1.0-beta.3** · Local preview candidate

Import Gaussian scenes and view their anisotropic splats, opacity and view-dependent SH color inside Rhino's modeling viewport. GSRhino keeps full supported Gaussian data; it does not replace splats with point clouds or silently reduce their count. No account or API key is needed.

## Supported files

| File | Current scope |
| --- | --- |
| `.ply` | Original 3DGS properties: position, log scale, WXYZ rotation, opacity logit and SH0–3. ASCII, binary little-endian and binary big-endian. A plain RGB point-cloud PLY is not a Gaussian file. |
| `.splat` | Standard 32-byte records, with position, linear scale, quaternion and RGBA. This format has no higher-order SH. |
| `.spz` | Versions 1–4, supported coordinates and SH0–3. Degree 4 is explicitly rejected rather than truncated. Compression and quantization already present in the source cannot be undone. |
| `.usd`, `.usda`, `.usdc`, `.usdz` | Import/export static official OpenUSD `ParticleField3DGaussianSplat` scenes, including multiple parts and transforms. Requires the optional local USD runtime. Ordinary mesh USDZ is not GS; arbitrary vendor schemas are not supported. |

See [format details and sources](docs/formats.md). No product-level splat-count ceiling is imposed; available memory and host/runtime limits still apply.

## Requirements and installation

- Rhino **8.32 or later**, using its modern .NET runtime. Managed builds target macOS and Windows.
- The new standalone package still needs native host acceptance. Building a Windows package does not establish that it has been tested in Windows Rhino.
- Install the matching local `.yak` candidate with Rhino's Yak tool, then restart Rhino manually. Do not load the RHP by copying it alone; it needs its adjacent libraries and assets.

From a source checkout with .NET SDK 8 installed:

```sh
dotnet build GSRhino.sln -c Release
dotnet test tests/GSRhino.Tests/GSRhino.Tests.csproj -c Release --no-build
bash scripts/build-package.sh
python3 scripts/install-local.py PATH_TO_MAC_YAK
```

On Windows use `scripts/build-package.ps1`; install the Windows candidate through Yak. Packaging scripts produce local archives and verification receipts in `artifacts/`. They do not publish anything.

### Optional USD / USDZ runtime

PLY, SPLAT and SPZ need no extra runtime. For USD formats, run the installer once with **Python 3.11–3.14**:

```sh
python3.12 scripts/setup-usd.py
python3.12 scripts/setup-usd.py --check
```

On Windows, `py -3.12 scripts/setup-usd.py` is equivalent. The installer downloads the official pinned `usd-core==26.11` wheel into a private environment; it does not modify Rhino or system Python. Use `--wheel-dir DIRECTORY` for an offline installation. Import and export are local.

Default runtime: macOS `~/Library/Application Support/GSRhino/usd-runtime`; Windows `%LOCALAPPDATA%\GSRhino\usd-runtime`. Advanced users may set `GSRHINO_USD_PYTHON` to the Python executable before starting Rhino.

## Use

1. Run **`GSRhinoPanel`**.
2. Select a local Gaussian file, confirm source axis/units and the optional scale multiplier, then import into the current document.
3. Import enables **GSRhino (Experimental)** display mode. In wireframe and other modes the object appears as a reference box. While navigating, a temporary box may appear until the new complete GS frame is prepared.
4. Use **`GSRhinoMode`** to enable/reload the display, **`GSRhinoDiagnostics`** for local status and log/cache paths, or **`GSRhinoProbe`** for the offline color/occlusion fixture.

`GSRhinoImport` opens a direct file picker with automatic axis/units and scale 1. The panel provides custom settings and cancellation. Unknown source units use 1 file unit = 1 document unit; unknown up axis defaults to Z. USD stage units and axes are respected in Auto mode, including OpenUSD's standard defaults for unauthored metadata (0.01 meter per unit and its default up axis).

### Export a portable GS scene

Select top-level GS blocks, then use **`GSRhinoExport`** or the panel's **Export USD / USDZ** action. The panel also offers an explicit **all top-level GS** scope, including hidden/locked blocks as visible exported assets. An empty selection never falls back to exporting the whole document, and a mixed GS/non-GS selection is rejected. Mesh/Brep/NURBS and nested blocks are outside this GS export.

Choose `.usdz` for a self-contained package, `.usd`/`.usdc` for binary USD, or `.usda` for text. Export snapshots the current block transforms and document units on the UI thread, then writes in the background with cancellation. Every splat and SH0–3 coefficient is retained; geometry follows the official schema's float32 precision, so arbitrary double values are not bit-exact. Double-precision transforms and a local-origin shift reduce large-coordinate precision loss. Set a standard document length unit before exporting. See [USD exchange details and manual checks](docs/usd-exchange.md).

### Existing 3DM documents

Version 0.1.0-beta.2 also recognizes compatible version-1 GS block references produced under another namespace. Open the existing 3DM and enable `GSRhinoMode`; no re-import is needed when its referenced local PLY is available. The reader verifies the recorded SHA-256 and complete Gaussian data, copies it into GSRhino's own cache, and keeps the existing instance transform. It does not rewrite the document or source file. Missing/corrupt references retain their boxes and report a specific error.

## Storage and current limits

Original files and lossless supported-field PLY caches are kept in GSRhino's local application-data folder. A 3DM stores native instance references, transforms and cache metadata. **The 3DM alone does not contain the GS assets.** Keep the cache with your 3DM, or export the complete GS scene to USDZ for portable interchange. This does not bundle ordinary Rhino geometry. USD root-layer backups do not automatically bundle referenced files, but decoded GS caches support reopening locally without the USD decoder.

The renderer uses background projection/sorting and batched native drawing. This is an experimental native adapter, not a dedicated GPU compute renderer. It supports SH0–3; output RGB is currently 8-bit. HDR, exact format-specific antialias compensation, arbitrary interleaving with transparent Rhino objects, nested-block editing, layouts and capture are not complete. Basic opaque-depth and clipping behavior needs the [host acceptance checks](docs/host-acceptance.md) on each platform.

[Architecture](docs/architecture.md) · [Validation](docs/validation.md) · [Development plan](docs/plan.md) · [Third-party notices](docs/third-party.md)

Licensed under [Apache 2.0](LICENSE). GSRhino is an independent plugin; Rhino is a product of Robert McNeel & Associates.
