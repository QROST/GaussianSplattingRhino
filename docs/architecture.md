# Architecture

GSRhino is a local viewer. It has no accounts, API keys, generation services or telemetry.

## Data path

`file → validated GaussianScene → original archive + full canonical PLY cache → Rhino instances → native GS viewport`

- `GSRhino.Core` owns format decoding, scene transforms, SH0–3, covariance, projection, stable sorting and a CPU compositing oracle. It does not reference Rhino.
- `GSRhino.Rhino` owns the panel, file import, undo, persistent asset references, display-mode registration and display conduit.
- The optional OpenUSD bridge runs in a separate Python process. It uses the official composition engine for USD/USDC/USDZ, writes per-prim GS data and returns compact scene metadata. Rhino never hosts Python or loads USD native libraries.
- `GSRhino.Reference` supplies reproducible CPU images and projection/sort benchmarks. These do not establish Rhino frame rate or native occlusion correctness.

## Scene and cache

Every part retains its local splats and its complete affine transform. Instance transforms compose source transforms, axis conversion and unit conversion. SH view directions are transformed back into the local frame; transforming only positions would change both covariance and directional color.

Canonical PLY caches store positions, log scales, quaternions and opacity logits as doubles, plus full floating-point SH coefficients. They do not sample or reduce the source. Mixed SH degrees are zero-padded, which preserves the same radiance function. The imported source bytes are separately archived by SHA-256. USD root-layer archives do not bundle external references; the decoded GS caches are used to reopen the document.

Rhino documents contain instance proxies and references to external caches, rather than embedding hundreds of megabytes of GS data into the 3DM. Moving a 3DM alone does not move those caches. Portable project bundling is future work.

Compatible existing GS blocks are resolved through same-prefix `.GS.Version`, `.GS.Sha256` and `.GS.AssetPath` metadata. The native prefix has priority; foreign references require version 1, a valid SHA-256 and an unambiguous prefix. Asset fields are never mixed across producers. On first load the referenced local PLY is snapshotted, hash-checked and strictly parsed before publishing a private cache. Rendering continues to use the existing Rhino instance transform, and never writes compatibility metadata back to the document.

## Display pipeline

Full data is projected and depth-sorted on a cancellable background worker. Quads are grouped into bounded upload batches; a shared opacity atlas preserves smooth anisotropic splats without one Rhino material per Gaussian. UI transfers use small time slices. While a new camera view is being prepared, a proxy may appear; the implementation never labels a partial frame as complete.

DPI-normalized camera keys, stable depth ranges and bounds contribution prevent redundant rebuilds and disappearing geometry. Display-mode registration is deferred until Rhino is ready and fails without disabling file import or the panel.

This is a native mesh/material adapter, not a dedicated compute-shader renderer. Source count is never silently capped. RAM, .NET collection sizes, Rhino's graphics resources and CPU/GPU speed remain real limits. Future work should optimize those measured costs while keeping a full-quality reference path.

## Rendering limits

The current renderer evaluates SH0–3 and quantizes display RGB to 8-bit. HDR output, exact format-specific antialias compensation, arbitrary transparent-object interleaving, nested-block editing, layouts and capture need separate work. Opaque native geometry and basic clipping use Rhino's depth pipeline; the complete platform matrix requires manual verification.
