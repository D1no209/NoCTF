import { ensureLocaleDomains, initializeLocale, localeDomains, prepareLocale } from '../app/utils/i18n'

await initializeLocale()
await ensureLocaleDomains(localeDomains)
await prepareLocale('en')
