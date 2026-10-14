import {Presentation, PresentationFile} from '@oai/artifact-tool';
const p=Presentation.create({slideSize:{width:1280,height:720}});
const s=p.slides.add();
console.log('ready', typeof p.slides.add, typeof s.shapes.add, typeof PresentationFile.exportPptx);
