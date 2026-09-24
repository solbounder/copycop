// Run after building the CLI: node tests/bundle-web.test.mjs [scratch-directory]
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, mkdtemp } from 'node:fs/promises';
import { randomBytes } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { dirname, resolve, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import { zipFixture, envelopeFixture } from './zip-fixture.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const scratch = resolve(process.argv[2] || join(root, 'build', 'bundle-tests'));
await mkdir(scratch, { recursive: true });
const work = await mkdtemp(join(scratch, 'run-'));
const html = await readFile(join(root, 'web', 'copycop.html'), 'utf8');
vm.runInThisContext(html.match(/<script id="copycop-codec">([\s\S]*?)<\/script>/)[1]);
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
assert.ok(!html.includes('id="source-files"') && !html.includes('id="pack"'), 'HTML contains only receiving controls');
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
for (const name of ['../escape', '/root', 'a\\b', 'C:/drive', 'CON.txt', 'a/../b', 'dir/', 'nul']) {
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
console.log('Receiver-only HTML, native interoperability, multipart, corruption, paths and limits: all passed.');
console.log(`Fixtures: ${work}`);
