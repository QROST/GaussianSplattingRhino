#!/usr/bin/env python3
"""Build a strictly allowlisted local Yak candidate and verify all archived bytes. Never publish."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent

def manifest_values(text):
    values, current = {}, None
    for raw in text.splitlines():
        if not raw.strip() or raw == '---' or raw.lstrip().startswith('#'): continue
        if raw.lstrip().startswith('- '):
            if current not in ('authors', 'keywords'): raise ValueError('Unexpected manifest list.')
            values[current].append(raw.strip()[2:]); continue
        if raw.startswith(' '):
            if current != 'description': raise ValueError('Unexpected manifest continuation.')
            values[current] = (values[current] + ' ' + raw.strip()).strip(); continue
        match = re.fullmatch(r'([A-Za-z]+):(?: (.*))?', raw)
        if not match or match[1] in values: raise ValueError('Invalid manifest field.')
        current, value = match[1], match[2] or ''
        if current in ('authors', 'keywords'): values[current] = []
        elif value == '>-': values[current] = ''
        elif value.startswith("'") and value.endswith("'"): values[current] = value[1:-1].replace("''", "'")
        else: values[current] = value
    return values

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--platform', choices=['mac','win'], required=True)
    parser.add_argument('--yak', required=True)
    parser.add_argument('--checks', choices=['passed','not-rerun'], default='not-rerun')
    args = parser.parse_args()
    subprocess.run([sys.executable, str(ROOT/'scripts/create-gs-assets.py'), '--check'], check=True)
    version = ET.parse(ROOT/'Directory.Build.props').findtext('.//Version')
    manifest = (ROOT/'packaging/manifest.yml').read_text()
    values = manifest_values(manifest)
    if values['version'] != version or values['name'] != 'gsrhino': raise ValueError('Project/package identities disagree.')
    artifacts = ROOT/'artifacts'; artifacts.mkdir(exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix='package-'+args.platform+'.', dir=artifacts))
    output = ROOT/'src/GSRhino.Rhino/bin/Release/net7.0'
    required = {name: output/name for name in ['GSRhino.Rhino.rhp','GSRhino.Core.dll','ZstdSharp.dll','usd/usd_bridge.py','assets/gaussian-opacity-atlas.png']}
    required['manifest.yml'] = ROOT/'packaging/manifest.yml'
    for name in ['LICENSE','NOTICE','readme.md','readme.zh-CN.md','scripts/setup-usd.py']:
        required[name] = ROOT/name
    for folder, suffixes in [('docs',{'.md','.txt'}),('fixtures',{'.ply','.splat','.spz','.usd','.usda','.usdc','.usdz','.md','.json'})]:
        for path in sorted((ROOT/folder).rglob('*')):
            if path.is_file() and path.suffix in suffixes:
                required[path.relative_to(ROOT).as_posix()] = path
    for name, source in required.items():
        target = stage/name; target.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(source,target)
    subprocess.run([str(Path(args.yak).resolve()),'build','--platform',args.platform],cwd=stage,check=True)
    archives = list(stage.glob('*.yak'))
    if len(archives)!=1: raise ValueError('Expected one Yak archive.')
    archive = archives[0]
    if archive.name != f'gsrhino-{version}-rh8_32-{args.platform}.yak': raise ValueError('Unexpected package identity or target: '+archive.name)
    with zipfile.ZipFile(archive) as package:
        entries = [i for i in package.infolist() if not i.is_dir()]
        names = [i.filename for i in entries]
        if len({n.casefold() for n in names}) != len(names): raise ValueError('Duplicate archive entries.')
        if set(names) != set(required): raise ValueError('Unexpected archive payload: '+str(set(names)^set(required)))
        for entry in entries:
            path = PurePosixPath(entry.filename)
            if path.is_absolute() or '..' in path.parts or '\\' in entry.filename: raise ValueError('Unsafe archive path.')
            if entry.filename == 'manifest.yml':
                expected = dict(values, platform=args.platform)
                if manifest_values(package.read(entry).decode()) != expected: raise ValueError('Metadata mismatch.')
            elif package.read(entry) != required[entry.filename].read_bytes(): raise ValueError('Payload mismatch: '+entry.filename)
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    archive.with_suffix('.yak.sha256').write_text(digest+'  '+archive.name+'\n')
    commit = subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()
    dirty = bool(subprocess.check_output(['git','status','--porcelain'],cwd=ROOT,text=True).strip())
    receipt = {'sourceCommit':commit,'sourceDirty':dirty,'name':'gsrhino','version':version,'platform':args.platform,
        'path':str(archive),'sha256':digest,'files':len(required),'checks':args.checks,'hostAcceptance':'pending',
        'createdUtc':datetime.now(timezone.utc).isoformat()}
    (artifacts/f'latest-package-{args.platform}.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print(json.dumps(receipt,indent=2))
    print('Local candidate only. No installation or publication performed.')

if __name__ == '__main__': main()
