from PIL import Image
for f in ['Assets/Game/World/Art/Bedroom/Bed/Bed.png','Assets/Game/World/Art/Bedroom/Tabletop.png','Assets/Game/World/Art/Bedroom/Plaster.png','Assets/Game/World/Art/Bedroom/Floorboards.png','Assets/Game/World/Art/Bedroom/Rug.png']:
 im=Image.open(f);print(f,im.mode,im.getpixel((im.width//2,im.height//2)))
