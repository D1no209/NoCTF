import { afterEach, describe, expect, test } from 'bun:test'
import { effect, stop } from 'vue'
import { parseApiError } from '../app/utils/api-error'
import { languageHeaders, localizeMessage, mergeLocaleCatalog, message, setLocale, translate } from '../app/utils/i18n'

afterEach(() => setLocale('zh-CN'))

describe('semantic feedback descriptors', () => {
  test('partial and empty Crowdin strings retain English without dropping usable translations', () => {
    const resolved = mergeLocaleCatalog({ 'common.action.save': 'Save', 'common.action.cancel': 'Cancel' }, { 'common.action.save': ' ', 'common.action.cancel': '取消' })
    expect(resolved['common.action.save']).toBe('Save')
    expect(resolved['common.action.cancel']).toBe('取消')
    expect(mergeLocaleCatalog({ 'common.action.save': 'Save' }, {})['common.action.save']).toBe('Save')
  })
  test('stored API errors react to language changes without matching diagnostic prose', () => {
    setLocale('en')
    const error = parseApiError({ status: 409, code: 'Conflict', messageKey: 'api.validation.required', messageArguments: { field: 'Title' }, detail: 'diagnostic must not identify this message' })
    const rendered: string[] = []
    const watcher = effect(() => rendered.push(localizeMessage(error.displayMessage)))
    setLocale('zh-CN')
    expect(rendered).toEqual(['Title is required.', 'Title不能为空。'])
    expect(error.message).toBe('Title不能为空。')
    expect(error.code).toBe('Conflict')
    stop(watcher)
  })
  test('field descriptors outrank generic problem codes and interpolate in both languages', () => {
    setLocale('en')
    const error = parseApiError({ status: 400, code: 'HumanVerificationRequired', errors: { Password: ['raw'] }, errorMessages: { Password: [{ key: 'api.validation.minimumLength', arguments: { field: 'Password', minimum: 8 } }] } })
    expect(error.message).toBe('Password must contain at least 8 characters.')
    setLocale('zh-CN')
    expect(error.message).toBe('Password至少需要 8 个字符。')
  })
  test('unknown field descriptors keep their concrete fallback beside known descriptors', () => {
    setLocale('en')
    const error = parseApiError({ status: 400, errors: { Terms: ['old text', 'specific operation reason'] }, errorMessages: { Terms: [{ key: 'api.validation.equal', arguments: { field: 'Terms', comparison: true } }, { key: 'unknown.key' }] } })
    expect(error.message).toBe('Terms must equal true; specific operation reason')
  })
  test('unknown metadata uses existing operation fallback and preserves open text', () => {
    const error = parseApiError({ status: 400, messageKey: 'unknown.key', detail: 'internal diagnostic' }, message('common.error.downloadFailed', { status: 400 }))
    expect(error.message).toBe(translate('common.error.downloadFailed', { status: 400 }))
    expect(localizeMessage('user-authored English content')).toBe('user-authored English content')
  })
  test('semantic keys stay the same when an error is described with different diagnostics', () => {
    const one = parseApiError({ status: 400, messageKey: 'api.validation.email', messageArguments: { field: 'Email' }, detail: 'old text' })
    const two = parseApiError({ status: 400, messageKey: 'api.validation.email', messageArguments: { field: 'Email' }, detail: 'changed text' })
    expect(one.displayMessage).toEqual(two.displayMessage)
  })
  test('request headers follow the selected language and preserve authentication', () => {
    setLocale('en')
    expect(languageHeaders({ Authorization: 'Bearer opaque' }).get('Accept-Language')).toBe('en')
    setLocale('zh-CN')
    const headers = languageHeaders({ Authorization: 'Bearer opaque' })
    expect(headers.get('Accept-Language')).toBe('zh-CN')
    expect(headers.get('Authorization')).toBe('Bearer opaque')
  })
})
