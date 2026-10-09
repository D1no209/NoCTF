import { expect, test } from 'bun:test'
import { parseLiveSoloError } from '../app/features/live-solo/live-solo-errors'
import { message } from '../app/utils/i18n'

test('LiveSolo media and isolation failures retain specific reasons rather than the generic 409 message', () => {
  expect(parseLiveSoloError({ status: 409, code: 'MediaUnavailable' }, message('liveSolo.error.operation')).displayMessage)
    .toEqual(message('liveSolo.error.mediaUnavailable'))
  expect(parseLiveSoloError({ status: 409, code: 'IsolationUnavailable' }, message('liveSolo.error.operation')).displayMessage)
    .toEqual(message('liveSolo.error.isolation'))
})

test('field validation and authentication failures keep their own messages', () => {
  const field = parseLiveSoloError({ status: 422, code: 'InvalidConfiguration', errors: { userIds: ['Invalid roster'] } }, message('liveSolo.error.operation'))
  expect(field.fieldErrors).toEqual({ userIds: ['Invalid roster'] })
  expect(field.displayMessage).not.toEqual(message('liveSolo.error.configuration'))
  expect(parseLiveSoloError({ status: 401, code: 'MfaRequired' }, message('liveSolo.error.operation')).code).toBe('MfaRequired')
  expect(parseLiveSoloError({ code: 'constructor' }, message('liveSolo.error.operation')).displayMessage).toEqual(message('liveSolo.error.operation'))
})
