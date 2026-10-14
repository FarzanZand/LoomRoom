import zipfile,re,html
for f in [r'C:\Users\Farza\OneDrive\Skrivbord\The Table Waits - Pitch.pptx',r'C:\Game projects\LoomRoom\pitch-output\LoomRoom_Publisher_Pitch.pptx']:
 z=zipfile.ZipFile(f); print('\n',f)
 for n in z.namelist():
  if n.startswith('ppt/notesSlides/notesSlide') and n.endswith('.xml'):
   b=z.read(n).decode('utf-8','ignore'); print(n, ' '.join(html.unescape(re.sub('<[^>]+>',' ',b)).split())[:2500])
