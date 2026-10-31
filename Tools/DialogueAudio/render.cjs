// Uses the pinned upstream Babble Engine 1.4.0, unchanged, with approved B/D tuning.
const fs=require('node:fs'),crypto=require('node:crypto');
const {Babble}=require('./engine.cjs');
const settings={
 player:{voice:'fern',pitch:310,speed:1.4,expression:.42,throat:1.03,breath:.08,brightness:.4,hiss:.65},
 mentor:{voice:'lowell',pitch:205,speed:1.12,expression:.33,throat:.96,breath:.1,brightness:.36,hiss:.6}
};
const items=JSON.parse(fs.readFileSync(0,'utf8'));
const rows=[];
for(const item of items){
 const r=Babble.render(item.spoken,{style:'animal',mood:'neutral',seed:31,sampleRate:48000,...settings[item.role]});
 let peak=0;for(const x of r.pcm){if(!Number.isFinite(x))throw Error('Invalid PCM');peak=Math.max(peak,Math.abs(x));}
 if(peak>=1||peak===0)throw Error('Silent/clipped '+item.text);
 const wav=Buffer.from(Babble.toWav(r));
 const file='Assets/Resources/'+item.clip+'.wav';
 if(!fs.existsSync(file)||!fs.readFileSync(file).equals(wav))fs.writeFileSync(file,wav);
 rows.push({...item,duration:r.duration,peak,sha256:crypto.createHash('sha256').update(wav).digest('hex')});
}
const catalog={engineVersion:Babble.version,engineSha256:crypto.createHash('sha256').update(fs.readFileSync(__dirname+'/engine.cjs')).digest('hex'),settings,lines:rows};
fs.writeFileSync('Assets/Resources/DialogueAudio/catalog.json',JSON.stringify(catalog,null,2)+'\n');
console.log('Generated '+rows.length+' dialogue clips. Peak and finite-sample checks passed.');
