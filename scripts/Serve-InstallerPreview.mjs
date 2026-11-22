// Loopback-only, read-only preview server. No installer or app commands exist here.
import http from 'node:http';
import { readFile, realpath } from 'node:fs/promises';
import { dirname, extname, isAbsolute, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = await realpath(resolve(dirname(fileURLToPath(import.meta.url)), '../artifacts/installer-preview'));
const port = Number(process.argv[2] || 8173);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Use a port from 1024 to 65535.');
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.png': 'image/png', '.otf': 'font/otf' };
const server = http.createServer(async (request, response) => {
  const finish = (code, body) => { response.writeHead(code, { 'Content-Type': 'text/plain; charset=utf-8' }); response.end(body); };
  if (!['GET', 'HEAD'].includes(request.method)) return finish(405, 'Read-only preview.');
  try {
    const pathname = decodeURIComponent(new URL(request.url, `http://127.0.0.1:${port}`).pathname);
    if (pathname.includes('\\') || pathname.includes('\0')) return finish(400, 'Invalid path.');
    const target = await realpath(resolve(root, `.${pathname === '/' ? '/index.html' : pathname}`));
    const inside = relative(root, target);
    if (isAbsolute(inside) || inside === '..' || inside.startsWith(`..${sep}`)) return finish(403, 'Outside preview.');
    const type = types[extname(target)];
    if (!type) return finish(404, 'Not found.');
    const body = await readFile(target);
    response.writeHead(200, { 'Content-Type': type, 'Content-Length': body.length, 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
    response.end(request.method === 'HEAD' ? undefined : body);
  } catch { finish(404, 'Not found.'); }
});
server.listen(port, '127.0.0.1', () => console.log(`Installer UI preview: http://127.0.0.1:${port}\nAll installation actions are simulated. Ctrl+C stops this preview server.`));
