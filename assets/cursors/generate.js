// resize-arrow.svg を 4 方向に回転させて 32x32 の PNG へラスタライズし、
// ResizeCursor.cs へ貼り付ける base64 文字列を出力する。
//
//   npm install
//   node generate.js
//
// 回転はラスタ画像ではなく SVG の transform で行うため、45 度でも劣化しない。
//
// PNG のエンコードはラスタライザ任せにせず自前で行う。ライブラリが吐く PNG は
// Unity の Texture2D.LoadImage が読めず (8x8 のエラーテクスチャに差し替わる)、
// 付加チャンクを削っても直らなかったため、Node 標準の zlib で素直に組み立てている。

const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { Resvg } = require('@resvg/resvg-js');

const SIZE = 32;
const SOURCE = path.join(__dirname, 'resize-arrow.svg');

// SVG は Y 軸が下向きなので、画面上で右下を向かせる角度がプラスになる
const VARIANTS = [
    { name: 'Horizontal', angle: 0 },
    { name: 'Vertical', angle: 90 },
    { name: 'DiagonalDown', angle: 45 },
    { name: 'DiagonalUp', angle: -45 },
];

function rotatedSvg(source, angle) {
    const inner = source.replace(/^[\s\S]*?<svg[^>]*>/, '').replace(/<\/svg>[\s\S]*$/, '');
    const center = SIZE / 2;
    return `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}">`
        + `<g transform="rotate(${angle} ${center} ${center})">${inner}</g></svg>`;
}

const CRC_TABLE = (() => {
    const table = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
        let c = n;
        for (let k = 0; k < 8; k++) {
            c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
        }
        table[n] = c;
    }
    return table;
})();

function crc32(buffer) {
    let c = -1;
    for (let i = 0; i < buffer.length; i++) {
        c = CRC_TABLE[(c ^ buffer[i]) & 0xff] ^ (c >>> 8);
    }
    return (c ^ -1) >>> 0;
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
    ihdr[8] = 8;  // ビット深度
    ihdr[9] = 6;  // カラータイプ: RGBA
    // 圧縮方式・フィルタ方式・インタレースはすべて既定 (0)

    // 各走査線の先頭にフィルタタイプ 0 (フィルタなし) を付ける
    const stride = width * 4;
    const raw = Buffer.alloc((stride + 1) * height);
    for (let y = 0; y < height; y++) {
        raw[y * (stride + 1)] = 0;
        rgba.copy(raw, y * (stride + 1) + 1, y * stride, (y + 1) * stride);
    }

    return Buffer.concat([
        Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
        chunk('IHDR', ihdr),
        chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
        chunk('IEND', Buffer.alloc(0)),
    ]);
}

const source = fs.readFileSync(SOURCE, 'utf8');

for (const variant of VARIANTS) {
    const svg = rotatedSvg(source, variant.angle);
    const image = new Resvg(svg).render();
    const png = encodePng(image.pixels, image.width, image.height);
    fs.writeFileSync(path.join(__dirname, `resize-${variant.name}.png`), png);
    console.log(`// ${variant.name} (${variant.angle} deg, ${png.length} bytes)`);
    console.log(`"${png.toString('base64')}"`);
    console.log();
}
