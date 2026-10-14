import fs from 'node:fs/promises';
import path from 'node:path';
import { Presentation, PresentationFile } from '@oai/artifact-tool';

const W=1280,H=720;
const C={bg:'#10131A',panel:'#171C25',panel2:'#202733',white:'#F4F0E5',muted:'#ABB2BC',faint:'#737D89',amber:'#E7A94B',rust:'#B75A3C',teal:'#70B6A7',stone:'#4C515B',line:'#353D48'};
const out='C:/Game projects/LoomRoom/pitch-output/LoomRoom_Publisher_Pitch.pptx';
const build='C:/Game projects/LoomRoom/.codex-build/loomroom-pitch';
const art='C:/Users/Farza/.codex/visualizations/2026/10/14/01a13aea-258e-7271-b19d-85150f9ea877/loomroom-research';
const p=Presentation.create({slideSize:{width:W,height:H}});
function shape(s,name,x,y,w,h,fill='none',geom='rect',lineFill='none',lw=0){return s.shapes.add({geometry:geom,name,position:{left:x,top:y,width:w,height:h},fill,line:{style:'solid',fill:lineFill,width:lw}})}
function text(s,name,str,x,y,w,h,size,color=C.white,opts={}){const q=shape(s,name,x,y,w,h); q.text=str; q.text.style={typeface:'Arial',fontSize:size,color,bold:!!opts.bold,alignment:opts.align||'left',verticalAlignment:opts.valign||'middle',wrap:true,autoFit:'shrinkText',insets:{left:0,right:0,top:0,bottom:0},...(opts.italic?{italic:true}:{})};return q;}
function addImg(s,name,file,x,y,w,h,fit='cover'){const full=path.join(art,file);return fs.readFile(full).then(bytes=>s.images.add({name,blob:new Uint8Array(bytes),contentType:'image/png',alt:name,fit,position:{left:x,top:y,width:w,height:h}}))}
function base(title,n,sub=''){const s=p.slides.add();s.background.fill=C.bg; if(title){text(s,'title',title,68,40,1120,52,38,C.white,{bold:true});shape(s,'title rule',68,108,68,4,C.amber); if(sub)text(s,'subtitle',sub,68,123,1120,32,17,C.muted);} if(n){text(s,'wordmark','LOOMROOM',68,676,250,18,12,C.faint,{bold:true});text(s,'page',String(n).padStart(2,'0'),1176,676,36,18,12,C.faint,{align:'right'});} return s;}
function notes(s,ns){s.speakerNotes.text=ns;}
function bullets(s,items,x,y,w,size=21,color=C.white,step=69){items.forEach((it,i)=>{text(s,'bullet dot '+i,'•',x,y+i*step,18,28,size,C.amber,{bold:true});text(s,'bullet '+i,it,x+28,y+i*step,w-28,step-8,size,color);});}
function label(s,txt,x,y,w=180,color=C.amber){text(s,'label '+txt,txt.toUpperCase(),x,y,w,20,12,color,{bold:true});}
function callout(s,head,body,x,y,w,accent=C.amber){shape(s,'callout rule',x,y,4,80,accent);text(s,'callout heading',head,x+20,y,w-20,30,19,C.white,{bold:true});text(s,'callout body',body,x+20,y+32,w-20,54,16,C.muted);}

// 1 — minimal cover with original project art.
{
 const s=p.slides.add();s.background.fill=C.bg;
 await addImg(s,'LoomRoom crypt dungeon card art','cover-bg.png',820,125,380,430,'contain');
 shape(s,'cover left field',0,0,760,H,C.bg);shape(s,'cover accent',70,85,72,5,C.amber);
 text(s,'title','LOOMROOM',70,150,680,90,62,C.white,{bold:true});
 text(s,'subtitle','A dungeon crawl on a bedroom table',74,255,650,60,30,C.amber,{bold:true});
 text(s,'genre','First-person roguelike dungeon crawler',74,340,650,34,22,C.muted);
 text(s,'studio','PUBLISHER PITCH  /  OCTOBER 2026',74,630,520,24,14,C.faint,{bold:true});
 notes(s,'Pitch claim grounded in LoomRoom’s GameDesign.md: first-person dungeon crawler framed by a miniature table, Dungeon Master, and bedroom setting. The cover figure is a project character model and is used as a mood image, not presented as a screenshot.');
}
// 2 — hook
{
 const s=base('A long descent, played on a small table',2,'The player descends through a dungeon while the Dungeon Master watches from the bedroom');
 await addImg(s,'Dungeon Master concept','dmhead.png',78,188,385,400,'contain');
 text(s,'premise','A dungeon crawler staged as a miniature adventure.',510,205,650,80,34,C.white,{bold:true});
 text(s,'detail','Fight through authored rooms, find stronger gear, and build a character over a multi-floor run.',512,312,610,100,24,C.muted);
 callout(s,'The framing device','The bedroom table and Dungeon Master give the descent a story-shaped context.',512,468,560,C.amber);
 notes(s,'This is the central positioning claim. The room and table are project framing. The game design describes 90% of play in the dungeon; avoid presenting the bedroom layer as equally large. Image is a concept asset for the Dungeon Master, not a final in-game screenshot.');
}
// 3 — core loop
{
 const s=base('The run is built around earned upgrades',3,'An easy-to-follow loop connects moment-to-moment combat to long-run character growth');
 const xs=[78,350,622,894]; const heads=['EXPLORE','FIGHT','RECOVER','DESCEND']; const desc=['Find branches, landmarks, secrets and stairs.','Read enemy wind-ups, manage stamina and choose an opening.','Compare loot, equip upgrades and restore health or mana.','Keep gear and skills as the next floor raises the pressure.'];
 heads.forEach((h,i)=>{label(s,h,xs[i],230,230,[C.amber,C.rust,C.teal,C.amber][i]);text(s,'stage '+h,h,xs[i],266,245,48,30,C.white,{bold:true});text(s,'stage detail '+h,desc[i],xs[i],330,222,130,19,C.muted);if(i<3){shape(s,'loop connector '+i,xs[i]+237,289,42,2,C.stone);text(s,'connector head '+i,'›',xs[i]+239,266,40,35,28,C.amber,{bold:true});}});
 shape(s,'carry strip',77,523,1110,1,C.line);text(s,'carry heading','PROGRESS CARRIES BETWEEN FLOORS',78,548,420,22,14,C.amber,{bold:true});text(s,'carry detail','Equipment  /  character level  /  skills  /  health and mana  /  supplies',78,580,1000,34,21,C.white);
 notes(s,'The loop is stated in Docs/GameDesign.md. The visual sequence is an editable native diagram, not a decorative image. Full-run persistence is described as intended design, although AdventureSave and descent functionality exist in project code.');
}
// 4 — combat decision
{
 const s=base('Every attack spends a defensive resource',4,'Attack, block, move or cast: the next choice depends on stamina and positioning');
 await addImg(s,'Sword game asset','sword.png',70,205,270,300,'contain'); await addImg(s,'Shield game asset','shield.png',352,225,235,260,'contain'); shape(s,'spell glow',944,235,150,150,'#8E3E28','ellipse');shape(s,'spell core',976,267,86,86,C.amber,'ellipse');shape(s,'spell heart',1000,291,38,38,'#FFF0B0','ellipse');
 text(s,'sword label','SWORD',110,526,190,30,18,C.white,{bold:true,align:'center'});text(s,'shield label','SHIELD',370,526,190,30,18,C.white,{bold:true,align:'center'});text(s,'magic label','FIREBALL',928,526,190,30,18,C.white,{bold:true,align:'center'});
 shape(s,'tradeoff line',644,290,156,2,C.line);text(s,'tradeoff','OR',698,260,56,48,17,C.amber,{bold:true,align:'center'});
 text(s,'combat choice','Left hand: shield or spell',584,390,270,78,23,C.white,{bold:true,align:'center'});
 text(s,'weapon detail','Swords favor reliable handling. Maces trade speed for stagger and guard pressure.',80,593,560,42,17,C.muted);
 text(s,'magic detail','Fireball pressures enemies at range. Heal trades mana and time for recovery.',650,593,540,42,17,C.muted);
 notes(s,'Weapons and shield images are authored project art, while Fireball is a texture from the project’s spell pack. GameDesign.md specifies one-handed swords and maces, shield or spell in left hand, stamina-driven attacks and blocks. This slide presents the intended systems; the current design scope and precise implementation should be confirmed against a playable build.');
}
// 5 — readable enemy encounters
{
 const s=base('Enemies ask the player to change tactics',5,'Readable wind-ups and recovery openings make each encounter a positioning problem');
 await addImg(s,'Crypt rat character concept','crypt-rat.png',76,185,420,350,'contain');
 text(s,'enemy idea','A guarded caster changes the approach.',565,196,585,60,32,C.white,{bold:true});
 bullets(s,['Circle the shielded enemy or wait for its recovery.','Use a doorway or pillar to control the approach.','Keep stamina for the next block.'],568,298,590,21,C.muted,76);
 shape(s,'fairness rule',566,560,560,1,C.line);text(s,'fairness','Wind-up  /  reach  /  recovery',566,580,560,28,18,C.amber,{bold:true});
 notes(s,'The rat image is a Meshy project character asset. Enemy behavior and fairness rules are described in GameDesign.md. Present the tactical encounter as target design; verify the exact enemy mix and behaviors in the current build before a live publisher meeting.');
}
// 6 — equipment and class progression
{
 const s=base('Loot and skill growth shape a run',6,'A deliberately small combat roster supports meaningful equipment choices');
 const colX=[80,470,860];
 label(s,'CLASSES',colX[0],205,240);text(s,'class roster','Warrior\nWizard\nCleric',colX[0],246,300,190,30,C.white,{bold:true});text(s,'class note','Different starting gear, resources and proficiencies.',colX[0],453,290,70,18,C.muted);
 label(s,'EQUIPMENT',colX[1],205,240,C.teal);text(s,'item roster','Weapons\nShields and armor\nRings and amulets\nFood and potions',colX[1],246,345,200,26,C.white,{bold:true});text(s,'item note','Clear improvements to Attack, Armor Class and attributes.',colX[1],466,330,68,18,C.muted);
 label(s,'SKILLS',colX[2],205,240,C.rust);text(s,'skill roster','Swords  /  Maces  /  Blocking\nSorcery  /  Thaumaturgy\nAthletics  /  Trading',colX[2],246,370,180,23,C.white,{bold:true});text(s,'skill note','Skills rise through successful use. No spendable skill points.',colX[2],455,350,70,18,C.muted);
 notes(s,'Content from GameDesign.md, including proposed three starting classes and seven skills. The detailed balance values are still tuning proposals, so this slide avoids exact percentages or claims of final balance.');
}
// 7 — table as distinctive frame
{
 const s=base('The table makes the world feel watched',7,'A bedroom-scale frame gives the dungeon a memorable place in the world');
 await addImg(s,'Bedroom rug texture','rug.png',76,185,300,340,'contain');await addImg(s,'Dungeon Master concept','dmhead.png',430,185,300,340,'contain');await addImg(s,'Crypt rat portrait','crypt-rat.png',810,185,300,340,'contain');
 text(s,'table caption','THE ADVENTURE HAS A PLACE',78,550,300,22,13,C.amber,{bold:true});
 text(s,'room caption','THE DUNGEON MASTER WATCHES',433,550,330,22,13,C.amber,{bold:true});
 text(s,'framing copy','A rat gnaws through the crypt while the Dungeon Master watches the board.',79,593,1050,44,22,C.white);
 notes(s,'The game design states that a small supporting tabletop and Dungeon Master layer occurs between runs while about 90% of gameplay is in the dungeon. The project has bedroom and boss-stage assets. Images are textured art assets, not assembled scene screenshots.');
}
// 8 — progression scope
{
 const s=base('The full descent is a production target',8,'The current design document outlines 20 floors across six biome blocks and a finale');
 const blocks=[['01–03','Cellars'],['04–06','Crypt'],['07–09','Caves'],['10–12','Ruins'],['13–15','Fortress'],['16–18','Depths'],['19–20','Finale']];
 blocks.forEach((b,i)=>{const x=78+i*160;const col=i<2?C.amber:i===6?C.rust:C.stone;shape(s,'biome mark '+i,x,254,122,14,col);text(s,'floor range '+i,b[0],x,283,132,30,20,C.white,{bold:true});text(s,'biome '+i,b[1],x,320,138,40,17,C.muted);});
 text(s,'scope tag','DESIGN TARGET',78,415,220,25,14,C.amber,{bold:true});
 text(s,'scope caveat','Docs describe the full run. The current Dungeon1 asset is configured for four floors across two stages, so the 20-floor plan still requires production.',78,455,1090,90,23,C.white);
 text(s,'progression detail','Biome changes enemy families, room themes and loot bands. Depth sets the available equipment band.',78,581,1080,50,18,C.muted);
 notes(s,'The game design’s intended full-run layout is six three-floor biome blocks plus a two-floor finale. The current Unity asset Assets/Game/Levels/Dungeon1/Dungeon1.asset has two stages of two floors each. This slide expressly distinguishes design target from the configured current game data.');
}
// 9 — market position
{
 const s=base('A familiar dungeon loop gets a distinctive frame',9,'Comparable games help explain the design space; they do not predict LoomRoom’s sales');
 const comp=[['BARONY','First-person dungeon survival and class-driven runs.','Design reference'],['INSCRYPTION','A tabletop frame that gives a game-within-a-game atmosphere.','Mood reference'],['LOOMROOM','First-person action roguelike progression, staged on a bedroom table.','Own proposition']];
 comp.forEach((row,i)=>{const y=215+i*121;label(s,row[0],82,y,200,i===2?C.amber:C.teal);text(s,'comp desc '+i,row[1],300,y-1,610,58,22,i===2?C.white:C.muted,{bold:i===2});text(s,'comp role '+i,row[2],944,y,240,42,16,i===2?C.amber:C.faint,{align:'right'});if(i<2)shape(s,'comp rule '+i,80,y+83,1110,1,C.line);});
 text(s,'market caveat','Genre comparables are positioning references, not performance forecasts.',82,600,1080,30,17,C.faint,{italic:true});
 notes(s,'Barony is named in GameDesign.md as the reference for dungeon loop, classes and baseline equipment stats. Inscryption is the design reference for the tabletop frame. No market size, sales, wishlist or revenue claims are made because this workspace contains no verified market research or traction data.');
}
// 10 — current evidence
{
 const s=base('The project has a working combat and dungeon foundation',10,'Current evidence from project documentation and Unity assets');
 const left=[['Implemented','Runtime scripts cover player combat, enemies, dungeon generation, loot and run progression.'],['Test record','A project audit records a clean compile and Play-mode smoke test of a generated dungeon, kills and XP.'],['Playable proof needed','A publisher build should show the core loop and the current quality bar in one short session.']];
 left.forEach((it,i)=>{const y=204+i*125;text(s,'proof head '+i,it[0],82,y,270,31,20,i===2?C.amber:C.white,{bold:true});text(s,'proof body '+i,it[1],330,y,830,69,18,C.muted);if(i<2)shape(s,'proof rule '+i,82,y+91,1080,1,C.line);});
 shape(s,'proof note',82,593,1112,1,C.line);text(s,'proof note text','The 20-floor run remains a design target; the current configured dungeon has four floors.',82,609,1100,30,18,C.amber,{bold:true});
 notes(s,'Project audit dated 2026-10-09 reports compiling cleanly and a Play-mode smoke test of a generated dungeon, kills and XP credit. This is not a claim that every mechanic is complete or a final quality playable is ready. Verify and attach a publisher build and gameplay capture before external circulation.');
}
// 11 — roadmap, intentionally not dated
{
 const s=base('A credible plan starts with proof and scope',11,'Proposed production gates, with dates and cost to be set from a bottom-up estimate');
 const steps=[['01','Pitch build','One polished slice that proves combat, loot and the table framing.'],['02','Vertical slice','Prove a representative floor, one class contrast and a boss encounter.'],['03','Content production','Build and tune the approved floor and biome scope.'],['04','Release readiness','QA, accessibility, localization, ports and launch preparation.']];
 steps.forEach((a,i)=>{const y=198+i*103;label(s,a[0],82,y,60,i===0?C.amber:C.teal);text(s,'roadmap head '+i,a[1],158,y-1,270,32,21,C.white,{bold:true});text(s,'roadmap body '+i,a[2],440,y-1,720,55,18,C.muted);if(i<3)shape(s,'roadmap rule '+i,82,y+70,1090,1,C.line);});
 text(s,'roadmap caution','Schedule depends on a locked scope, current build assessment and staffing plan.',82,626,1080,24,15,C.faint,{italic:true});
 notes(s,'These are proposed production gates, not an approved schedule. No team size, budget, build milestone dates, platforms or release window were supplied or found in project documentation. Derive dates from a production plan and developer burn rate before submitting to publishers.');
}
// 12 — partner request & open diligence fields
{
 const s=base('Seeking a publishing partner for the next stage',12,'LoomRoom needs production planning and commercial support grounded in a strong playable');
 text(s,'ask','Publisher partnership',80,203,520,56,32,C.white,{bold:true});
 bullets(s,['Assess the core gameplay build and help define a shippable scope.','Co-develop the production budget, schedule and release plan.','Plan publishing support for marketing, QA, localization and ports.'],82,293,690,20,C.muted,73);
 shape(s,'ask divider',820,202,1,386,C.line);
 label(s,'TO COMPLETE BEFORE SUBMISSION',862,206,320,C.amber);
 text(s,'budget field','Funding ask  [USD budget]',862,250,330,38,19,C.white,{bold:true});
 text(s,'schedule field','Target release  [month / year]',862,310,350,38,19,C.white,{bold:true});
 text(s,'studio field','Studio and team  [names / credits]',862,370,360,40,19,C.white,{bold:true});
 text(s,'proof fields','Build, gameplay video, platforms and traction',862,430,340,52,17,C.muted);
 text(s,'contact','[Contact name]  /  [email]  /  [build link]',80,626,900,25,16,C.faint);
 notes(s,'Direct, specific ask follows Raw Fury’s six questions and Devolver’s requested fields: quick pitch, gameplay video, demo, financial roadmap, publisher support and requested funds. No contact, build URL, budget, platform commitment or release date is asserted. Fill these from team-approved and verified inputs before external circulation.');
}

await fs.mkdir(path.dirname(out),{recursive:true});
await fs.mkdir(build,{recursive:true});
for(let i=0;i<p.slides.items.length;i++){
 const blob=await p.export({slide:p.slides.items[i],format:'png',scale:1});
 await fs.writeFile(path.join(build,`slide-${String(i+1).padStart(2,'0')}.png`),new Uint8Array(await blob.arrayBuffer()));
}
const montage=await p.export({format:'webp',montage:true,scale:0.25});
await fs.writeFile(path.join(build,'montage.webp'),new Uint8Array(await montage.arrayBuffer()));
const draft=path.join(build,'LoomRoom_Publisher_Pitch.draft.pptx');
await (await PresentationFile.exportPptx(p)).save(draft);
console.log(JSON.stringify({draft,out,slides:p.slides.items.length,montage:path.join(build,'montage.webp')}));


