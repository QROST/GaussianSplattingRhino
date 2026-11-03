# GSRhino

**Rhino 8 本地 Gaussian Splat 查看插件。** 作者：**CuriousArc · qrost**。

[English](readme.md) · 版本 **0.1.0-beta.3** · 本地预览候选版

导入高斯场景，在 Rhino 原生建模视窗中显示各向异性高斯、透明度及随视角变化的 SH 颜色。完整保留受支持的高斯数据，不转为点云、不静默抽样，也不需要账号或 API Key。

## 支持格式

| 文件 | 当前支持范围 |
| --- | --- |
| `.ply` | 标准 3DGS 字段：位置、对数尺度、WXYZ 四元数、透明度 logit、SH0–3；支持 ASCII、大小端二进制。仅含 RGB 点的普通 PLY 不是 GS。 |
| `.splat` | 标准 32 字节记录，包含位置、线性尺度、四元数和 RGBA；该格式本身不包含高阶 SH。 |
| `.spz` | v1–4、受支持的坐标约定和 SH0–3。四阶 SH 明确拒绝，不截断；无法恢复源格式量化时已丢失的信息。 |
| `.usd`、`.usda`、`.usdc`、`.usdz` | 双向导入/导出 OpenUSD 官方 `ParticleField3DGaussianSplat` 静态场景，支持多部分和完整变换；需可选的本地 USD 运行时。普通网格 USDZ 不是 GS，暂不兼容任意厂商自定义结构。 |

详见[格式范围及来源](docs/formats.md)。不人为限制高斯总数；实际可处理规模仍由内存、宿主和运行时决定。

## 安装

- Rhino **8.32 或更高版本**，使用现代 .NET 运行时。提供 macOS 与 Windows 构建目标。
- 本独立插件仍需原生宿主验收；生成 Windows 安装包不代表已在 Windows Rhino 中实测。
- 用 Rhino 的 Yak 工具安装对应平台的本地 `.yak` 包，再手动重启 Rhino。不要只复制 RHP，它还依赖相邻的库和资源。

源码目录中安装 .NET SDK 8 后可执行：

```sh
dotnet build GSRhino.sln -c Release
dotnet test tests/GSRhino.Tests/GSRhino.Tests.csproj -c Release --no-build
bash scripts/build-package.sh
python3 scripts/install-local.py MAC安装包路径
```

Windows 使用 `scripts/build-package.ps1` 打包，再用 Yak 安装 Windows 候选包。安装包和校验记录位于 `artifacts/`；打包脚本不上传、不发布。

### USD / USDZ 可选运行时

PLY、SPLAT、SPZ 无需额外运行时。USD 系列需使用 **Python 3.11–3.14** 执行一次：

```sh
python3.12 scripts/setup-usd.py
python3.12 scripts/setup-usd.py --check
```

Windows 可用 `py -3.12 scripts/setup-usd.py`。脚本把固定版本的官方 `usd-core==26.11` 安装到独立环境，不修改 Rhino 或系统 Python。首次安装需下载依赖，也支持 `--wheel-dir 目录` 离线安装；之后导入和导出完全在本机完成。

默认位置：macOS `~/Library/Application Support/GSRhino/usd-runtime`；Windows `%LOCALAPPDATA%\GSRhino\usd-runtime`。需要自定义时，在启动 Rhino 前设置 `GSRHINO_USD_PYTHON` 指向该环境的 Python 可执行文件。

## 使用

1. 运行 **`GSRhinoPanel`**，选择本地高斯文件。
2. 确认向上轴、源单位及额外比例倍率，点击导入当前文档；面板支持取消。
3. 导入后自动启用 **GSRhino (Experimental)** 显示模式。在线框等其他模式下显示引用框；导航时可能暂时显示包围盒，待新视角的完整 GS 画面准备好后恢复。
4. **`GSRhinoMode`** 启用或重新加载显示；**`GSRhinoDiagnostics`** 查看状态及日志、缓存位置；**`GSRhinoProbe`** 创建离线颜色与遮挡试验。

**`GSRhinoImport`** 可直接选文件，以 Auto 轴向、Auto 单位、1 倍比例导入。源单位缺失时按 1 文件单位 = 1 文档单位处理，向上轴缺失时默认 Z；USD 的单位与轴向元数据在 Auto 模式下生效；若未声明，则使用 OpenUSD 标准默认值（每单位 0.01 米及其默认向上轴）。

### 导出便携 GS 场景

选中顶层 GS 块后，运行 **`GSRhinoExport`**，或点击面板中的 **导出 USD / USDZ**。面板也可以明确选择 **全部顶层 GS**，隐藏和锁定的块会作为可见资产一并导出。空选择不会自动导出整个文档；混选非 GS 对象时会提示调整选择。Mesh、Brep、NURBS 和嵌套块不属于此次 GS 导出范围。

`.usdz` 是自包含便携包，`.usd`/`.usdc` 是二进制场景，`.usda` 是文本场景。导出记录点击时的块变换和文档单位，再后台写入，支持取消。保留全部高斯和 SH0–3 系数；几何遵循官方格式的 float32 精度，不能保证任意 double 值逐位无损。双精度变换和局部原点重定位可减少大坐标精度损失。导出前请设置明确的标准长度单位。详见 [USD 交换范围及手动验证](docs/usd-exchange.md)。

### 已有 3DM 文档

0.1.0-beta.2 开始可识别其他命名空间下兼容的 v1 GS 块引用。打开已有 3DM 后运行 `GSRhinoMode`，只要原先引用的本地 PLY 仍在，就无需重新导入。插件核对文档记录的 SHA-256 和完整高斯数据，复制到自己的缓存，沿用现有块的位置、缩放和旋转；不改写原文档或源文件。缓存缺失或损坏时保留包围盒，并给出具体错误。

## 文件保存与当前限制

原始文件和完整的受支持字段 PLY 缓存保存在 GSRhino 的本地应用数据目录。3DM 保存原生块引用、变换和缓存信息，**3DM 本身不包含高斯资产**。请随 3DM 保留本地缓存，或导出完整 GS 场景为 USDZ 便携文件；这不包含普通 Rhino 几何。USD 根层备份不自动包含外部引用文件，但展开后的 GS 缓存允许本机重开文档时不再使用 USD 解码器。

渲染使用后台投影、排序和原生批量绘制，目前仍为实验性适配，尚非专用 GPU 计算渲染器。支持 SH0–3，显示 RGB 为 8 位；HDR、格式特定的精确抗锯齿补偿、与任意半透明 Rhino 对象交错、嵌套块编辑、布局和截图输出仍未完成。原生不透明几何遮挡与裁剪需逐平台按[宿主验收清单](docs/host-acceptance.md)确认。

[架构](docs/architecture.md) · [验证记录](docs/validation.md) · [开发计划](docs/plan.md) · [第三方说明](docs/third-party.md)

采用 [Apache 2.0](LICENSE) 许可。GSRhino 是独立插件；Rhino 属于 Robert McNeel & Associates。
