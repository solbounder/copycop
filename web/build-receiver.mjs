// Rebuild the standalone receiver after editing copycop.source.html.
// Only Node.js built-ins are used; the generated HTML needs no server or network.
import { readFile, writeFile } from 'node:fs/promises';
import { gzipSync } from 'node:zlib';

const sourceUrl = new URL('./copycop.source.html', import.meta.url);
const outputUrl = new URL('./copycop.html', import.meta.url);
const source = (await readFile(sourceUrl, 'utf8')).replace(/\r\n?/g, '\n');
const compressed = gzipSync(Buffer.from(source), { level: 9 });
compressed[9] = 255; // Platform-neutral gzip OS byte for reproducible builds.
const payload = compressed.toString('base64');
const html = `<!doctype html><html lang="de"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'"><title>CopyCop</title><p id="copycop-start">CopyCop wird geoeffnet...</p><noscript>Bitte JavaScript aktivieren.</noscript><script id="copycop-payload" type="application/octet-stream">${payload}</script><script>(async()=>{try{const bytes=Uint8Array.from(atob(document.getElementById("copycop-payload").textContent),c=>c.charCodeAt(0)),html=await new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"))).text();document.open();document.write(html);document.close();}catch(error){document.getElementById("copycop-start").textContent="CopyCop konnte nicht geoeffnet werden. Bitte einen aktuellen Browser verwenden oder die HTML-Datei erneut uebertragen. "+error.message;}})();</script></html>\n`;
if (/[^\x09\x0a\x0d\x20-\x7e]/.test(html) || html.includes(String.fromCharCode(94)))
  throw new Error('Generated HTML contains a character CopyCop cannot type.');
if (Buffer.byteLength(html) > 126464)
  throw new Error('Generated HTML no longer fits in one CopyCop transfer.');
await writeFile(outputUrl, html, 'utf8');
console.log(`Receiver: ${source.length} -> ${html.length} typing characters (${(100 - html.length / source.length * 100).toFixed(1)}% fewer).`);
