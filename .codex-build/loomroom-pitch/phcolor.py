import zipfile,xml.etree.ElementTree as ET
z=zipfile.ZipFile(r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx');ns={'p':'http://schemas.openxmlformats.org/presentationml/2006/main','a':'http://schemas.openxmlformats.org/drawingml/2006/main'}
for i in [1,2,3,4,5,6,7,8,9,10,11,12]:
 r=ET.fromstring(z.read(f'ppt/slides/slide{i}.xml')); print('\n',i)
 for sp in r.findall('.//p:sp',ns):
  tx=' '.join(t.text or '' for t in sp.findall('.//a:t',ns)); p=sp.find('.//a:pPr',ns);ln=sp.find('.//a:ln',ns);gf=sp.find('.//a:solidFill/*',ns)
  col=gf.tag.split('}')[-1]+':'+str(gf.attrib) if gf is not None else ''
  if tx.strip() and any(x in tx for x in ['[','[number]','[date]','[window]','[price]','[link]','[email]']):
   print('placeholder',tx[:100], 'color',col)
