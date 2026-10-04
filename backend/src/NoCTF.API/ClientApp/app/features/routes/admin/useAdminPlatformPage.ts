import { markRaw } from 'vue'
import { adminWorkspacePath } from '~/features/admin/admin-navigation'

import { Beaker, Container, History, Info, KeyRound, MailCheck, ScrollText, Users } from '@lucide/vue'
import type { WorkspaceNavGroup } from '../../app/workspace-nav'
import AppWorkspaceNavComponent from '../../app/AppWorkspaceNav.vue'

/** Owns state, effects and commands for AdminPlatformPage. */
export function useAdminPlatformPage() {
  const route = useRoute()

  const activePath = computed(() => adminWorkspacePath(route.path))

  const navGroups = computed<WorkspaceNavGroup[]>(() => [
    {
      label: translate("administration.label.platform"),
      items: [
        { to: '/admin/platform', label: translate("administration.label.platformInformation"), icon: Info, exact: true },
        { to: '/admin/platform/users', label: translate("administration.label.user"), icon: Users },
        { to: '/admin/platform/email', label: translate("administration.label.emailHumanVerification"), icon: MailCheck },
        { to: '/admin/platform/authentication', label: translate('sso.authenticationSettings'), icon: KeyRound },
        { to: '/admin/platform/experiments', label: translate('administration.label.experimentalFeatures'), icon: Beaker },
      ],
    },
    {
      label: translate("administration.label.maintenance"),
      items: [
        { to: '/admin/platform/runtimes', label: translate("common.label.runtimeContainers"), icon: Container },
        { to: '/admin/platform/logs', label: translate("administration.label.log"), icon: ScrollText },
        { to: '/admin/platform/audit', label: translate("administration.label.audit"), icon: History },
      ],
    },
  ])

  const AppWorkspaceNav = markRaw(AppWorkspaceNavComponent)

  return {
      navGroups,
      activePath,
      AppWorkspaceNav
    }
}

export type AdminPlatformPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformPage>>>
