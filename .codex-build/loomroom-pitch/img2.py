from PIL import Image
for f in ['Assets/Game/Art/Synty Pixel/Props/Menu figure/Menu figure painted.png','Assets/Game/Art/Synty Pixel/Props/Menu figure/Menu figure.png','Assets/Game/World/Art/Bedroom/Bed/Bed.png']:
 try:
  im=Image.open(f);print(f, im.size, im.mode, im.getbbox())
 except Exception as e: print(e)
