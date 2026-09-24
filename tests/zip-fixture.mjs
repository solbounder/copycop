// Independent stored-ZIP fixtures for receiver error tests; not shipped in the HTML.
import { createHash } from 'node:crypto';

function crc32(data) {
  let crc = 0xffffffff;
  for (const byte of data) { crc ^= byte; for (let bit=0;bit<8;bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
  return (crc ^ 0xffffffff) >>> 0;
}

export function zipFixture(name, data) {
  const filename = Buffer.from(name), body = Buffer.from(data), crc = crc32(body);
  const header = Buffer.alloc(30 + filename.length);
  header.writeUInt32LE(0x04034b50); header.writeUInt16LE(20,4); header.writeUInt16LE(2048,6);
  header.writeUInt32LE(crc,14); header.writeUInt32LE(body.length,18); header.writeUInt32LE(body.length,22);
  header.writeUInt16LE(filename.length,26); filename.copy(header,30);
  const central = Buffer.alloc(46 + filename.length);
  central.writeUInt32LE(0x02014b50); central.writeUInt16LE(20,4); central.writeUInt16LE(20,6);
  central.writeUInt16LE(2048,8); central.writeUInt32LE(crc,16); central.writeUInt32LE(body.length,20);
  central.writeUInt32LE(body.length,24); central.writeUInt16LE(filename.length,28); filename.copy(central,46);
  const end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50); end.writeUInt16LE(1,8); end.writeUInt16LE(1,10);
  end.writeUInt32LE(central.length,12); end.writeUInt32LE(header.length + body.length,16);
  return new Uint8Array(Buffer.concat([header,body,central,end]));
}

export function envelopeFixture(archive) {
  const hash = createHash('sha256').update(archive).digest('hex');
  return `COPYCOP/1 ${hash} 1/1\n${Buffer.from(archive).toString('base64')}\nENDCOPYCOP\n`;
}
