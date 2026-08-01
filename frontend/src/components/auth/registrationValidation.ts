import * as z from 'zod'

const registrationUserNamePattern = /^[\w-]+$/u

interface RegistrationValidationMessages {
  userNameRequired: string
  userNameLength: string
  userNameFormat: string
  emailRequired: string
  emailInvalid: string
  passwordRequired: string
  passwordMin: string
}

export function createRegistrationSchema(messages: RegistrationValidationMessages) {
  return z.object({
    userName: z.string()
      .trim()
      .min(1, messages.userNameRequired)
      .min(3, messages.userNameLength)
      .max(64, messages.userNameLength)
      .regex(registrationUserNamePattern, messages.userNameFormat),
    email: z.string()
      .min(1, messages.emailRequired)
      .email(messages.emailInvalid),
    password: z.string()
      .min(1, messages.passwordRequired)
      .min(8, messages.passwordMin),
  })
}
