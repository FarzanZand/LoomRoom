import zipfile,xml.etree.ElementTree as ET
f=r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx';z=zipfile.ZipFile(f);ns={'p':'http://schemas.openxmlformats.org/presentationml/2006/main'}
for i in [2,5,6,8,10]:
 r=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); print('\n',i)
 for pic in r.findall('.//p:pic',ns):
  nv=pic.find('p:nvPicPr/p:cNvPr',ns);xf=pic.find('p:spPr/a:xfrm',{'p':ns['p'],'a':'http://schemas.openxmlformats.org/drawingml/2006/main'})
  if xf is not None:
   off=xf.find('{http://schemas.openxmlformats.org/drawingml/2006/main}off');ext=xf.find('{http://schemas.openxmlformats.org/drawingml/2006/main}ext');print(nv.attrib.get('name'),[round(int(v)/914400,2) for v in(off.get('x'),off.get('y'),ext.get('cx'),ext.get('cy'))])
