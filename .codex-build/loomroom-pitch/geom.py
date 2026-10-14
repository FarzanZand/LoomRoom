import zipfile,xml.etree.ElementTree as ET
f=r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx'; z=zipfile.ZipFile(f); ns={'a':'http://schemas.openxmlformats.org/drawingml/2006/main','p':'http://schemas.openxmlformats.org/presentationml/2006/main'}
for i in [1,2,3,4,5,6,7,8,9,10,11,12]:
 root=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); print('\nSLIDE',i)
 for sp in root.findall('.//p:sp',ns):
  tx=' '.join((t.text or '') for t in sp.findall('.//a:t',ns)); xfrm=sp.find('.//a:xfrm',ns); box=''
  if xfrm is not None:
   off=xfrm.find('a:off',ns); ext=xfrm.find('a:ext',ns); box=f'[{off.get("x")},{off.get("y")},{ext.get("cx")},{ext.get("cy")}]' if off is not None and ext is not None else ''
  if tx.strip(): print(box,tx[:150])
