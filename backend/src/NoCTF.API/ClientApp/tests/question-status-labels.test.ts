import { expect, test } from 'bun:test'
import { translate } from '../app/utils/i18n'
import { questionStatusLabel } from '../app/utils/labels'

test('numeric question status values resolve to their localized labels', () => {
  expect(questionStatusLabel(0)).toBe(translate('notifications.label.awaitingReply'))
  expect(questionStatusLabel(1)).toBe(translate('notifications.label.replied'))
  expect(questionStatusLabel(2)).toBe(translate('notifications.label.resolved'))
  expect(questionStatusLabel(3)).toBe(translate('notifications.label.closed'))
})

test('missing question status renders the unknown label, never a raw key', () => {
  expect(questionStatusLabel(null)).toBe(translate('common.label.unknown'))
  expect(questionStatusLabel(undefined)).toBe(translate('common.label.unknown'))
  for (const status of [0, 1, 2, 3])
    expect(questionStatusLabel(status)).not.toContain('notifications.label.')
})
