import type { MessageKey } from '../locales/zh-CN'
import { shallowRef } from 'vue'

export const supportedLocales = ['zh-CN', 'en'] as const
export type AppLocale = typeof supportedLocales[number]

export const localeDomains = [
  'core',
  'competitions',
  'challenges',
  'leaderboard',
  'administration',
  'account',
  'notifications',
  'runtime',
  'writeups',
] as const
export type LocaleDomain = typeof localeDomains[number]

type LocaleCatalog = Partial<Record<MessageKey, string>>
interface LocaleChunk {
  messages: LocaleCatalog
  englishSources?: Readonly<Record<string, MessageKey>>
}

const localeStorageKey = 'noctf-locale'
const activeLocale = shallowRef<AppLocale>('zh-CN')
const catalogRevision = shallowRef(0)
const catalogs: Record<AppLocale, LocaleCatalog> = { 'zh-CN': {}, en: {} }
const sourceKeys = new Map<string, MessageKey>()
const loadedDomains: Record<AppLocale, Set<LocaleDomain>> = { 'zh-CN': new Set(), en: new Set() }
const pendingDomains = new Map<string, Promise<void>>()
const activeDomains = new Set<LocaleDomain>(['core'])

const catalogLoaders: Record<AppLocale, Record<LocaleDomain, () => Promise<LocaleChunk>>> = {
  'zh-CN': {
    core: () => import('../locales/catalogs/zh-CN/core'),
    competitions: () => import('../locales/catalogs/zh-CN/competitions'),
    challenges: () => import('../locales/catalogs/zh-CN/challenges'),
    leaderboard: () => import('../locales/catalogs/zh-CN/leaderboard'),
    administration: () => import('../locales/catalogs/zh-CN/administration'),
    account: () => import('../locales/catalogs/zh-CN/account'),
    notifications: () => import('../locales/catalogs/zh-CN/notifications'),
    runtime: () => import('../locales/catalogs/zh-CN/runtime'),
    writeups: () => import('../locales/catalogs/zh-CN/writeups'),
  },
  en: {
    core: () => import('../locales/catalogs/en/core'),
    competitions: () => import('../locales/catalogs/en/competitions'),
    challenges: () => import('../locales/catalogs/en/challenges'),
    leaderboard: () => import('../locales/catalogs/en/leaderboard'),
    administration: () => import('../locales/catalogs/en/administration'),
    account: () => import('../locales/catalogs/en/account'),
    notifications: () => import('../locales/catalogs/en/notifications'),
    runtime: () => import('../locales/catalogs/en/runtime'),
    writeups: () => import('../locales/catalogs/en/writeups'),
  },
}

function isSupportedLocale(value: string | null | undefined): value is AppLocale {
  return supportedLocales.includes(value as AppLocale)
}

export function detectLocale(): AppLocale {
  if (!import.meta.client)
    return 'zh-CN'

  let stored: string | null = null
  try {
    stored = localStorage.getItem(localeStorageKey)
  }
  catch {
    // A blocked storage backend must not prevent the application from loading.
  }
  if (isSupportedLocale(stored))
    return stored

  return navigator.language.toLowerCase().startsWith('zh') ? 'zh-CN' : 'en'
}

async function loadLocaleDomain(locale: AppLocale, domain: LocaleDomain): Promise<void> {
  if (loadedDomains[locale].has(domain))
    return

  const cacheKey = `${locale}:${domain}`
  const existing = pendingDomains.get(cacheKey)
  if (existing)
    return existing

  const pending = catalogLoaders[locale][domain]().then((chunk) => {
    Object.assign(catalogs[locale], chunk.messages)
    if (chunk.englishSources) {
      for (const [message, key] of Object.entries(chunk.englishSources))
        sourceKeys.set(message.trim(), key)
    }
    loadedDomains[locale].add(domain)
    catalogRevision.value++
  }).finally(() => pendingDomains.delete(cacheKey))

  pendingDomains.set(cacheKey, pending)
  return pending
}

/** Loads the core catalog before Nuxt mounts, preventing a mixed-language first frame. */
export async function initializeLocale(): Promise<AppLocale> {
  const locale = detectLocale()
  await loadLocaleDomain(locale, 'core')
  activeLocale.value = locale
  if (import.meta.client)
    document.documentElement.lang = locale
  return locale
}

/** Ensures the current route's feature catalogs exist before its page renders. */
export async function ensureLocaleDomains(domains: readonly LocaleDomain[]): Promise<void> {
  for (const domain of domains)
    activeDomains.add(domain)
  await Promise.all(domains.map(domain => loadLocaleDomain(activeLocale.value, domain)))
}

/** Prepares the target language without changing visible text. */
export async function prepareLocale(locale: AppLocale): Promise<void> {
  await Promise.all([...activeDomains].map(domain => loadLocaleDomain(locale, domain)))
}

export function currentLocale(): AppLocale {
  return activeLocale.value
}

export function setLocale(locale: AppLocale): void {
  activeLocale.value = locale
  if (!import.meta.client)
    return

  try {
    localStorage.setItem(localeStorageKey, locale)
  }
  catch {
    // The in-memory locale and document language still update for this session.
  }
  document.documentElement.lang = locale
}

export function localeTag(): string {
  return activeLocale.value === 'en' ? 'en-US' : 'zh-CN'
}

export function translate(source: string, values: Record<string, string | number> = {}): string {
  void catalogRevision.value
  const messages = catalogs[activeLocale.value]
  const template = Object.hasOwn(messages, source) ? messages[source as MessageKey]! : source
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    values[key] === undefined ? match : String(values[key]))
}

/** UI-owned messages use a stable, type-checked catalog key. */
export function t(key: MessageKey, values: Record<string, string | number> = {}): string {
  return translate(key, values)
}

export function localizeMessage(message: string | null | undefined): string {
  if (!message) return ''
  return translate(sourceKeys.get(message.trim()) ?? message)
}
