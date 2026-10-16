/**
 * 将本项目原生 MeloTTS 引擎生成的 5 大类离线 WAV 音频
 * 高保真压缩转码为轻量 MP3 (96kbps)，并输出到 audio/preset/
 */

const fs = require('fs');
const path = require('path');

const lameDir = path.resolve('node_modules/lamejs/src/js');
fs.readdirSync(lameDir).forEach((file) => {
  if (file.endsWith('.js') && file !== 'index.js') {
    const name = file.replace('.js', '');
    try {
      global[name] = require(path.join(lameDir, file));
    } catch {
      // ignore
    }
  }
});
const lamejs = require('lamejs');

const SRC_DIR = path.resolve('audio/preset_minimal_wav');
const OUT_DIR = path.resolve('audio/preset');

if (!fs.existsSync(OUT_DIR)) {
  fs.mkdirSync(OUT_DIR, { recursive: true });
}

function parseWav(buffer) {
  let offset = 12;
  let fmt = null;
  let dataOffset = 0;
  let dataSize = 0;

  while (offset < buffer.length) {
    const chunkId = buffer.toString('ascii', offset, offset + 4);
    const chunkSize = buffer.readUInt32LE(offset + 4);
    offset += 8;

    if (chunkId === 'fmt ') {
      fmt = {
        audioFormat: buffer.readUInt16LE(offset),
        numChannels: buffer.readUInt16LE(offset + 2),
        sampleRate: buffer.readUInt32LE(offset + 4),
        byteRate: buffer.readUInt32LE(offset + 8),
        blockAlign: buffer.readUInt16LE(offset + 12),
        bitsPerSample: buffer.readUInt16LE(offset + 14),
      };
    } else if (chunkId === 'data') {
      dataOffset = offset;
      dataSize = chunkSize;
      break;
    }
    offset += chunkSize;
  }

  if (!fmt || !dataOffset) {
    throw new Error('Invalid WAV: fmt or data missing');
  }

  return { fmt, dataOffset, dataSize };
}

function convertWavToMp3(wavBuffer, kbps = 96) {
  const { fmt, dataOffset, dataSize } = parseWav(wavBuffer);
  const pcmData = wavBuffer.subarray(dataOffset, dataOffset + dataSize);
  const samples = new Int16Array(pcmData.buffer, pcmData.byteOffset, pcmData.byteLength / 2);

  const encoder = new lamejs.Mp3Encoder(fmt.numChannels, fmt.sampleRate, kbps);
  const mp3Chunks = [];
  const blockSize = 1152;

  if (fmt.numChannels === 1) {
    for (let i = 0; i < samples.length; i += blockSize) {
      const chunk = samples.subarray(i, i + blockSize);
      const mp3buf = encoder.encodeBuffer(chunk);
      if (mp3buf.length > 0) mp3Chunks.push(Buffer.from(mp3buf));
    }
  } else {
    const left = new Int16Array(samples.length / 2);
    const right = new Int16Array(samples.length / 2);
    for (let i = 0, j = 0; i < samples.length; i += 2, j++) {
      left[j] = samples[i];
      right[j] = samples[i + 1];
    }
    for (let i = 0; i < left.length; i += blockSize) {
      const lChunk = left.subarray(i, i + blockSize);
      const rChunk = right.subarray(i, i + blockSize);
      const mp3buf = encoder.encodeBuffer(lChunk, rChunk);
      if (mp3buf.length > 0) mp3Chunks.push(Buffer.from(mp3buf));
    }
  }

  const flushBuf = encoder.flush();
  if (flushBuf.length > 0) mp3Chunks.push(Buffer.from(flushBuf));

  return Buffer.concat(mp3Chunks);
}

function run() {
  const wavFiles = fs.readdirSync(SRC_DIR).filter((f) => f.endsWith('.wav'));
  console.log(`[Project Voice Compress] 正在压缩本项目音色的离线语音 (共 ${wavFiles.length} 个)...`);

  let totalRaw = 0;
  let totalMp3 = 0;
  const manifest = [];

  for (const wavFile of wavFiles) {
    const rawPath = path.join(SRC_DIR, wavFile);
    const wavBuf = fs.readFileSync(rawPath);
    totalRaw += wavBuf.length;

    const mp3Buf = convertWavToMp3(wavBuf, 96);
    totalMp3 += mp3Buf.length;

    const mp3Name = wavFile.replace(/\.wav$/i, '.mp3');
    const outPath = path.join(OUT_DIR, mp3Name);
    fs.writeFileSync(outPath, mp3Buf);

    manifest.push({
      key: mp3Name.replace(/\.mp3$/, ''),
      file: mp3Name,
      rawBytes: wavBuf.length,
      compressedBytes: mp3Buf.length,
    });
  }

  fs.writeFileSync(
    path.join(OUT_DIR, 'manifest.json'),
    JSON.stringify(manifest, null, 2),
    'utf-8'
  );

  console.log(`\n======================================================`);
  console.log(`[项目原生音色离线音频压缩汇报]`);
  console.log(`转码文件总数: ${wavFiles.length}`);
  console.log(`原始 WAV 总大小: ${(totalRaw / 1024 / 1024).toFixed(2)} MB`);
  console.log(`压缩 MP3 总大小: ${(totalMp3 / 1024 / 1024).toFixed(2)} MB`);
  console.log(`压缩瘦身比: -${((1 - totalMp3 / totalRaw) * 100).toFixed(1)}%`);
  console.log(`存储路径: ${OUT_DIR}`);
  console.log(`======================================================\n`);
}

run();
