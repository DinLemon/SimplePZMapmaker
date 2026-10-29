"""Render original, procedural README artwork; no game or map-provider imagery."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'docs'/'branding'
W,H=960,360
BG='#172920'; PANEL='#20392b'; INK='#eee6cf'; MUTED='#a6b69f'; AMBER='#dcb974'
FONT_PATHS=[Path('C:/Windows/Fonts/segoeui.ttf'),Path('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf')]
def font(size):
 for path in FONT_PATHS:
  if path.is_file():return ImageFont.truetype(str(path),size)
 return ImageFont.load_default(size=size)
FONTS={size:font(size) for size in (12,14,17,23)}
def text(d,xy,value,size=14,fill=INK):d.text(xy,value,font=FONTS[size],fill=fill)
def house(d,x,y):
 d.polygon([(x-9,y),(x+2,y+6),(x+2,y+17),(x-9,y+11)],fill='#dfd4b8')
 d.polygon([(x+2,y+6),(x+12,y),(x+12,y+11),(x+2,y+17)],fill='#b3b498')
 d.polygon([(x-12,y),(x,y-8),(x+15,y),(x+2,y+8)],fill=AMBER)
 d.rectangle((x-5,y+7,x-2,y+12),fill=BG)
def frame(n):
 phase=min(3,n//24);t=(n%24)/23
 im=Image.new('RGB',(W,H),BG);d=ImageDraw.Draw(im)
 text(d,(28,19),'Choose a place. Build a connected world.',23)
 text(d,(29,52),'SimplePZMapmaker  /  Project Zomboid Build 42',12,MUTED)
 labels=[('Choose an area','Keep its bounds and scale.'),('Create the terrain','Roads, water and section seams.'),('Add buildings','Furnished rooms, editable plans.'),('Export by section','Compile one connected map mod.')]
 for i,(title,detail) in enumerate(labels):
  y=102+i*49;active=i==phase
  d.ellipse((30,y+1,51,y+22),fill=AMBER if active else PANEL)
  text(d,(36,y+3),str(i+1),12,BG if active else MUTED)
  text(d,(65,y-1),title,17,INK if active else MUTED)
  if active:text(d,(65,y+21),detail,12,MUTED)
 x0,y0,cw,ch=371,99,101,66
 for row in range(3):
  for col in range(4):
   x=x0+col*cw;y=y0+row*ch
   ready=phase>=2 or phase==1 and (row*4+col)<int(t*13)
   fill=('#526f48' if (row+col)%3 else '#678553') if ready else PANEL
   d.rectangle((x,y,x+cw-1,y+ch-1),fill=fill)
 if phase>=2 or phase==1 and t>.8:
  d.polygon([(545,99),(564,99),(582,149),(555,195),(575,247),(563,296),(544,296),(553,247),(536,195),(562,149)],fill='#527d81')
  d.line([(371,169),(773,169)],fill='#c4c0a7',width=12)
  d.line([(473,99),(473,296)],fill='#c4c0a7',width=10)
  d.line([(371,237),(773,237)],fill='#b4b39d',width=9)
  for x in range(383,770,26):d.line([(x,169),(x+10,169)],fill=BG,width=2)
 if phase>=2:
  positions=[(400,132),(438,132),(507,132),(613,132),(670,132),(725,132),(402,210),(505,210),(616,210),(666,210),(728,210),(409,277),(511,277),(615,277),(672,277),(726,277)]
  for index,(x,y) in enumerate(positions):
   if phase==3 or index<=int(t*len(positions)):house(d,x,y)
 for row in range(4):d.line([(x0,y0+row*ch),(x0+4*cw,y0+row*ch)],fill='#a8bb92',width=1)
 for col in range(5):d.line([(x0+col*cw,y0),(x0+col*cw,y0+3*ch)],fill='#a8bb92',width=1)
 if phase==0:
  d.rectangle((x0-5,y0-5,x0+4*cw+4,y0+3*ch+4),outline=AMBER,width=2)
  x=x0+int(t*4*cw);d.polygon([(x,y0+3*ch+14),(x+9,y0+3*ch+24),(x+5,y0+3*ch+26)],fill=INK)
 if phase==3:
  tile=min(11,int(t*12));row,col=divmod(tile,4);x=x0+col*cw;y=y0+row*ch
  d.rectangle((x+2,y+2,x+cw-2,y+ch-2),outline=AMBER,width=3)
  d.line([(777,198),(820,198)],fill=AMBER,width=2)
  d.polygon([(817,193),(825,198),(817,203)],fill=AMBER)
  d.rounded_rectangle((830,153,931,244),radius=5,fill=PANEL,outline=AMBER,width=2)
  text(d,(852,166),'B42',23,AMBER);text(d,(847,203),'map mod',14)
  d.line([(845,228),(857,238),(884,217)],fill=AMBER,width=3)
 text(d,(372,314),'Selected area' if phase==0 else 'Terrain and paths' if phase==1 else 'Editable buildings' if phase==2 else 'Sections written one at a time',14)
 text(d,(29,334),'Workflow illustration',12,MUTED)
 d.rectangle((371,342,775,345),fill=PANEL);d.rectangle((371,342,371+int(((phase+t)/4)*404),345),fill=AMBER)
 return im

def main():
 OUT.mkdir(parents=True,exist_ok=True)
 frames=[frame(n) for n in range(96)]
 palette=Image.new('P',(1,1));colors=Image.new('RGB',(len(frames),1))
 # A common palette prevents frame-to-frame color flicker.
 palette=frames[-1].quantize(colors=64)
 indexed=[im.quantize(palette=palette,dither=Image.Dither.NONE) for im in frames]
 indexed[0].save(OUT/'world-assembly.gif',save_all=True,append_images=indexed[1:],duration=75,loop=0,optimize=True,disposal=1)
 frames[-1].save(OUT/'world-assembly-poster.png',optimize=True)
 print('Workflow GIF:',(OUT/'world-assembly.gif').stat().st_size,'bytes')

if __name__=='__main__':main()
