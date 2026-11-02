#!/usr/bin/env python3
"""Explicit OpenUSD integration checks; run with the pinned USD interpreter, outside ordinary .NET unit tests."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

from pxr import Gf, Sdf, Usd, UsdGeom, UsdUtils, UsdVol, Vt

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("gsrhino_usd_bridge", ROOT / "scripts" / "usd_bridge.py")
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


def gs(stage, path, half=False, degree=3, count=2):
    splat = UsdVol.ParticleField3DGaussianSplat.Define(stage, path)
    positions = [(i + 1, i + 2, i + 3) for i in range(count)]
    scales = [(2, .5, 1) for _ in range(count)]
    orientations = [Gf.Quatf(.5, .5, -.5, .5) for _ in range(count)]
    opacity = [float(i % 2) for i in range(count)]
    coefficients = [(k * .125 - 1, k * -.0625 + .5, k * .03125) for i in range(count) for k in range((degree + 1) ** 2)]
    if half:
        splat.CreatePositionshAttr(Vt.Vec3hArray([Gf.Vec3h(*v) for v in positions]))
        splat.CreateScaleshAttr(Vt.Vec3hArray([Gf.Vec3h(*v) for v in scales]))
        splat.CreateOrientationshAttr(Vt.QuathArray([Gf.Quath(.5, .5, -.5, .5) for _ in range(count)]))
        splat.CreateOpacitieshAttr(Vt.HalfArray(opacity))
        splat.CreateRadianceSphericalHarmonicsCoefficientshAttr(Vt.Vec3hArray([Gf.Vec3h(*v) for v in coefficients]))
    else:
        splat.CreatePositionsAttr(Vt.Vec3fArray(positions))
        splat.CreateScalesAttr(Vt.Vec3fArray(scales))
        splat.CreateOrientationsAttr(Vt.QuatfArray(orientations))
        splat.CreateOpacitiesAttr(Vt.FloatArray(opacity))
        splat.CreateRadianceSphericalHarmonicsCoefficientsAttr(Vt.Vec3fArray(coefficients))
    splat.CreateRadianceSphericalHarmonicsDegreeAttr(degree)
    return splat


def stage(path):
    scene = Usd.Stage.CreateNew(str(path))
    UsdGeom.SetStageUpAxis(scene, UsdGeom.Tokens.z)
    UsdGeom.SetStageMetersPerUnit(scene, .001)
    return scene


def fixtures(directory):
    path = directory / "usd-two-parts.usda"
    scene = stage(path)
    world = UsdGeom.Xform.Define(scene, "/World")
    world.AddTranslateOp().Set(Gf.Vec3d(10, 20, 30))
    scene.SetDefaultPrim(world.GetPrim())
    group = UsdGeom.Xform.Define(scene, "/World/Group")
    group.AddRotateZOp().Set(90)
    group.AddScaleOp().Set(Gf.Vec3f(2, 3, 4))
    gs(scene, "/World/Group/Float")
    other = gs(scene, "/World/Half", half=True)
    UsdGeom.Xformable(other).AddTranslateOp().Set(Gf.Vec3d(5, -2, 1))
    scene.GetRootLayer().Save()
    binary = directory / "usd-two-parts.usdc"
    scene.GetRootLayer().Export(str(binary))
    archive = directory / "usd-two-parts.usdz"
    UsdUtils.CreateNewUsdzPackage(Sdf.AssetPath(str(binary)), str(archive))
    source = stage(directory / "usd-prototype.usda")
    asset = UsdGeom.Xform.Define(source, "/Asset")
    source.SetDefaultPrim(asset.GetPrim())
    gs(source, "/Asset/Splat", degree=1, count=1)
    source.GetRootLayer().Save()
    instanced = stage(directory / "usd-instanced.usda")
    for name, offset in (("A", 3), ("B", 9)):
        instance = UsdGeom.Xform.Define(instanced, "/" + name)
        instance.GetPrim().GetReferences().AddReference("usd-prototype.usda")
        instance.GetPrim().SetInstanceable(True)
        instance.AddTranslateOp().Set(Gf.Vec3d(offset, 0, 0))
    instanced.GetRootLayer().Save()
    return path


class Integration(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="gsrhino-usd-check-")
        self.directory = Path(self.temporary.name)
        fixtures(self.directory)
        self.sequence = 0

    def tearDown(self):
        self.temporary.cleanup()

    def decode(self, file):
        self.sequence += 1
        output = self.directory / ("output-" + str(self.sequence))
        output.mkdir()
        bridge.convert(file, output)
        return json.loads((output / "manifest.json").read_text()), output

    def test_usda_usdc_usdz_same_complete_data_and_actual_usdc_root(self):
        results = [self.decode(self.directory / ("usd-two-parts." + ext)) for ext in ("usda", "usdc", "usdz")]
        for manifest, _ in results:
            self.assertEqual(2, len(manifest["parts"]))
            self.assertEqual(4, sum(part["count"] for part in manifest["parts"]))
            self.assertEqual([3, 3], [part["degree"] for part in manifest["parts"]])
            self.assertEqual("Z", manifest["upAxis"])
            self.assertEqual(.001, manifest["metersPerUnit"])
            self.assertEqual([10., 20., 30.], manifest["parts"][0]["transform"][9:])
            self.assertEqual([15., 18., 31.], manifest["parts"][1]["transform"][9:])
            linear = manifest["parts"][0]["transform"][:9]
            for actual, expected in zip(linear, (0, -3, 0, 2, 0, 0, 0, 0, 4)):
                self.assertAlmostEqual(expected, actual, places=12)
        for index in range(2):
            self.assertEqual(1, len({(out / manifest["parts"][index]["file"]).read_bytes() for manifest, out in results}))
        with zipfile.ZipFile(self.directory / "usd-two-parts.usdz") as package:
            root = package.namelist()[0]
            self.assertTrue(root.endswith(".usdc"))
            self.assertEqual(b"PXR-USDC", package.read(root)[:8])

    def test_all_instance_proxies_and_external_layer_warning(self):
        manifest, _ = self.decode(self.directory / "usd-instanced.usda")
        self.assertEqual(["/A/Splat", "/B/Splat"], [part["name"] for part in manifest["parts"]])
        self.assertEqual([3., 9.], [part["transform"][9] for part in manifest["parts"]])
        self.assertTrue(any("external dependencies" in warning for warning in manifest["warnings"]))

    def test_float_arrays_win_over_half_without_losing_sh(self):
        path = self.directory / "usd-float-priority.usda"
        scene = stage(path)
        splat = gs(scene, "/Splat", half=True, degree=1, count=1)
        splat.CreatePositionsAttr(Vt.Vec3fArray([(9, 8, 7)]))
        scene.GetRootLayer().Save()
        manifest, output = self.decode(path)
        import struct
        payload = (output / manifest["parts"][0]["file"]).read_bytes().split(b"end_header\n", 1)[1]
        self.assertEqual((9., 8., 7.), struct.unpack_from("<3d", payload))
        self.assertEqual(1, manifest["parts"][0]["degree"])

    def test_regular_points_are_not_gaussians(self):
        path = self.directory / "usd-points.usda"
        scene = stage(path); UsdGeom.Points.Define(scene, "/Points").CreatePointsAttr([(1, 2, 3)]); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "Ordinary Points"):
            self.decode(path)

    def test_mixed_mesh_is_explicitly_reported(self):
        path = self.directory / "usd-mixed.usda"
        scene = stage(path); gs(scene, "/Splat"); UsdGeom.Mesh.Define(scene, "/Mesh"); scene.GetRootLayer().Save()
        manifest, _ = self.decode(path)
        self.assertTrue(any("/Mesh (Mesh)" in warning for warning in manifest["warnings"]))

    def test_time_samples_fail_instead_of_importing_wrong_frame(self):
        path = self.directory / "usd-animated.usda"
        scene = stage(path); splat = gs(scene, "/Splat"); splat.GetPositionsAttr().Set([(0, 0, 0), (1, 1, 1)], 1); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "animated/time-sampled"):
            self.decode(path)

    def test_missing_reference_fails(self):
        path = self.directory / "usd-missing.usda"
        scene = stage(path); gs(scene, "/Splat"); scene.DefinePrim("/Missing").GetReferences().AddReference("not-present.usda"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "dependenc"):
            self.decode(path)

    def test_malformed_sh_is_not_truncated(self):
        path = self.directory / "usd-bad-sh.usda"
        scene = stage(path); splat = gs(scene, "/Splat"); splat.GetRadianceSphericalHarmonicsCoefficientsAttr().Set([(1, 1, 1)]); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "SH coefficient count"):
            self.decode(path)

    def test_unsupported_degree_is_not_reduced(self):
        path = self.directory / "usd-sh4.usda"
        scene = stage(path); gs(scene, "/Splat", degree=4); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "SH degree 4"):
            self.decode(path)

    def test_point_instancer_is_not_a_single_prototype_import(self):
        path = self.directory / "usd-point-instancer.usda"
        scene = stage(path); gs(scene, "/Splat"); UsdGeom.PointInstancer.Define(scene, "/Instances"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "PointInstancer"):
            self.decode(path)

    def test_vendor_schema_is_not_a_pointcloud(self):
        path = self.directory / "usd-vendor.usda"
        scene = stage(path); scene.DefinePrim("/Vendor", "OmniNuRecFieldAsset"); scene.GetRootLayer().Save()
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "unsupported Gaussian schema"):
            self.decode(path)

    def test_usdz_cannot_resolve_a_dependency_outside_its_package(self):
        root = self.directory / "usd-escaping-root.usda"
        scene = stage(root)
        scene.DefinePrim("/External").GetReferences().AddReference(str(self.directory / "usd-prototype.usda"))
        scene.GetRootLayer().Save()
        archive = self.directory / "usd-escaping.usdz"
        with Sdf.ZipFileWriter.CreateNew(str(archive)) as writer:
            writer.AddFile(str(root), "root.usda")
        with self.assertRaisesRegex(bridge.UnsupportedUsd, "escapes its package"):
            self.decode(archive)

    def test_script_cli_reports_errors_without_traceback(self):
        output = self.directory / "cli"
        result = subprocess.run([sys.executable, "-I", str(ROOT / "scripts/usd_bridge.py"), "--input", str(self.directory / "missing.usdz"), "--output", str(output)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertIn("does not exist", json.loads((output / "error.json").read_text())["message"])
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    if "--write-fixtures" in sys.argv:
        fixtures(ROOT / "fixtures")
    else:
        unittest.main()
