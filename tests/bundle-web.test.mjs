// Run after building the CLI: node tests/bundle-web.test.mjs [scratch-directory]
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, mkdtemp } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { dirname, resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { gunzipSync } from 'node:zlib';
import { zipFixture, envelopeFixture } from './zip-fixture.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const scratch = resolve(process.argv[2] || join(root, 'build', 'bundle-tests'));
await mkdir(scratch, { recursive: true });
const work = await mkdtemp(join(scratch, 'run-'));
const html = await readFile(join(root, 'web', 'copycop.html'), 'utf8');
assert.ok(!/[^\x09\x0a\x0d\x20-\x7e]/.test(html), 'receiver source uses only keyboard-safe ASCII');
const payload = html.match(/<script id="copycop-payload" type="application\/octet-stream">([A-Za-z0-9+/=]+)<\/script>/)?.[1];
assert.ok(payload, 'standalone receiver contains its compressed payload');
const source = (await readFile(join(root, 'web', 'copycop.source.html'), 'utf8')).replace(/\r\n?/g, '\n');
const restoredHtml = gunzipSync(Buffer.from(payload, 'base64')).toString('utf8');
assert.equal(restoredHtml, source, 'published HTML contains the complete current receiver source');
assert.ok(html.length < source.length * 0.7, 'compressed receiver saves at least 30% typing');
vm.runInThisContext(restoredHtml.match(/<script id="copycop-codec">([\s\S]*?)<\/script>/)[1]);
const codec = globalThis.CopyCopCodec;
const cli = join(root, 'host', 'copycop-cli', 'bin', 'Release', 'net8.0', 'copycop-cli.dll');
function command(args, success = true) {
  const result = spawnSync('dotnet', [cli, ...args], { encoding: 'utf8', windowsHide: true });
  assert.equal(result.status === 0, success, result.stdout + result.stderr);
  return result;
}
const samples = [
  { name: 'Grüße.txt', data: new TextEncoder().encode('\uFEFFä 😀 你好\r\n\tHello\r\n'.repeat(500)) },
  { name: 'binary.dat', data: Uint8Array.from({ length: 256 }, (_, i) => i) },
  { name: 'empty.txt', data: new Uint8Array() },
];
for (const sample of samples) await writeFile(join(work, sample.name), sample.data);
assert.equal(codec.pack, undefined, 'receiver has no bundle creation API');
assert.equal(codec.frame, undefined, 'receiver has no sending API');
assert.ok(!restoredHtml.includes('id="source-files"') && !restoredHtml.includes('id="pack"'), 'HTML contains only receiving controls');
for (const compress of [true, false]) {
  const name = compress ? 'compressed' : 'stored';
  const file = join(work, `${name}-cli.copycop`);
  command(['pack', file, ...samples.map(sample => join(work, sample.name)), ...(compress ? [] : ['--no-compress'])]);
  const restored = await codec.unzip(await codec.decode(await readFile(file, 'utf8')));
  assert.deepEqual(restored, samples, 'native ZIP decodes byte-exactly in receiver');
  const output = join(work, `${name}-cli-out`);
  command(['unpack', file, output]);
  for (const sample of samples) assert.deepEqual(new Uint8Array(await readFile(join(output, sample.name))), sample.data);
  command(['unpack', file, output], false);
  command(['pack', file, join(work, samples[0].name)], false);
}
const nestedArchive = zipFixture('src/unterordner/hello.txt', samples[0].data);
const nested = { text: envelopeFixture(nestedArchive) };
assert.deepEqual((await codec.unzip(await codec.decode(nested.text)))[0].data, samples[0].data);
await writeFile(join(work, 'random.dat'), randomBytes(300000));
command(['pack', join(work, 'large.copycop'), join(work, 'random.dat')]);
const bigText = await readFile(join(work, 'large.copycop'), 'utf8');
const big = { text: bigText, parts: bigText.match(/COPYCOP\/1 [\s\S]*?ENDCOPYCOP\n/g), archive: await codec.decode(bigText) };
assert.ok(big.parts.length > 1);
assert.ok(big.parts.every(part => part.length <= 126464 && part.endsWith('ENDCOPYCOP\n')));
assert.deepEqual(await codec.decode(big.parts.toReversed().join('') + big.parts[0]), big.archive);
await assert.rejects(() => codec.decode(big.parts[0]), /unvollständig/);
assert.equal(codec.collect(big.parts[0]).missing.length, big.parts.length - 1);
const corrupt = big.parts[0].replace(/\n[A-Za-z0-9+/]/, value => '\n' + (value[1] === 'A' ? 'B' : 'A'));
await assert.rejects(() => codec.decode(corrupt + big.parts.slice(1).join('')), /Prüfsumme/);
await assert.rejects(() => codec.decode(big.text + corrupt), /unterschiedlichem Inhalt/);
await assert.rejects(() => codec.decode(big.text + nested.text), /unterschiedlichen Paketen/);
await assert.rejects(() => codec.decode(big.text + 'garbage'), /ungültiger Teil/);
await assert.rejects(() => codec.decode(big.text.replaceAll('COPYCOP/1', 'COPYCOP/2')), /Version 1/);
await assert.rejects(() => codec.decode('x'.repeat(codec.MAX_TEXT + 1)), /größer/);
for (const name of ['../escape', '/root', 'a\\b', 'C:/drive', 'CON.txt', 'COM\u00b9.txt', 'LPT\u00b2.log', 'COM\u00b3', 'a/../b', 'dir/', 'nul']) {
  const archive = zipFixture(name, samples[0].data);
  await assert.rejects(() => codec.unzip(archive), /Datei/);
}
const unsafe = { archive: zipFixture('aa/file', samples[0].data) };
const truncated = unsafe.archive.slice(0, -1);
await assert.rejects(() => codec.unzip(truncated), /ZIP/);
const tooBig = unsafe.archive.slice();
const central = tooBig.findIndex((value, i) => value === 80 && tooBig[i+1] === 75 && tooBig[i+2] === 1 && tooBig[i+3] === 2);
new DataView(tooBig.buffer).setUint32(central + 24, codec.MAX_RAW + 1, true);
await assert.rejects(() => codec.unzip(tooBig), /16 MiB/);
const badCrc = unsafe.archive.slice();
const localLength = 30 + new DataView(badCrc.buffer).getUint16(26, true);
badCrc[localLength] ^= 1;
await assert.rejects(() => codec.unzip(badCrc), /beschädigt/);
await writeFile(join(work, 'crc-invalid.copycop'), envelopeFixture(badCrc));
command(['unpack', join(work, 'crc-invalid.copycop'), join(work, 'crc-out')], false);
// Announced part length, live progress for the mask and plain-text detection.
const firstHeader = big.parts[0].slice(0, big.parts[0].indexOf('\n')).split(' ');
const firstBody = big.parts[0].slice(big.parts[0].indexOf('\n') + 1, -'ENDCOPYCOP\n'.length).replace(/\n/g, '');
assert.equal(Number(firstHeader[3]), firstBody.length, 'sender announces the Base64 length of each part');
const legacyText = big.text.replace(/^(COPYCOP\/1 \S+ \d+\/\d+) \d+\n/gm, '$1\n');
assert.deepEqual(await codec.decode(legacyText), big.archive, 'frames without length remain readable');
const wrongLength = big.parts[0].replace(/^(COPYCOP\/1 \S+ \d+\/\d+) (\d+)/, (_, head, size) => `${head} ${Number(size) + 4}`);
await assert.rejects(() => codec.decode(wrongLength + big.parts.slice(1).join('')), /unvollständig oder zu lang/);
assert.deepEqual(codec.progress('  \n'), { kind: 'empty' });
assert.deepEqual(codec.progress('Hallo Welt'), { kind: 'text', chars: 10 });
assert.equal(codec.progress('COP').kind, 'package', 'a package header is recognised while it is typed');
const half = big.parts[0].slice(0, Math.floor(big.parts[0].length / 2));
const halfProgress = codec.progress(half);
assert.equal(halfProgress.current.number, 1);
assert.ok(halfProgress.fraction > 0.4 && halfProgress.fraction < 0.6, 'part progress follows typed Base64');
const afterFirst = codec.progress(big.parts[0]);
assert.equal(afterFirst.done, 1);
assert.equal(afterFirst.fraction, 1);
assert.ok(!afterFirst.complete && afterFirst.missing.length === big.parts.length - 1);
assert.ok(codec.progress(big.text).complete, 'all parts typed completes the package');
assert.equal(codec.progress(legacyText.slice(0, 5000)).fraction, null, 'legacy frames show indeterminate progress');
assert.match(codec.progress('COPYCOP/2 x\nabc').error, /Paketkopf/);
assert.equal(codec.asText(new TextEncoder().encode('﻿äö')), 'äö', 'text view strips BOM');
assert.equal(codec.asText(samples[1].data), null, 'binary data is not shown as text');
assert.equal(codec.imageType('bild.PNG'), 'image/png');
assert.equal(codec.imageType('daten.bin'), null);
assert.ok(restoredHtml.includes('id="mask"') && restoredHtml.includes('id="mask-arm"'), 'receiver offers the mask');
console.log('Receiver-only HTML, native interoperability, multipart, corruption, paths and limits: all passed.');
console.log(`Fixtures: ${work}`);
