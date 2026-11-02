# Third-party components and references

GSRhino code and documentation are maintained by **CuriousArc · qrost** under Apache 2.0. Third-party license notices below retain their required original attribution.

- **RhinoCommon 8.32.26160.13001** is a compile-time SDK dependency. RhinoCommon/Eto are supplied by the user's Rhino installation and are not redistributed in the Yak archive. [Rhino developer documentation](https://developer.rhino3d.com/).
- **ZstdSharp.Port 0.8.8**, MIT, is distributed as `ZstdSharp.dll` for SPZ v4 decompression. [Pinned upstream source](https://github.com/oleg-st/ZstdSharp/tree/2cd0c019693bc786a5fe5c3be94e107b24e7267e); [license notice](licenses/ZstdSharp.txt).
- **OpenUSD / usd-core 26.11** is an optional separately installed decoder, under the terms included with the official wheel (TOST-1.0). The Yak package includes the independently implemented bridge and setup script, not the USD runtime. [Official distribution and license metadata](https://pypi.org/project/usd-core/26.11/).
- **SPLAT** and **SPZ** decoding follows their public MIT-licensed format specifications. Reference encoders are used only to produce tiny validation fixtures, not shipped as native libraries. Sources are listed in [formats](formats.md); [SPZ license notices](licenses/SPZ.txt) accompany the reference-derived decoder.
- xUnit and Microsoft.NET.Test.Sdk are development/test dependencies and are not shipped to Rhino.

The Gaussian covariance, projection, SH and compositing implementation and procedural fixtures are independent. No research CUDA rasterizer is bundled.
