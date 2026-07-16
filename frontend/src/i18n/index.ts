import { createI18n } from 'vue-i18n'
import en from '@/locales/en.json'
import zhCN from '@/locales/zh-CN.json'

const messages = {
  en,
  'zh-CN': zhCN,
}

const savedLocale = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null
const locale = savedLocale === 'zh-CN' ? 'zh-CN' : 'en'

export const i18n = createI18n({
  legacy: false,
  locale,
  fallbackLocale: 'en',
  messages,
})

export function translate(key: string, named?: Record<string, unknown>) {
  return i18n.global.t(key, named ?? {})
}
