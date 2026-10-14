from PIL import Image,ImageDraw
from pathlib import Path
out='C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research/'
files={'sword':'Assets/Game/Items/Icons/Generated/Iron Sword.png','shield':'Assets/Game/Items/Icons/Generated/Iron Shield.png','mace':'Assets/Game/Items/Icons/Generated/Iron Mace.png'}
for name,f in files.items():
 im=Image.open(f).convert('RGBA')
 crop=im.getbbox() and im.crop(im.getbbox())
 if name in ('sword','mace'):
  crop=crop.resize((crop.width,crop.height//3),Image.Resampling.NEAREST)
 crop.thumbnail((256,256),Image.Resampling.LANCZOS)
 canvas=Image.new('RGBA',(256,256),(0,0,0,0));canvas.alpha_composite(crop,((256-crop.width)//2,(256-crop.height)//2));canvas.save(out+name+'.png')
# fire using programmatic vector not allowed; use source pixelart icon enlarged
im=Image.open('Assets/Game/Art/PixelItems/Skills/Singles/02_Fire_Element.png').convert('RGBA').resize((256,256),Image.Resampling.NEAREST)
im.save(out+'fireball.png')
