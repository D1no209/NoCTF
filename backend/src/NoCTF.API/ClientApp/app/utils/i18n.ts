import type { MessageKey } from '../locales/en'
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
  'api',
  'mfa',
] as const
export type LocaleDomain = typeof localeDomains[number]

type LocaleCatalog = Partial<Record<MessageKey, string>>
export type MessageArguments = Record<string, string | number | boolean | null | MessageDescriptor | MessageGroup>
export interface MessageDescriptor { key: MessageKey; arguments?: MessageArguments }
export interface MessageGroup { messages: UiMessage[]; separator: MessageKey }
export type UiMessage = string | MessageDescriptor | MessageGroup


const localeStorageKey = 'noctf-locale'
const activeLocale = shallowRef<AppLocale>('zh-CN')
const catalogRevision = shallowRef(0)
const catalogs: Record<AppLocale, LocaleCatalog> = { 'zh-CN': {}, en: {} }
const loadedDomains: Record<AppLocale, Set<LocaleDomain>> = { 'zh-CN': new Set(), en: new Set() }
const pendingDomains = new Map<string, Promise<void>>()
const activeDomains = new Set<LocaleDomain>(['core', 'api', 'mfa'])

export function mergeLocaleCatalog(english: LocaleCatalog, translated: LocaleCatalog): LocaleCatalog {
  return { ...english, ...Object.fromEntries(Object.entries(translated).filter(([, value]) => value?.trim())) }
}

const catalogLoaders: Record<AppLocale, Record<LocaleDomain, () => Promise<LocaleCatalog>>> = {
  'zh-CN': {
    core: () => import('../locales/catalogs/zh-CN/core.json').then(chunk => chunk.default),
    competitions: () => import('../locales/catalogs/zh-CN/competitions.json').then(chunk => chunk.default),
    challenges: () => import('../locales/catalogs/zh-CN/challenges.json').then(chunk => chunk.default),
    leaderboard: () => import('../locales/catalogs/zh-CN/leaderboard.json').then(chunk => chunk.default),
    administration: () => import('../locales/catalogs/zh-CN/administration.json').then(chunk => chunk.default),
    account: () => import('../locales/catalogs/zh-CN/account.json').then(chunk => chunk.default),
    notifications: () => import('../locales/catalogs/zh-CN/notifications.json').then(chunk => chunk.default),
    runtime: () => import('../locales/catalogs/zh-CN/runtime.json').then(chunk => chunk.default),
    writeups: () => import('../locales/catalogs/zh-CN/writeups.json').then(chunk => chunk.default),
    api: () => import('../locales/catalogs/zh-CN/api.json').then(chunk => chunk.default),
    mfa: () => import('../locales/catalogs/zh-CN/mfa.json').then(chunk => chunk.default),
  },
  'en': {
    core: () => import('../locales/catalogs/en/core.json').then(chunk => chunk.default),
    competitions: () => import('../locales/catalogs/en/competitions.json').then(chunk => chunk.default),
    challenges: () => import('../locales/catalogs/en/challenges.json').then(chunk => chunk.default),
    leaderboard: () => import('../locales/catalogs/en/leaderboard.json').then(chunk => chunk.default),
    administration: () => import('../locales/catalogs/en/administration.json').then(chunk => chunk.default),
    account: () => import('../locales/catalogs/en/account.json').then(chunk => chunk.default),
    notifications: () => import('../locales/catalogs/en/notifications.json').then(chunk => chunk.default),
    runtime: () => import('../locales/catalogs/en/runtime.json').then(chunk => chunk.default),
    writeups: () => import('../locales/catalogs/en/writeups.json').then(chunk => chunk.default),
    api: () => import('../locales/catalogs/en/api.json').then(chunk => chunk.default),
    mfa: () => import('../locales/catalogs/en/mfa.json').then(chunk => chunk.default),
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

  const pending = Promise.all([catalogLoaders.en[domain](), catalogLoaders[locale][domain]()]).then(([english, translated]) => {
    Object.assign(catalogs.en, english)
    Object.assign(catalogs[locale], mergeLocaleCatalog(english, translated))
    loadedDomains[locale].add(domain)
    catalogRevision.value++
  }).finally(() => pendingDomains.delete(cacheKey))

  pendingDomains.set(cacheKey, pending)
  return pending
}

/** Loads the core catalog before Nuxt mounts, preventing a mixed-language first frame. */
export async function initializeLocale(): Promise<AppLocale> {
  const locale = detectLocale()
  await Promise.all(['core', 'api', 'mfa'].map(domain => loadLocaleDomain(locale, domain as LocaleDomain)))
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

/** Open protocol labels may pass through unchanged; UI-owned text uses t/$t. */
export function translate(source: string, values: MessageArguments = {}): string {
  void catalogRevision.value
  const messages = catalogs[activeLocale.value]
  const template = Object.hasOwn(messages, source) ? messages[source as MessageKey]! : source
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    values[key] === undefined ? match : typeof values[key] === 'object' ? localizeMessage(values[key]) : String(values[key]))
}

/** UI-owned messages use a stable, type-checked catalog key. */
export function t(key: MessageKey, values: MessageArguments = {}): string {
  return translate(key, values)
}

/** Descriptions remain structured so stored feedback reacts to locale changes. */
export function message(key: MessageKey, arguments_: MessageArguments = {}): MessageDescriptor {
  return { key, arguments: arguments_ }
}

export function isMessageKey(value: string): value is MessageKey {
  return Object.hasOwn(catalogs.en, value)
}

export function localizeMessage(value: UiMessage | null | undefined): string {
  if (!value) return ''
  if (typeof value === 'string') return value
  if ('messages' in value) return value.messages.map(localizeMessage)
    .map((text, index) => index === value.messages.length - 1 ? text : text.replace(/[。.!?；;]+$/u, ''))
    .join(translate(value.separator))
  return translate(value.key, value.arguments)
}

export function languageHeaders(headers?: HeadersInit): Headers {
  const result = new Headers(headers)
  result.set('Accept-Language', currentLocale())
  return result
}
