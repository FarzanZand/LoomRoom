from PIL import Image
from pathlib import Path
out=Path('C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research/')
base=Path('Assets/Game/Art/PixelItems/Armory/Singles/Weapon Singles/Iron')
for name,f in [('sword',base/'Iron_Weapon1.png'),('shield',base/'Iron_Shield1.png'),('mace',base/'Iron_Weapon17.png')]:
 im=Image.open(f).convert('RGBA').resize((256,256),Image.Resampling.NEAREST); im.save(out/(name+'.png'))
