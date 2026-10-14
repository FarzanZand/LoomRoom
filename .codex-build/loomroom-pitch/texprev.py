from PIL import Image,ImageDraw
from pathlib import Path
files=[('C:/Game projects/LoomRoom/Assets/Game/World/Art/Bedroom/Bed/Bed.png','bed'),('C:/Game projects/LoomRoom/Assets/Game/World/Art/Bedroom/Tabletop.png','table'),('C:/Game projects/LoomRoom/Assets/Game/World/Art/Bedroom/Rug.png','rug'),('C:/Game projects/LoomRoom/Assets/Game/Levels/Dungeon1/Dungeon1 Card.png','dungeon')]
out=Image.new('RGBA',(1024,300),(20,22,28,255))
for i,(f,n) in enumerate(files):
 im=Image.open(f).convert('RGBA');im.thumbnail((240,250));x=i*256+8;y=10;out.alpha_composite(im,(x,y));ImageDraw.Draw(out).text((x,y+255),n,fill='white')
out.save('C:/Game projects/LoomRoom/.codex-build/loomroom-pitch/textures.png')
