from PIL import Image,ImageDraw
from pathlib import Path
files=[f'Assets/Game/Art/PixelItems/Armory/Singles/Weapon Singles/Iron/Iron_Weapon{i}.png' for i in range(1,37)]
out=Image.new('RGBA',(720,720),(20,22,28,255))
for i,f in enumerate(files):
 im=Image.open(f).convert('RGBA').resize((80,80),Image.Resampling.NEAREST); x=(i%9)*80;y=(i//9)*180;out.alpha_composite(im,(x,y));ImageDraw.Draw(out).text((x+2,y+82),str(i+1),fill='white')
out.save('C:/Game projects/LoomRoom/.codex-build/loomroom-pitch/weapons-grid.png')
