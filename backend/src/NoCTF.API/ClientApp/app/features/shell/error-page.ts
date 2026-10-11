import type { MessageKey } from '../../locales/en'

export type ErrorRecoveryAction = 'home' | 'competitions' | 'login' | 'retry'

export interface ErrorPagePresentation {
  status: number
  titleKey: MessageKey
  descriptionKey: MessageKey
  action: ErrorRecoveryAction
  actionKey: MessageKey
  showPath: boolean
}

/** Public copy is selected by status; exception messages and stacks never reach the view. */
export function errorPagePresentation(status: number | undefined): ErrorPagePresentation {
  const code = typeof status === 'number' && Number.isInteger(status) && status >= 400 && status <= 599 ? status : 500
  const base = { status: code, showPath: false }
  switch (code) {
    case 401:
      return { ...base, titleKey: 'errorPage.unauthorized.title', descriptionKey: 'errorPage.unauthorized.description', action: 'login', actionKey: 'errorPage.action.login' }
    case 403:
      return { ...base, titleKey: 'errorPage.forbidden.title', descriptionKey: 'errorPage.forbidden.description', action: 'home', actionKey: 'errorPage.action.home' }
    case 404:
    case 410:
      return { ...base, showPath: true, titleKey: 'errorPage.notFound.title', descriptionKey: 'errorPage.notFound.description', action: 'competitions', actionKey: 'errorPage.action.competitions' }
    case 408:
      return { ...base, titleKey: 'errorPage.timeout.title', descriptionKey: 'errorPage.timeout.description', action: 'retry', actionKey: 'errorPage.action.retry' }
    case 429:
      return { ...base, titleKey: 'errorPage.rateLimited.title', descriptionKey: 'errorPage.rateLimited.description', action: 'retry', actionKey: 'errorPage.action.retry' }
    default:
      return code >= 500
        ? { ...base, titleKey: 'errorPage.unavailable.title', descriptionKey: 'errorPage.unavailable.description', action: 'retry', actionKey: 'errorPage.action.retry' }
        : { ...base, titleKey: 'errorPage.invalid.title', descriptionKey: 'errorPage.invalid.description', action: 'home', actionKey: 'errorPage.action.home' }
  }
}

/** Error recovery never leaves this origin, even with malformed history or redirect input. */
export function errorRecoveryPath(value: unknown): string | null {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//')
    || /[\\\u0000-\u0020\u007f]/.test(value)) return null
  return value
}

export function errorDisplayPath(value: unknown): string {
  return errorRecoveryPath(value)?.split(/[?#]/, 1)[0] ?? ''
}
