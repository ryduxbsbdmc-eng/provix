const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const PACK = 'c:/Users/rydux/OneDrive/Desktop/provix/IconPacks/Midnight';
const SRC = path.join(PACK, 'src');

fs.mkdirSync(SRC, { recursive: true });

// ----- shared svg pieces ---------------------------------------------------
function open() {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">`;
}

// soft volumetric drop shadow (keeps background transparent)
const dropShadow = `
  <filter id="ds" x="-30%" y="-30%" width="160%" height="170%">
    <feDropShadow dx="0" dy="7" stdDeviation="9" flood-color="#0b1830" flood-opacity="0.35"/>
  </filter>`;

// glossy white sheen gradient (top highlight)
const glossGrad = `
  <linearGradient id="gloss" x1="0" y1="0" x2="0" y2="1">
    <stop offset="0%"  stop-color="#ffffff" stop-opacity="0.65"/>
    <stop offset="100%" stop-color="#ffffff" stop-opacity="0.04"/>
  </linearGradient>`;

function vgrad(id, stops) {
  return `<linearGradient id="${id}" x1="0" y1="0" x2="0" y2="1">${stops}</linearGradient>`;
}

// ----- FOLDER --------------------------------------------------------------
function folderSvg(back1, back2, front1, front2, front3) {
  return `${open()}
  <defs>
    ${dropShadow}
    ${glossGrad}
    ${vgrad('fbk', `<stop offset="0%" stop-color="${back1}"/><stop offset="100%" stop-color="${back2}"/>`)}
    ${vgrad('ffr', `<stop offset="0%" stop-color="${front1}"/><stop offset="55%" stop-color="${front2}"/><stop offset="100%" stop-color="${front3}"/>`)}
  </defs>
  <g filter="url(#ds)">
    <!-- back panel + tab -->
    <path d="M46 84 a16 16 0 0 1 16 -16 H100 a8 8 0 0 1 6 2 l14 14 H194 a16 16 0 0 1 16 16 V176 a16 16 0 0 1 -16 16 H62 a16 16 0 0 1 -16 -16 Z" fill="url(#fbk)"/>
    <!-- front pocket -->
    <rect x="42" y="100" width="172" height="98" rx="17" fill="url(#ffr)"/>
    <!-- glossy sheen on front -->
    <path d="M42 132 V117 a17 17 0 0 1 17 -17 H197 a17 17 0 0 1 17 17 V132 Q128 160 42 132 Z" fill="url(#gloss)"/>
    <!-- thin rim highlight -->
    <rect x="43.5" y="101.5" width="169" height="95" rx="15.5" fill="none" stroke="#ffffff" stroke-opacity="0.45" stroke-width="1.5"/>
  </g>
</svg>`;
}

// ----- DRIVE ---------------------------------------------------------------
function driveSvg() {
  return `${open()}
  <defs>
    ${dropShadow}
    ${glossGrad}
    ${vgrad('dbody', `<stop offset="0%" stop-color="#7d8ba3"/><stop offset="50%" stop-color="#566174"/><stop offset="100%" stop-color="#39424f"/>`)}
    ${vgrad('dled', `<stop offset="0%" stop-color="#8be0ff"/><stop offset="100%" stop-color="#2f8fd6"/>`)}
  </defs>
  <g filter="url(#ds)">
    <rect x="46" y="82" width="164" height="92" rx="20" fill="url(#dbody)"/>
    <!-- top sheen -->
    <path d="M46 116 V102 a20 20 0 0 1 20 -20 H190 a20 20 0 0 1 20 20 V116 Q128 142 46 116 Z" fill="url(#gloss)"/>
    <!-- engraved slot line -->
    <rect x="70" y="150" width="116" height="6" rx="3" fill="#222a33" fill-opacity="0.55"/>
    <!-- activity LED -->
    <circle cx="180" cy="124" r="9" fill="url(#dled)"/>
    <circle cx="177" cy="121" r="3" fill="#ffffff" fill-opacity="0.8"/>
    <!-- rim highlight -->
    <rect x="47.5" y="83.5" width="161" height="89" rx="18.5" fill="none" stroke="#ffffff" stroke-opacity="0.35" stroke-width="1.5"/>
  </g>
</svg>`;
}

// ----- DOCUMENT (base + extension) -----------------------------------------
// page path, fold flap, sheen reused. Optional colored banner with label.
function docSvg(opts) {
  opts = opts || {};
  const accent1 = opts.accent1;
  const accent2 = opts.accent2;
  const label = opts.label;
  const textColor = opts.textColor || '#ffffff';
  const fontSize = opts.fontSize || 30;

  const page = `M82 46 H156 L188 78 V198 a14 14 0 0 1 -14 14 H82 a14 14 0 0 1 -14 -14 V60 a14 14 0 0 1 14 -14 Z`;
  const sheen = `M68 96 V60 a14 14 0 0 1 14 -14 H156 L188 78 V96 Q128 118 68 96 Z`;

  let middle = '';
  if (label) {
    const fs2 = fontSize;
    middle = `
    <rect x="68" y="148" width="120" height="44" fill="url(#acc)"/>
    <rect x="68" y="148" width="120" height="20" fill="url(#gloss)"/>
    <text x="128" y="172" text-anchor="middle" dominant-baseline="central"
          font-family="Segoe UI, Arial, sans-serif" font-weight="800"
          font-size="${fs2}" letter-spacing="0.5" fill="${textColor}">${label}</text>`;
  } else {
    // plain document: faint content lines
    middle = `
    <rect x="86" y="118" width="84" height="8" rx="4" fill="#c3ccdd"/>
    <rect x="86" y="140" width="84" height="8" rx="4" fill="#c3ccdd"/>
    <rect x="86" y="162" width="60" height="8" rx="4" fill="#c3ccdd"/>`;
  }

  const accDef = label
    ? vgrad('acc', `<stop offset="0%" stop-color="${accent1}"/><stop offset="100%" stop-color="${accent2}"/>`)
    : '';

  return `${open()}
  <defs>
    ${dropShadow}
    ${glossGrad}
    ${vgrad('paper', `<stop offset="0%" stop-color="#ffffff"/><stop offset="100%" stop-color="#e7edf7"/>`)}
    ${accDef}
  </defs>
  <g filter="url(#ds)">
    <path d="${page}" fill="url(#paper)"/>
    <!-- folded corner -->
    <path d="M156 46 L188 78 H160 a4 4 0 0 1 -4 -4 Z" fill="#c4cedf"/>
    ${middle}
    <!-- top sheen -->
    <path d="${sheen}" fill="url(#gloss)"/>
    <!-- rim highlight -->
    <path d="${page}" fill="none" stroke="#ffffff" stroke-opacity="0.5" stroke-width="1.5"/>
  </g>
</svg>`;
}

// ----- icon set ------------------------------------------------------------
const docs = {
  txt:  { accent1: '#aab6c6', accent2: '#6c7888', label: 'TXT', fontSize: 28 },
  md:   { accent1: '#6f9bff', accent2: '#3a5fd0', label: 'MD',  fontSize: 30 },
  pdf:  { accent1: '#ff7a72', accent2: '#d62f2a', label: 'PDF', fontSize: 27 },
  zip:  { accent1: '#ffcf63', accent2: '#f59e0b', label: 'ZIP', fontSize: 27, textColor: '#5a3d05' },
  '7z': { accent1: '#ffb35a', accent2: '#ef7e00', label: '7Z',  fontSize: 30, textColor: '#5a3300' },
  rar:  { accent1: '#bb98ff', accent2: '#7c4ddb', label: 'RAR', fontSize: 26 },
  cs:   { accent1: '#a98bff', accent2: '#6b3fd6', label: 'C#',  fontSize: 30 },
  js:   { accent1: '#ffdf5c', accent2: '#f1c40f', label: 'JS',  fontSize: 30, textColor: '#4a3a02' },
  ts:   { accent1: '#5ab6ff', accent2: '#2f74e0', label: 'TS',  fontSize: 30 },
  json: { accent1: '#ffd35c', accent2: '#e0a400', label: 'JSON',fontSize: 21, textColor: '#5a4202' },
  html: { accent1: '#ff9a5c', accent2: '#e6601f', label: 'HTML',fontSize: 20 },
  css:  { accent1: '#5cb6ff', accent2: '#2f74e0', label: 'CSS', fontSize: 27 },
  png:  { accent1: '#69d489', accent2: '#2fae54', label: 'PNG', fontSize: 27 },
  jpg:  { accent1: '#5ad4b6', accent2: '#2fae8a', label: 'JPG', fontSize: 27 },
  exe:  { accent1: '#94a6bd', accent2: '#566373', label: 'EXE', fontSize: 27 },
};

const icons = {};
// folder: vibrant glassy blue
icons['folder'] = folderSvg('#3f7fd8', '#2f63c4', '#79c2ff', '#3f93f0', '#2c74dc');
icons['drive']  = driveSvg();
icons['file']   = docSvg({}); // plain document
for (const [k, v] of Object.entries(docs)) icons[k] = docSvg(v);

// ----- render --------------------------------------------------------------
(async () => {
  const names = Object.keys(icons);
  for (const name of names) {
    const svg = icons[name];
    fs.writeFileSync(path.join(SRC, `${name}.svg`), svg, 'utf8');
    await sharp(Buffer.from(svg))
      .resize(256, 256, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png()
      .toFile(path.join(PACK, `${name}.png`));
  }
  fs.writeFileSync(path.join(__dirname, 'render-result.txt'),
    `RENDERED ${names.length}: ${names.join(', ')}`, 'utf8');
})().catch(e => {
  fs.writeFileSync(path.join(__dirname, 'render-result.txt'), 'ERROR ' + e.stack, 'utf8');
  process.exit(1);
});
