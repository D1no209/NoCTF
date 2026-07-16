import type { Plugin } from 'vite'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

/**
 * Dev-only Vite plugin that serves `/mock-data.json` from `src/mocks/mock-data.json`.
 * By providing the mock route table through the dev server instead of the static
 * `public/` directory, the file is never copied into `dist` production builds.
 */
export function mockDataPlugin(): Plugin {
  return {
    name: 'noctf-mock-data',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use('/mock-data.json', (_req, res, next) => {
        try {
          const data = readFileSync(resolve('src/mocks/mock-data.json'), 'utf-8')
          res.setHeader('content-type', 'application/json')
          res.end(data)
        }
        catch (err) {
          next(err)
        }
      })
    },
  }
}