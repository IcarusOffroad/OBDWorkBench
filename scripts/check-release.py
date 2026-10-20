#!/usr/bin/env python3
"""Check source-package hygiene, Markdown targets and Windows icon resources."""
from pathlib import Path
import re,struct,sys

root=Path(__file__).resolve().parent.parent
ignored={'.git','.vs','.vscode','.idea','bin','obj','publish','__pycache__'}
files=[p for p in root.rglob('*') if p.is_file() and not any(x in ignored for x in p.relative_to(root).parts)]
errors=[]
required=['scripts/Build-Workbench.ps1','README.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md','CONTRIBUTING.md','SECURITY.md',
 'docs/README.md','docs/BUILDING.md','docs/QUICK_START.md','docs/USER_GUIDE.md','docs/GPS.md',
 'docs/TROUBLESHOOTING.md','docs/SUPPORT_BUNDLES.md','docs/RELEASING.md','docs/VALIDATION.md',
 'Branding/README.md','OBDWorkbench.sln','ReferenceData/SSM_DTC_MAP.csv',
 'GPSService/README_WORKBENCH.md','GPSService/IcarusGPS_v0.2.4/BUILD.bat',
 'GPSService/IcarusGPS_v0.2.4/INSTALL.bat','.gitignore','.github/workflows/windows.yml']
for name in required:
 if not (root/name).is_file():errors.append('Missing required file: '+name)
for p in files:
 rel=p.relative_to(root).as_posix()
 if p.name.endswith('_wpftmp.csproj') or p.suffix.lower() in {'.log','.pdb','.zip','.exe','.dll'}:
  errors.append('Generated/private artifact in source: '+rel)
 if p.suffix=='.md':
  text=re.sub(r'```.*?```','',p.read_text(),flags=re.S)
  for target in re.findall(r'!?\[[^\]]*\]\(([^)]+)\)',text):
   target=target.strip().split(' "')[0].split('#')[0]
   if not target or re.match(r'^[a-zA-Z][a-zA-Z0-9+.-]*:',target):continue
   if not (p.parent/target).exists():errors.append(f'Broken Markdown target in {rel}: {target}')
for name in ['IcarusWorkbench','IcarusGPS','IcarusGPS-offline','IcarusGPS-acquiring','IcarusGPS-fix']:
 p=root/'Branding'/f'{name}.ico'
 if not p.exists():errors.append('Missing ICO: '+name);continue
 data=p.read_bytes();reserved,kind,count=struct.unpack_from('<HHH',data)
 sizes={((data[6+16*i] or 256),(data[7+16*i] or 256)) for i in range(count)}
 if reserved!=0 or kind!=1 or not {(16,16),(24,24),(32,32),(48,48),(256,256)}.issubset(sizes):errors.append('ICO lacks required sizes: '+name)
pairs=[('Branding/IcarusWorkbench.ico','OBDWorkbench/Assets/IcarusWorkbench.ico')]
for p in (root/'Branding').glob('IcarusGPS*.ico'):pairs.append((p.relative_to(root).as_posix(),'GPSService/IcarusGPS_v0.2.4/Assets/'+p.name))
for original,copy in pairs:
 if not (root/copy).exists() or (root/original).read_bytes()!=(root/copy).read_bytes():errors.append('Icon consumer copy mismatch: '+copy)
if errors:
 print('\n'.join(errors));sys.exit(1)
print(f'PASS: {len(files)} source files; required documentation, Markdown targets, source hygiene and icon sizes/copies checked.')
