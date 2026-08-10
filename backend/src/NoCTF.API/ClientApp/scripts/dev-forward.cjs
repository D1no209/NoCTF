// Local dev forwarder: 127.0.0.1:5080 -> https://noctf.fa1lsnow.com
// Lets the local Nuxt dev server render pages with real public data.
const http = require('node:http')

const TARGET = 'https://noctf.fa1lsnow.com'

http
  .createServer(async (req, res) => {
    try {
      const url = TARGET + req.url
      let body
      if (!['GET', 'HEAD'].includes(req.method)) {
        body = await new Promise((resolve) => {
          const chunks = []
          req.on('data', (c) => chunks.push(c))
          req.on('end', () => resolve(Buffer.concat(chunks)))
        })
      }
      const headers = { ...req.headers, host: new URL(TARGET).host }
      delete headers['content-length']
      delete headers['accept-encoding']
      const resp = await fetch(url, { method: req.method, headers, body, redirect: 'manual' })
      const outHeaders = {}
      for (const [k, v] of resp.headers.entries()) {
        if (['content-encoding', 'transfer-encoding', 'content-length'].includes(k)) continue
        outHeaders[k] = v
      }
      res.writeHead(resp.status, outHeaders)
      res.end(Buffer.from(await resp.arrayBuffer()))
    } catch (e) {
      res.writeHead(502)
      res.end(String(e))
    }
  })
  .listen(5080, '127.0.0.1', () => console.log('forwarder on 127.0.0.1:5080'))
