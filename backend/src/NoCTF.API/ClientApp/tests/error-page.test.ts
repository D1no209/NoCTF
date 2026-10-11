import { describe, expect, test } from 'bun:test'
import { errorDisplayPath, errorPagePresentation, errorRecoveryPath } from '../app/features/shell/error-page'
import { englishMessages } from '../app/locales/en'
import { chineseMessages } from '../app/locales/zh-CN'
import { currentLocale, initializeLocale, prepareLocale, setLocale, translate } from '../app/utils/i18n'

describe('application error recovery', () => {
  test('error copy is available at bootstrap and follows language switching without route middleware', async () => {
    const originalLocale = currentLocale()
    try {
      await initializeLocale()
      expect(translate('errorPage.notFound.title')).toBe('页面未找到')
      await prepareLocale('en')
      setLocale('en')
      expect(translate('errorPage.notFound.title')).toBe('Page not found')
      expect(translate('errorPage.status', { code: 404 })).toBe('Error code 404')
    }
    finally {
      setLocale(originalLocale)
    }
  })

  test('missing pages lead to competitions while authentication errors lead to sign-in', () => {
    expect(errorPagePresentation(404).action).toBe('competitions')
    expect(errorPagePresentation(410).action).toBe('competitions')
    expect(errorPagePresentation(401).action).toBe('login')
    expect(errorPagePresentation(403).action).toBe('home')
    expect(errorPagePresentation(403).showPath).toBe(false)
  })

  test('only transient failures offer an explicit retry', () => {
    for (const status of [408, 429, 500, 502, 503, 504])
      expect(errorPagePresentation(status).action).toBe('retry')
    for (const status of [400, 403, 404, 409, 413, 422])
      expect(errorPagePresentation(status).action).not.toBe('retry')
    expect(errorPagePresentation(429).descriptionKey).not.toBe(errorPagePresentation(503).descriptionKey)
  })

  test('invalid status values are treated as service failures', () => {
    for (const status of [undefined, NaN, Infinity, 200, 399, 600, 404.5])
      expect(errorPagePresentation(status).status).toBe(500)
  })

  test('every status has bilingual public copy and a labelled recovery action', () => {
    for (const status of [400, 401, 403, 404, 408, 410, 429, 500, 503]) {
      const presentation = errorPagePresentation(status)
      for (const key of [presentation.titleKey, presentation.descriptionKey, presentation.actionKey]) {
        expect(englishMessages[key].trim().length).toBeGreaterThan(0)
        expect(chineseMessages[key]?.trim().length).toBeGreaterThan(0)
      }
    }
  })

  test('history and redirect values cannot send the user to another origin', () => {
    for (const path of [undefined, null, [], {}, '', 'https://example.com', '//example.com', '/\\example.com', '/path\nnext', '/path\t', '/path\u007f', 'javascript:alert(1)'])
      expect(errorRecoveryPath(path)).toBeNull()
    expect(errorRecoveryPath('/competitions/123?tab=teams#members')).toBe('/competitions/123?tab=teams#members')
    expect(errorRecoveryPath('/')).toBe('/')
  })

  test('the visible address never includes query or fragment values', () => {
    expect(errorDisplayPath('/competition?token=private#private')).toBe('/competition')
    expect(errorDisplayPath('/competition#private?token=private')).toBe('/competition')
    expect(errorDisplayPath('//example.com?token=private')).toBe('')
  })
})
