import { englishMessages } from '../locales/en'
import { shallowRef } from 'vue'

export const supportedLocales = ['zh-CN', 'en'] as const
export type AppLocale = typeof supportedLocales[number]

const localeStorageKey = 'noctf-locale'

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

// Resolve the persisted locale while modules are evaluated so labels declared at
// module scope are created in the same language as the first rendered frame.
const activeLocale = shallowRef<AppLocale>(detectLocale())

export function initializeLocale(): AppLocale {
  const locale = detectLocale()
  activeLocale.value = locale
  if (import.meta.client)
    document.documentElement.lang = locale
  return locale
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

export function translate(
  source: string,
  values: Record<string, string | number> = {},
): string {
  const template = activeLocale.value === 'en'
    ? englishMessages[source] ?? source
    : source
  return template.replace(/\{(\w+)\}/g, (match, key: string) =>
    values[key] === undefined ? match : String(values[key]))
}
