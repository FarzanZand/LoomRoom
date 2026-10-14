import {GlobalFonts} from '@napi-rs/canvas';console.log(GlobalFonts.families.map(f=>f.family).filter(x=>['Arial','Aptos','Inter','DejaVu Sans'].includes(x)).slice(0,10));
