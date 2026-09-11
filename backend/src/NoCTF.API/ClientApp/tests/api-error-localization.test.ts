import { afterEach, describe, expect, test } from 'bun:test'
import { parseApiError } from '../app/utils/api-error'
import { setLocale } from '../app/utils/i18n'

describe('api error localization', () => {
  afterEach(() => setLocale('zh-CN'))

  test('explains competition deletion guards without suggesting that pausing is enough', () => {
    setLocale('zh-CN')
    expect(parseApiError({ status: 409, detail: 'Finish the competition before permanently deleting it. Paused competitions cannot be deleted.' }).message)
      .toBe('请先结束比赛再永久删除，暂停中的比赛不能删除。')
    expect(parseApiError({ status: 409, detail: 'Notification threads contain cross-scope or unproven references. No data was deleted. Review the conflicting notification IDs before retrying.' }).message)
      .toBe('通知线程存在跨作用域或归属不明的引用，未删除任何数据。请检查返回的冲突通知 ID 后重试。')
  })

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

  test('shows explicit conflict details but hides unknown diagnostics for other statuses', () => {
    setLocale('zh-CN')

    expect(parseApiError({
      status: 500,
      title: 'An error occurred while processing your request.',
    }).message).toBe('服务器内部错误,请稍后重试')

    expect(parseApiError({
      status: 400,
      detail: 'Unexpected legacy payload value.',
    }).message).toBe('请求参数有误,请检查输入')

    expect(parseApiError({
      statusCode: 409,
      code: 'RuntimeStateConflict',
      message: 'The runtime is stopping and cannot be extended.',
    })).toMatchObject({
      status: 409,
      code: 'RuntimeStateConflict',
      message: 'The runtime is stopping and cannot be extended.',
    })

    expect(parseApiError({
      statusCode: 409,
      message: 'One or more errors occurred.',
      errors: { General: 'The competition has already finished.' },
    }).message).toBe('The competition has already finished.')

    expect(parseApiError({
      status: 409,
      code: 'RuntimeStateConflict',
      detail: 'The runtime state changed while this request was being processed. Refresh the runtime status before retrying.',
    }).message).toBe('处理请求期间运行环境状态已变化，请刷新运行状态后重试。')
  })

  test('keeps unknown English diagnostics in the English locale', () => {
    setLocale('en')

    expect(parseApiError({
      status: 500,
      title: 'An error occurred while processing your request.',
    }).message).toBe('An error occurred while processing your request.')
  })

  test('localizes stable human verification problem codes', () => {
    setLocale('zh-CN')

    expect(parseApiError({
      status: 403,
      code: 'HumanVerificationRequired',
      detail: 'Complete human verification before retrying this operation.',
    }).message).toBe('请完成人机验证后重试。')
    expect(parseApiError({
      status: 503,
      code: 'HumanVerificationUnavailable',
    }).message).toBe('人机验证服务暂不可用，请稍后重新验证。')
    expect(parseApiError({
      status: 400,
      code: 'HumanVerificationSecretInvalid',
    }).message).toBe('请输入有效的 Provider 密钥。')
  })
})
