const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const PACK = 'c:/Users/rydux/OneDrive/Desktop/provix/IconPacks/Midnight';
const manifest = JSON.parse(fs.readFileSync(path.join(PACK, 'iconpack.json'), 'utf8'));

const refs = [manifest.folder, manifest.file, manifest.drive, ...Object.values(manifest.extensions)];

(async () => {
  const out = [];
  let allGood = true;
  for (const ref of refs) {
    const p = path.join(PACK, ref);
    if (!fs.existsSync(p)) { out.push(`MISSING ${ref}`); allGood = false; continue; }
    const size = fs.statSync(p).size;
    const meta = await sharp(p).metadata();
    const stats = await sharp(p).stats();
    const hasAlpha = meta.hasAlpha === true;
    // check transparency: min alpha should be 0 (transparent pixels exist)
    const alphaCh = stats.channels[stats.channels.length - 1];
    const transparent = hasAlpha && alphaCh.min === 0;
    const ok = size > 0 && transparent && meta.width === 256 && meta.height === 256;
    if (!ok) allGood = false;
    out.push(`${ok ? 'OK ' : 'BAD'} ${ref}  ${meta.width}x${meta.height} ${size}B alpha=${hasAlpha} minAlpha=${alphaCh.min}`);
  }
  out.push(allGood ? 'ALL_VALID' : 'FAILURES_PRESENT');
  fs.writeFileSync(path.join(__dirname, 'verify-result.txt'), out.join('\n'), 'utf8');
})().catch(e => {
  fs.writeFileSync(path.join(__dirname, 'verify-result.txt'), 'ERROR ' + e.stack, 'utf8');
  process.exit(1);
});
