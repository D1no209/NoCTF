import { fileURLToPath } from 'node:url'

const directory = fileURLToPath(new URL('.', import.meta.url))
const frontend = fileURLToPath(new URL('../node_modules/@nuxt/cli/bin/nuxi.mjs', import.meta.url))
const api = Bun.spawn([process.execPath, '--watch', 'server.ts'], { cwd: directory, stdout: 'inherit', stderr: 'inherit' })
// Match `bun run dev`: Nuxt's CLI uses Node for Nitro workers on Windows.
const ui = Bun.spawn(['node', frontend, 'dev', directory, '--host', '127.0.0.1', '--port', '3001', '--no-fork'], {
  cwd: directory, stdout: 'inherit', stderr: 'inherit',
  env: { ...process.env, NUXT_API_PROXY_TARGET: 'http://127.0.0.1:5081' },
})
function stop() { api.kill(); ui.kill() }
process.on('SIGINT', stop)
process.on('SIGTERM', stop)
const status = await Promise.race([api.exited, ui.exited])
stop()
process.exit(status)
