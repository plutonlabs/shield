// A tiny origin for adult.sh: answers every request with the host, the address it was asked for (as the proxy sent it on)
// and the YouTube-Restrict header, so the test can see what the proxy changed.
const https = require('https'), http = require('http'), fs = require('fs'), tls = require('tls');
const dir = process.env.E2E_CERTS + '/';
const hosts = ['adult.test', 'www.google.com', 'www.bing.com', 'duckduckgo.com', 'www.youtube.com', 'ok.test'];
const ctxs = {};
for (const h of hosts) ctxs[h] = tls.createSecureContext({ key: fs.readFileSync(dir + h + '.key'), cert: fs.readFileSync(dir + h + '.pem') });
function handler(req, res) {
  res.writeHead(200, { 'content-type': 'application/json' });
  res.end(JSON.stringify({ host: (req.headers.host || '').split(':')[0], url: req.url, restrict: req.headers['youtube-restrict'] || null }));
}
https.createServer({ SNICallback: (n, cb) => cb(null, ctxs[n] || ctxs['ok.test']), key: fs.readFileSync(dir + 'ok.test.key'), cert: fs.readFileSync(dir + 'ok.test.pem') }, handler).listen(8443, '127.0.0.1');
http.createServer(handler).listen(8080, '127.0.0.1');
