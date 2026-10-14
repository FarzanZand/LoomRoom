import zipfile,xml.etree.ElementTree as ET
f=r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx';z=zipfile.ZipFile(f);ns={'p':'http://schemas.openxmlformats.org/presentationml/2006/main','a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
for i in [3,4,6,7,9,10]:
 r=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); print('\nslide',i)
 for sh in r.findall('.//p:sp',ns):
  tx=' '.join(t.text or '' for t in sh.findall('.//a:t',ns)); xf=sh.find('./p:spPr/a:xfrm',ns)
  if xf is None or not tx.strip(): continue
  o=xf.find('a:off',ns); e=xf.find('a:ext',ns); x,y,w,h=[int(v)/914400 for v in(o.get('x'),o.get('y'),e.get('cx'),e.get('cy'))]
  if x>6.5 and w>4: print(tx[:60], [round(q,2) for q in (x,y,w,h)])
