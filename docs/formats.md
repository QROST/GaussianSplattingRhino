# Format contract

A file extension is not sufficient evidence that a file contains Gaussian splats. Import fully validates the supported data before inserting native references. The original file remains archived; canonical caches retain all supported parameters without a point-cloud fallback.

| Format | Data and coordinates | Explicit limits |
| --- | --- | --- |
| 3DGS PLY | Named `x/y/z`, `scale_0..2` (log), `rot_0..3` (WXYZ), `opacity` (logit), `f_dc_0..2`, complete channel-major `f_rest_*` reordered to coefficient-major RGB. ASCII/LE/BE. | Complete SH0–3 only; RGB-only points, incomplete fields, malformed/truncated/trailing records rejected. No unit/axis guess beyond the documented UI default. |
| SPLAT | 32-byte records: float32 position/linear scale, RGBA bytes, quantized WXYZ quaternion. | DC color only; headerless format cannot self-identify. Partial records, nonfinite values and invalid scales/quaternions rejected. |
| SPZ | v1 half positions; v2–4 fixed-point positions; gzip v1–3, separate Zstd streams v4. Complete SH0–3, scale/opacity/DC conventions preserved. Coordinate metadata maps through a part transform with local SH intact. | Degree 4 rejected. Unknown extensions reported; their original bytes remain in the source archive but unknown semantics are not applied. Antialias metadata retained; current rendering does not implement its exact compensation. |
| USD family | Official `ParticleField3DGaussianSplat`, float/half fields, multiple prims/instances, complete local-to-stage transforms, stage units and up-axis; official OpenUSD 26.11 composition engine. USDZ root can be binary USDC. | Static supported schema only. Animation, unsupported visibility/material/instancer semantics, vendor compressed schemas and missing references fail explicitly. Mesh/Points are reported as omitted, never converted into GS. |

USDZ is an uncompressed ZIP container with package constraints, not a generic Gaussian encoding. The import/export runtime is deliberately separate from Rhino's process and uses a pinned local `usd-core` environment. `.usd/.usda/.usdc` references resolve relative to their original source; the archived root layer alone is not a portable USD scene. Decoded part caches retain the imported GS result. USDZ dependencies must remain in the package.

Reference sources:

- [OpenUSD Gaussian schema](https://github.com/PixarAnimationStudios/OpenUSD/blob/v26.11/pxr/usd/usdVol/schema.usda)
- [OpenUSD USDZ specification](https://openusd.org/release/spec_usdz.html)
- [Official usd-core distribution](https://pypi.org/project/usd-core/26.11/)
- [Original SPLAT writer](https://github.com/antimatter15/splat/blob/main/convert.py)
- [SPZ format and reference implementation](https://github.com/nianticlabs/spz)
- [SPZ extension definitions](https://github.com/nianticlabs/spz/tree/main/extensions)

Unsupported formats such as SOG/KSPLAT and vendor-specific USD schemas require their own decoders and validation. They are not accepted by renaming an extension.

## Export

USD, USDA, USDC and USDZ support full static GS export. See [USD exchange](usd-exchange.md) for scope, precision and portability. PLY/SPLAT/SPZ remain import formats in the current UI.
