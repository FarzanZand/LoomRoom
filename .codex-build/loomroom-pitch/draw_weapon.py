from PIL import Image,ImageDraw
from pathlib import Path
out=Path('C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research')
for name,color in [('sword',(230,180,110,255)),('shield',(65,142,135,255)),('mace',(180,90,60,255))]:
 im=Image.new('RGBA',(256,256),(0,0,0,0));d=ImageDraw.Draw(im)
 if name=='sword':
  d.polygon([(124,10),(132,10),(140,158),(116,158)],fill=color);d.rectangle((102,154,154,166),fill=(190,140,80,255));d.rectangle((123,166,133,223),fill=(85,55,43,255));d.rectangle((118,220,138,230),fill=(205,165,95,255))
 elif name=='shield':
  d.polygon([(40,28),(216,28),(204,160),(128,228),(52,160)],fill=color,outline=(231,169,75,255),width=8);d.rectangle((112,48,144,190),fill=(230,190,125,255));d.rectangle((62,100,194,132),fill=(230,190,125,255))
 else:
  d.rectangle((118,72,138,222),fill=(105,72,50,255));d.rectangle((104,198,152,214),fill=(196,144,73,255));d.polygon([(78,40),(178,40),(168,120),(146,145),(110,145),(88,120)],fill=color,outline=(228,167,87,255),width=6)
 im.save(out/(name+'.png'))
