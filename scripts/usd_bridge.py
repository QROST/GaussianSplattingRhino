#!/usr/bin/env python3
"""Static official OpenUSD GS -> local binary PLY parts + a small manifest.

Run in an isolated usd-core==26.11 interpreter. No networking, cloud service, point-cloud conversion,
renderer, or third-party USD file-format plugins are required. USD composition is delegated to OpenUSD.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import sys


class UnsupportedUsd(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise UnsupportedUsd(message)


def finite(values, label):
    result = tuple(float(v) for v in values)
    require(all(math.isfinite(v) for v in result), label + " contains non-finite values.")
    return result


def digest(path):
    sha = hashlib.sha256()
    with open(path, "rb") as stream:
        for data in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(data)
    return sha.hexdigest()


def physical_path(identifier, Ar):
    value = str(identifier)
    if Ar.IsPackageRelativePath(value):
        value, _ = Ar.SplitPackageRelativePathOuter(value)
    require("://" not in value, "Only local USD dependencies are supported.")
    return Path(value).resolve()


def choose_array(prim, float_name, half_name):
    # Schema helpers prefer nonempty float arrays, then half arrays. Empty arrays mean no authored values.
    for name in (float_name, half_name):
        attribute = prim.GetAttribute(name)
        values = attribute.Get() if attribute else None
        if values is not None and len(values):
            return values
    return None


def optional_array(prim, float_name, half_name, count):
    values = choose_array(prim, float_name, half_name)
    if values is not None:
        require(len(values) == count, f"{prim.GetPath()}: {float_name} has {len(values)} entries; expected {count}. No data was truncated.")
    return values


def affine_values(matrix, label):
    require(all(math.isfinite(float(matrix[r][c])) for r in range(4) for c in range(4)), label + ": non-finite transform.")
    require(all(float(matrix[r][3]) == 0 for r in range(3)) and float(matrix[3][3]) == 1,
            label + ": projective transforms are not supported for Gaussian splats.")
    # Gf matrices multiply row vectors. Core uses column vectors: transpose only the linear block.
    linear = [float(matrix[c][r]) for r in range(3) for c in range(3)]
    translation = [float(matrix[3][c]) for c in range(3)]
    require(matrix.GetDeterminant() != 0, label + ": singular Gaussian transform is not supported.")
    return linear + translation


def write_part(prim, output, index, cache, UsdGeom, UsdVol, UsdShade):
    label = str(prim.GetPath())
    require(UsdGeom.Imageable(prim).ComputeVisibility() != UsdGeom.Tokens.invisible,
            label + ": invisible Gaussian prims are not supported by this static import; export the intended visible scene explicitly.")
    material, _ = UsdShade.MaterialBindingAPI(prim).ComputeBoundMaterial()
    require(not material, label + ": material-bound Gaussian radiance is not supported; export the SH radiance explicitly.")
    known_particle_apis = {"ParticleFieldPositionAttributeAPI", "ParticleFieldPositionBaseAPI", "ParticleFieldOrientationAttributeAPI",
                           "ParticleFieldScaleAttributeAPI", "ParticleFieldOpacityAttributeAPI", "ParticleFieldKernelGaussianEllipsoidAPI",
                           "ParticleFieldKernelBaseAPI", "ParticleFieldSphericalHarmonicsAttributeAPI", "ParticleFieldRadianceBaseAPI"}
    for api in prim.GetAppliedSchemas():
        affects_splats = str(api).startswith("ParticleField") or any(word in str(api).lower() for word in ("gaussian", "splat", "compress", "nurec"))
        require(not affects_splats or api in known_particle_apis, label + ": unsupported Gaussian API schema " + str(api))
    for attribute in prim.GetAttributes():
        require(not attribute.HasAuthoredConnections(), label + ": connected/procedural Gaussian attributes are not supported.")
    positions = choose_array(prim, "positions", "positionsh")
    count = len(positions) if positions is not None else 0
    scales = optional_array(prim, "scales", "scalesh", count)
    orientations = optional_array(prim, "orientations", "orientationsh", count)
    opacities = optional_array(prim, "opacities", "opacitiesh", count)
    sh = choose_array(prim, "radiance:sphericalHarmonicsCoefficients", "radiance:sphericalHarmonicsCoefficientsh")
    gaussian = UsdVol.ParticleField3DGaussianSplat(prim)
    degree = int(gaussian.GetRadianceSphericalHarmonicsDegreeAttr().Get()) if sh is not None else 0
    require(0 <= degree <= 3, label + f": SH degree {degree} is not supported; coefficients were not reduced or discarded.")
    coefficients = (degree + 1) ** 2
    if sh is not None:
        require(len(sh) == count * coefficients, label + f": SH coefficient count {len(sh)} does not match {count} splats at degree {degree}.")
    transform = affine_values(cache.GetLocalToWorldTransform(prim), label)
    filename = f"part-{index:06d}.ply"
    header = ["ply", "format binary_little_endian 1.0", "comment GSRhino official static USD Gaussian bridge", f"element vertex {count}"]
    fields = ("x", "y", "z", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3", "opacity")
    header += ["property double " + name for name in fields]
    header += ["property float f_dc_" + str(i) for i in range(3)]
    header += ["property float f_rest_" + str(i) for i in range(3 * (coefficients - 1))]
    header += ["end_header", ""]
    record = struct.Struct("<11d" + "f" * (3 * coefficients))
    with open(output / filename, "wb") as stream:
        stream.write("\n".join(header).encode("ascii"))
        for i in range(count):
            position = finite(positions[i], label + ": positions")
            scale = finite(scales[i], label + ": scales") if scales is not None else (1.0, 1.0, 1.0)
            require(all(v > 0 for v in scale), label + f": splat {i} has a nonpositive scale; no epsilon substitution was applied.")
            if orientations is None:
                rotation = (1.0, 0.0, 0.0, 0.0)
            else:
                q = orientations[i]
                rotation = finite((q.GetReal(), *q.GetImaginary()), label + ": orientations")
                require(any(v != 0 for v in rotation), label + f": splat {i} has a zero quaternion.")
            opacity = float(opacities[i]) if opacities is not None else 1.0
            require(math.isfinite(opacity) and 0 <= opacity <= 1, label + f": splat {i} opacity is outside [0,1].")
            # Finite logit sentinels preserve exactly activated 0 and 1 in Core's stable sigmoid.
            logit = -sys.float_info.max if opacity == 0 else sys.float_info.max if opacity == 1 else math.log(opacity) - math.log1p(-opacity)
            values = [finite(sh[i * coefficients + k], label + ": SH") for k in range(coefficients)] if sh is not None else [(0.0, 0.0, 0.0)]
            ply_sh = list(values[0]) + [values[k][channel] for channel in range(3) for k in range(1, coefficients)]
            stream.write(record.pack(*position, *(math.log(v) for v in scale), *rotation, logit, *ply_sh))
    return {"name": label, "file": filename, "count": count, "degree": degree, "transform": transform,
            "projectionModeHint": str(gaussian.GetProjectionModeHintAttr().Get()),
            "sortingModeHint": str(gaussian.GetSortingModeHintAttr().Get())}


def convert(input_path, output):
    try:
        from pxr import Ar, Sdf, Usd, UsdGeom, UsdShade, UsdUtils, UsdVol
    except ImportError as error:
        raise UnsupportedUsd("OpenUSD runtime is missing. Install usd-core==26.11 using the GSRhino USD setup.") from error
    require(Usd.GetVersion() == (0, 26, 11), "This bridge requires usd-core==26.11; another OpenUSD runtime version was found.")
    source = input_path.resolve()
    require(source.is_file(), "USD input file does not exist.")
    require(source.suffix.lower() in (".usd", ".usda", ".usdc", ".usdz"), "Unsupported USD container extension.")
    stage = Usd.Stage.Open(str(source), load=Usd.Stage.LoadAll)
    require(bool(stage), "OpenUSD could not open the stage.")
    errors = stage.GetCompositionErrors()
    require(not errors, "USD composition has unresolved or invalid dependencies: " + "; ".join(str(e) for e in errors))
    dependency_layers, assets, unresolved = UsdUtils.ComputeAllDependencies(Sdf.AssetPath(str(source)))
    require(not unresolved, "USD dependencies are missing or require an unsupported resolver/plugin: " + ", ".join(str(value) for value in unresolved))
    identifiers = [str(layer.realPath) for layer in dependency_layers if not layer.anonymous and layer.realPath]
    identifiers += [str(asset) for asset in assets]
    files = {source}
    for identifier in identifiers:
        resolved = physical_path(identifier, Ar)
        require(resolved.is_file(), "USD dependency is not an accessible local file: " + identifier)
        if source.suffix.lower() == ".usdz":
            require(resolved == source, "USDZ dependency escapes its package: " + identifier)
        files.add(resolved)
    # Detect source-layer/dependency changes during extraction; this is not an atomic filesystem snapshot.
    before = {str(path): digest(path) for path in sorted(files)}
    warnings = []
    if len(files) > 1:
        warnings.append("This USD uses external dependencies. Source archival backs up only the root layer; reopening uses the expanded Gaussian cache. Share the original USD together with its complete dependency set.")
    if not stage.HasAuthoredMetadata("metersPerUnit"):
        warnings.append("USD metersPerUnit is unauthored; OpenUSD's 0.01 meter fallback is used.")
    if not stage.HasAuthoredMetadata("upAxis"):
        warnings.append("USD upAxis is unauthored; the OpenUSD fallback axis is used.")
    up_axis, meters = str(UsdGeom.GetStageUpAxis(stage)), float(UsdGeom.GetStageMetersPerUnit(stage))
    require(up_axis in ("Y", "Z") and math.isfinite(meters) and meters > 0, "USD stage has invalid units or upAxis.")
    cache = UsdGeom.XformCache(Usd.TimeCode.Default())
    prims, ignored = [], []
    for prim in Usd.PrimRange.Stage(stage, Usd.TraverseInstanceProxies()):
        label, type_name = str(prim.GetPath()), str(prim.GetTypeName())
        require(not prim.IsA(UsdGeom.PointInstancer), label + ": PointInstancer expansion is not supported; no prototype was imported as a substitute.")
        for attribute in prim.GetAttributes():
            require(attribute.GetNumTimeSamples() == 0, label + ": animated/time-sampled USD content is not supported by this static importer.")
        require(not prim.HasMetadata("clips"), label + ": USD value clips are not supported by this static importer.")
        if type_name == "ParticleField3DGaussianSplat":
            prims.append(prim)
        else:
            suspicious = prim.IsA(UsdVol.ParticleField) or any(word in type_name.lower() for word in ("gaussian", "splat", "nurec"))
            suspicious = suspicious or bool(prim.GetAttribute("radiance:sphericalHarmonicsCoefficients"))
            require(not suspicious, label + ": unsupported Gaussian schema or vendor-compressed field " + type_name + "; no conversion to Points/Mesh was attempted.")
            if prim.IsA(UsdGeom.Boundable):
                ignored.append(label + " (" + type_name + ")")
    require(bool(prims), "No official ParticleField3DGaussianSplat prims were found. Ordinary Points and Mesh are not Gaussian splats.")
    if ignored:
        warnings.append(f"{len(ignored)} non-Gaussian geometry prim(s) were not imported: " + ", ".join(ignored[:8]) + (" ..." if len(ignored) > 8 else ""))
    parts = [write_part(prim, output, index, cache, UsdGeom, UsdVol, UsdShade) for index, prim in enumerate(prims)]
    require(sum(part["count"] for part in parts) > 0, "The USD stage contains no Gaussian particles.")
    for part in parts:
        if part["projectionModeHint"] != "perspective" or part["sortingModeHint"] != "zDepth":
            warnings.append(part["name"] + ": authored renderer hints " + part["projectionModeHint"] + "/" + part["sortingModeHint"] + "; GSRhino currently uses its camera projection and center-z sorting.")
    for path, expected in before.items():
        require(digest(path) == expected, "A USD source dependency changed during import; retry after saving finishes.")
    manifest = {"protocol": 1, "openUsdVersion": "26.11", "upAxis": up_axis, "metersPerUnit": meters,
                "parts": parts, "warnings": warnings}
    with open(output / "manifest.json", "w", encoding="utf-8") as stream:
        json.dump(manifest, stream, ensure_ascii=False, allow_nan=False, separators=(",", ":"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    try:
        convert(Path(args.input), output)
    except Exception as error:
        message = str(error) or type(error).__name__
        with open(output / "error.json", "w", encoding="utf-8") as stream:
            json.dump({"message": message}, stream, ensure_ascii=False)
        print(message, file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
