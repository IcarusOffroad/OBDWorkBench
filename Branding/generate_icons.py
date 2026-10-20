from pathlib import Path
import math,subprocess,io,shutil
from PIL import Image,ImageDraw,ImageFont
r=Path(__file__).resolve().parent.parent
d=r/'Branding';d.mkdir(exist_ok=True)
def gear(cx,cy,rad):
 pts=[]
 for i in range(48):
  a=i*math.pi/24; rr=rad if i%4 in (1,2) else rad*.79
  pts.append(f'{cx+math.cos(a)*rr:.2f},{cy+math.sin(a)*rr:.2f}')
 return '<polygon points="'+' '.join(pts)+'" fill="url(#copper)"/>'+f'<circle cx="{cx}" cy="{cy}" r="{rad*.40}" fill="#151a1d"/>'
base='''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 128 128"><defs><linearGradient id="copper" x2=".7" y2="1"><stop stop-color="#f1bb91"/><stop offset="1" stop-color="#bc7548"/></linearGradient><linearGradient id="back" x2="1" y2="1"><stop stop-color="#252b2f"/><stop offset="1" stop-color="#101416"/></linearGradient></defs><rect x="3" y="3" width="122" height="122" rx="26" fill="url(#back)"/><rect x="4.5" y="4.5" width="119" height="119" rx="25" fill="none" stroke="#b27a55" stroke-opacity=".55" stroke-width="2"/>
'''
wing='''<path d="M24 88 C15 63 38 41 75 14 C70 48 50 72 24 88Z" fill="#f3eee3"/><path d="M28 84 C52 57 72 49 81 29 C81 58 58 79 28 84Z" fill="url(#copper)"/><path d="M29 78 Q41 69 53 66 M34 64 Q48 57 61 53 M43 48 Q56 43 68 36" fill="none" stroke="#151a1d" stroke-width="4" stroke-linecap="round"/>'''
wb=base+wing+'''<path d="M69 64 L90 49 L90 34 M76 76 L103 58 L103 44" fill="none" stroke="#d99a6c" stroke-width="4" stroke-linecap="round"/><circle cx="90" cy="29" r="5" fill="#f3eee3"/><circle cx="103" cy="39" r="5" fill="#d99a6c"/>'''+gear(40,94,23)+gear(84,99,15)+'</svg>'
gps=base+wing+'''<circle cx="88" cy="88" r="25" fill="#151a1d" stroke="url(#copper)" stroke-width="5"/><path d="M88 55 V61 M88 115 V121 M55 88 H61 M115 88 H121" stroke="#f3eee3" stroke-width="3" stroke-linecap="round"/><path d="M99 70 L94 98 L84 92 L73 87Z" fill="#f3eee3"/><path d="M99 70 L84 92 L94 98Z" fill="url(#copper)"/></svg>'''
for name,svg in [('IcarusWorkbench',wb),('IcarusGPS',gps)]:
 (d/f'{name}.svg').write_text(svg)
 subprocess.run(['inkscape',str(d/f'{name}.svg'),'--export-type=png','--export-width=1024',f'--export-filename={d/name}.png'],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 img=Image.open(d/f'{name}.png').convert('RGBA')
 sizes=[16,20,24,32,40,48,64,128,256]
 img.save(d/f'{name}.ico',sizes=[(s,s) for s in sizes])
 img.resize((512,512),Image.Resampling.LANCZOS).save(d/f'{name}.png')
 # Separate status variants retain the GPS emblem, with a small tray badge.
 if name=='IcarusGPS':
  for state,col in [('offline','#91989d'),('acquiring','#e4ab35'),('fix','#61d28b')]:
   badge=img.copy();dr=ImageDraw.Draw(badge);dr.ellipse((720,720,1010,1010),fill=col,outline='#101416',width=45)
   badge.save(d/f'IcarusGPS-{state}.ico',sizes=[(s,s) for s in sizes])
preview=Image.new('RGB',(1000,350),'#101416');draw=ImageDraw.Draw(preview)
def load_font(size):
 for candidate in ['/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf','C:/Windows/Fonts/segoeui.ttf']:
  if Path(candidate).exists():return ImageFont.truetype(candidate,size)
 return ImageFont.load_default(size=size)
font=load_font(19)
small=load_font(13)
for j,(name,label) in enumerate([('IcarusWorkbench','OBD Workbench · feather + gears'),('IcarusGPS','GPS Monitor · feather + compass')]):
 x=30+j*500
 im=Image.open(d/f'{name}.png').convert('RGBA');preview.paste(im.resize((144,144)),(x,65),im.resize((144,144)))
 draw.text((x,22),label,font=font,fill='#f3eee3')
 cur=x+185
 for size in [16,24,32,48]:
  a=im.resize((size,size),Image.Resampling.LANCZOS);preview.paste(a,(cur,116-size//2),a)
  draw.text((cur,154),str(size),font=small,fill='#beb7ad');cur+=65
 draw.text((x,238),'Copper / ivory / charcoal · scalable SVG + multi-size ICO',font=small,fill='#beb7ad')
 if j==1:
  for i,state in enumerate(['offline','acquiring','fix']):
   im=Image.open(d/f'IcarusGPS-{state}.ico').convert('RGBA').resize((32,32));preview.paste(im,(x+i*125,286),im)
   draw.text((x+i*125+37,292),state,font=small,fill='#f3eee3')
preview.save(d/'IconPreview.png')

shutil.copy(d/'IcarusWorkbench.ico',r/'OBDWorkbench/Assets/IcarusWorkbench.ico')
gps_assets=r/'GPSService/IcarusGPS_v0.2.4/Assets'
gps_assets.mkdir(exist_ok=True)
for asset in d.glob('IcarusGPS*.ico'):shutil.copy(asset,gps_assets/asset.name)
print('Generated branding and synchronized runtime icon copies.')
