import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { readCatalog, validateCatalog, locales } from '../../../../scripts/Validate-Translations'

const api = fileURLToPath(new URL('../../Localization/Catalogs/', import.meta.url))
const output = fileURLToPath(new URL('../app/locales/catalogs/', import.meta.url))
const english = readCatalog(resolve(api, 'en/api.json'))
for (const locale of locales) {
  const input = resolve(api, `${locale}/api.json`)
  const messages = existsSync(input) ? readCatalog(input) : {}
  validateCatalog(english, messages, input)
  const target = resolve(output, `${locale}/api.json`)
  const content = JSON.stringify(locale === 'en' ? english : messages, null, 2) + '\n'
  if (!existsSync(target) || readFileSync(target, 'utf8') !== content) {
    mkdirSync(dirname(target), { recursive: true })
    writeFileSync(target, content)
  }
}
