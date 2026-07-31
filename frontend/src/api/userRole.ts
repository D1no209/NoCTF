import type { NoCtfDomainIdentityUserRole } from './generated/types.gen'

export const PLATFORM_USER_ROLE = {
  user: 0,
  organizer: 1,
  administrator: 2,
} as const satisfies Record<string, NoCtfDomainIdentityUserRole>

export function isPlatformUserRole(value: unknown): value is NoCtfDomainIdentityUserRole {
  return value === PLATFORM_USER_ROLE.user
    || value === PLATFORM_USER_ROLE.organizer
    || value === PLATFORM_USER_ROLE.administrator
}

export function isPlatformAdministrator(role: NoCtfDomainIdentityUserRole | null | undefined) {
  return role === PLATFORM_USER_ROLE.administrator
}

export function canManagePlatformResources(role: NoCtfDomainIdentityUserRole | null | undefined) {
  return role === PLATFORM_USER_ROLE.organizer
    || role === PLATFORM_USER_ROLE.administrator
}
