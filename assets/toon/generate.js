// サンプルの toon テクスチャ (影の濃さ) を生成し、配布用 Config の Toon フォルダへ書き出す。
//
//   node generate.js
//
// ゲームのテクスチャは再配布しないため、ここで数式から作る。
// 形はゲームの _ShadowRateToon (左 = 暗部が黒、右 = 明部が白、ToonSkin_Shadow は u≈0.33〜0.5 で立ち上がる) に合わせる。
// PNG のエンコードは icons/generate.js と同じく Node 標準の zlib で組み立てる
// (ライブラリが吐く PNG は Unity の Texture2D.LoadImage が読めないことがあるため)。

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const WIDTH = 256;
const HEIGHT = 16;
const OUT_DIR = path.join(__dirname, '../../UnityInjector/Config/SceneEditor/Toon');

// dark: 暗部の値 (0〜255)、from / to: 暗部から白へ立ち上がる u の範囲
const SAMPLES = [
    { name: '0_影なし', dark: 255, from: 0.33, to: 0.47 },
    { name: '1_影薄め', dark: 160, from: 0.33, to: 0.47 },
    { name: '2_影標準', dark: 0, from: 0.33, to: 0.47 },
    { name: '3_影濃いめ', dark: 0, from: 0.48, to: 0.62 },
];

const CRC_TABLE = (() => {
    const table = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) {
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        table[n] = c >>> 0;
    }
    return table;
})();

function crc32(buffer) {
    let crc = 0xffffffff;
    for (const byte of buffer) {
        crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
    }
    return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
    const head = Buffer.alloc(8);
    head.writeUInt32BE(data.length, 0);
    head.write(type, 4, 'ascii');
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])), 0);
    return Buffer.concat([head, data, crc]);
}

function encodePng(rgba, width, height) {
    const ihdr = Buffer.alloc(13);
    ihdr.writeUInt32BE(width, 0);
    ihdr.writeUInt32BE(height, 4);
    ihdr[8] = 8; // ビット深度
    ihdr[9] = 6; // RGBA
    const raw = Buffer.alloc((width * 4 + 1) * height);
    for (let y = 0; y < height; y++) {
        raw[y * (width * 4 + 1)] = 0; // フィルタなし
        rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
    }
    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', ihdr),
        chunk('IDAT', zlib.deflateSync(raw)),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

function smoothstep(from, to, x) {
    const t = Math.min(1, Math.max(0, (x - from) / (to - from)));
    return t * t * (3 - 2 * t);
}

function render(sample) {
    const rgba = Buffer.alloc(WIDTH * HEIGHT * 4);
    for (let x = 0; x < WIDTH; x++) {
        const u = x / (WIDTH - 1);
        const value = Math.round(sample.dark + (255 - sample.dark) * smoothstep(sample.from, sample.to, u));
        for (let y = 0; y < HEIGHT; y++) {
            const i = (y * WIDTH + x) * 4;
            rgba[i] = rgba[i + 1] = rgba[i + 2] = value;
            rgba[i + 3] = 255;
        }
    }
    return encodePng(rgba, WIDTH, HEIGHT);
}

fs.mkdirSync(OUT_DIR, { recursive: true });
for (const sample of SAMPLES) {
    const file = path.join(OUT_DIR, `${sample.name}.png`);
    fs.writeFileSync(file, render(sample));
    console.log(`生成しました: ${file}`);
}
