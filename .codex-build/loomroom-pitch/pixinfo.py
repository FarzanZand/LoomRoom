from PIL import Image
from pathlib import Path
for f in ['Assets/Game/Art/PixelItems/Armory/Singles/Weapon Singles/Iron/Iron_Weapon1.png','Assets/Game/Art/PixelItems/Armory/Singles/Weapon Singles/Iron/Iron_Weapon12.png','Assets/Game/Art/PixelItems/Armory/Singles/Weapon Singles/Iron/Iron_Shield1.png','Assets/Game/Art/PixelItems/Skills/Singles/02_Fire_Element.png','Assets/Game/Art/PixelItems/Miscellaneous/Singles/177_Lava_Orb.png','Assets/Game/Art/PixelItems/Miscellaneous/Singles/176_Orb_White.png']:
 im=Image.open(f);print(f,im.size,im.mode,im.getbbox())
