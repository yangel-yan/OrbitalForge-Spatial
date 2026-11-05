"""Rasterize the project's original geometric app mark; no source bitmap is used."""
from pathlib import Path
from math import cos, sin, pi
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'Assets/SpaceBall/Art/Brand'
OUT.mkdir(parents=True, exist_ok=True)
S = 4
image = Image.new('RGB', (1024*S, 1024*S), '#0B1928')
draw = ImageDraw.Draw(image)
def points(values): return [(round(x*S), round(y*S)) for x,y in values]
def line(values, color, width):
    draw.line(points(values), fill=color, width=round(width*S), joint='curve')
    for x,y in (values[0], values[-1]):
        r=width/2
        draw.ellipse((round((x-r)*S),round((y-r)*S),round((x+r)*S),round((y+r)*S)), fill=color)
def disk(x,y,r,color): draw.ellipse(tuple(round(v*S) for v in (x-r,y-r,x+r,y+r)),fill=color)
def bezier(a,b,c,d):
    return [((1-t)**3*a[0]+3*(1-t)**2*t*b[0]+3*(1-t)*t*t*c[0]+t**3*d[0],
             (1-t)**3*a[1]+3*(1-t)**2*t*b[1]+3*(1-t)*t*t*c[1]+t**3*d[1]) for t in [i/180 for i in range(181)]]
for y in range(1024*S):
    f=y/(1024*S-1)
    draw.line((0,y,1024*S,y), fill=(round(18-7*f),round(43-18*f),round(60-20*f)))
orbit=[(512+342*cos(t),512+342*sin(t)) for t in [(-2.32+i*4.15/240) for i in range(241)]]
line(orbit, '#284957', 5)
path=bezier((270,724),(356,814),(600,779),(694,600))
path+=bezier((694,600),(762,471),(697,344),(561,337))[1:]+[(388,337)]
line([(x,y+14) for x,y in path], '#081522', 154)
line(path, '#B6CED3', 132)
line([(x,y-4) for x,y in path], '#E4EFF0', 110)
line(path, '#193746', 66)
line(path, '#60C7D0', 11)
hexagon=lambda x,y,r:[(x+r*cos(i*pi/3),y+r*sin(i*pi/3)) for i in range(6)]
draw.polygon(points(hexagon(338,337,92)),fill='#DDEBEE')
draw.polygon(points(hexagon(338,337,70)),fill='#355868')
draw.polygon(points(hexagon(338,337,48)),fill='#102B3A')
disk(338,337,26,'#73D6DC')
disk(338,337,12,'#DDEFF0')
disk(695,630,102,'#0A1D2A')
disk(692,619,91,'#D9A459')
for i in range(90,0,-1):
    f=1-i/90
    draw.ellipse(tuple(round(v*S) for v in (692-i-f*16,619-i-f*17,692+i-f*16,619+i-f*17)),
                 fill=(round(222+33*f),round(166+61*f),round(91+62*f)))
disk(660,584,17,'#FFF0CB')
disk(646,573,7,'#FFF8E6')
image.resize((1024,1024), Image.Resampling.LANCZOS).save(OUT/'OrbitalCourierIcon.png')
(OUT/'OrbitalCourierMark.svg').write_text('''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024">
<defs><linearGradient id="bg" x2="0" y2="1"><stop stop-color="#122B3C"/><stop offset="1" stop-color="#0B1928"/></linearGradient><radialGradient id="ball" cx=".32" cy=".28"><stop stop-color="#FFE699"/><stop offset="1" stop-color="#D9A459"/></radialGradient></defs>
<path fill="url(#bg)" d="M0 0h1024v1024H0z"/>
<path d="M279 262A342 342 0 1 1 786 718" fill="none" stroke="#284957" stroke-width="5"/>
<g fill="none" stroke-linecap="round" stroke-linejoin="round"><path id="rail" d="M270 724C356 814 600 779 694 600S697 344 561 337H388" stroke="#B6CED3" stroke-width="132"/><use href="#rail" stroke="#E4EFF0" stroke-width="110"/><use href="#rail" stroke="#193746" stroke-width="66"/><use href="#rail" stroke="#60C7D0" stroke-width="11"/></g>
<path d="M430 337l-46 80h-92l-46-80 46-80h92z" fill="#DDEBEE"/><path d="M408 337l-35 61h-70l-35-61 35-61h70z" fill="#355868"/><path d="M386 337l-24 42h-48l-24-42 24-42h48z" fill="#102B3A"/>
<circle cx="338" cy="337" r="26" fill="#73D6DC"/><circle cx="338" cy="337" r="12" fill="#DDEFF0"/>
<circle cx="695" cy="630" r="102" fill="#0A1D2A"/><circle cx="692" cy="619" r="91" fill="url(#ball)"/><circle cx="660" cy="584" r="17" fill="#FFF0CB"/><circle cx="646" cy="573" r="7" fill="#FFF8E6"/>
</svg>''',encoding='utf-8')
print(OUT/'OrbitalCourierIcon.png')
