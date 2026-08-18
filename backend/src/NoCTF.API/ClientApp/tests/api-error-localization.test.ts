import { afterEach, describe, expect, test } from 'bun:test'
import { parseApiError } from '../app/utils/api-error'
import { setLocale } from '../app/utils/i18n'

describe('api error localization', () => {
  afterEach(() => setLocale('zh-CN'))

  test('localizes known backend problem details in the Chinese locale', () => {
    setLocale('zh-CN')

    expect(parseApiError({
      status: 400,
      detail: 'Specify no event range for staff history, or a range between zero and 31 days.',
    }).message).toBe('工作人员查看完整历史时请不要设置时间范围；普通查询的时间范围需在 0 到 31 天内。')

    expect(parseApiError({
      status: 400,
      errors: { To: ['The incident query range must be between zero and 31 days.'] },
    }).message).toBe('作弊事件查询时间范围必须在 0 到 31 天内。')

    expect(parseApiError({
      status: 400,
      title: 'Invalid cursor.',
    }).message).toBe('游标无效,请刷新后重试')
  })

  test('keeps the same backend details in the English locale', () => {
    setLocale('en')

    expect(parseApiError({
      status: 400,
      detail: 'The platform log query range must be between zero and 14 days.',
    }).message).toBe('The platform log query range must be between zero and 14 days.')
  })
})
