import { describe, expect, test } from 'bun:test'
import { createRegistrationSchema } from '../src/components/auth/registrationValidation'

const messages = {
  userNameRequired: 'username required',
  userNameLength: 'username length',
  userNameFormat: 'username format',
  emailRequired: 'email required',
  emailInvalid: 'email invalid',
  passwordRequired: 'password required',
  passwordMin: 'password length',
}

const schema = createRegistrationSchema(messages)

describe('registration form validation', () => {
  test('accepts the API registration contract', () => {
    expect(schema.safeParse({
      userName: '  player_01  ',
      email: 'player@example.test',
      password: 'eight888',
    }).success).toBe(true)
  })

  test('rejects credentials that the API rejects', () => {
    expect(schema.safeParse({
      userName: 'player_01',
      email: 'player@example.test',
      password: 'seven77',
    }).success).toBe(false)
    expect(schema.safeParse({
      userName: '选手',
      email: 'player@example.test',
      password: 'eight888',
    }).success).toBe(false)
    expect(schema.safeParse({
      userName: 'player.name',
      email: 'player@example.test',
      password: 'eight888',
    }).success).toBe(false)
  })
})
