import { expect, test } from 'bun:test'
import { credentialImportFeedback } from '../app/features/admin/credentials/import-feedback'
import { localizeMessage } from '../app/utils/i18n'

test('all conflicting rows retain their identities and localized reasons', () => {
  const feedback = credentialImportFeedback({
    code: 'InvalidRows',
    issues: [
      { rowNumber: 2, code: 'ExistingUserName', message: 'alice' },
      { rowNumber: 2, code: 'ExistingEmail', message: 'alice@example.test' },
      { rowNumber: 8, code: 'ExistingTeam', message: '蓝队' },
    ],
  })
  expect(feedback.issues.map(issue => issue.rowNumber)).toEqual([2, 2, 8])
  expect(localizeMessage(feedback.issues[0]!.description)).toContain('alice')
  expect(localizeMessage(feedback.issues[1]!.description)).toContain('alice@example.test')
  expect(localizeMessage(feedback.issues[2]!.description)).toContain('蓝队')
  expect(feedback.error).toEqual({ key: 'administration.credentials.failure.InvalidRows', arguments: {} })
})

test('empty and finished imports keep their specific failure reasons', () => {
  for (const code of ['EmptyFile', 'CompetitionFinished', 'TransactionConflict'] as const) {
    const feedback = credentialImportFeedback({ code, issues: [] })
    expect(feedback.issues).toEqual([])
    expect(localizeMessage(feedback.error)).not.toContain('undefined')
    expect(feedback.error).toEqual({ key: `administration.credentials.failure.${code}`, arguments: {} })
  }
})

test('header errors keep source row one and the offending column name', () => {
  const feedback = credentialImportFeedback({ code: 'InvalidRows', issues: [{ code: 'DuplicateColumn', rowNumber: 1, message: 'username' }] })
  expect(feedback.issues[0]!.rowNumber).toBe(1)
  expect(localizeMessage(feedback.issues[0]!.description)).toContain('username')
})
