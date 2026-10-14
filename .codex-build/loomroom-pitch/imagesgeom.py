import zipfile,xml.etree.ElementTree as ET,html,re
f=r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx';z=zipfile.ZipFile(f);ns={'a':'http://schemas.openxmlformats.org/drawingml/2006/main','p':'http://schemas.openxmlformats.org/presentationml/2006/main'}
for i in range(1,13):
 root=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); print('\nSLIDE',i)
 for sp in root.findall('.//p:sp',ns):
  tx=' '.join(t.text or '' for t in sp.findall('.//a:t',ns)); xf=sp.find('./p:spPr/a:xfrm',ns)
  if tx.strip() and xf is not None:
   off=xf.find('a:off',ns); ext=xf.find('a:ext',ns);print(tx[:40], [round(int(v)/914400,2) for v in (off.get('x'),off.get('y'),ext.get('cx'),ext.get('cy'))])
