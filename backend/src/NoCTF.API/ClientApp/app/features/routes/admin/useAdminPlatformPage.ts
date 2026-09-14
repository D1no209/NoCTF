import { markRaw } from 'vue'

import { Activity, Beaker, Container, Globe, History, Info, MailCheck, ScrollText, Users } from '@lucide/vue'
import type { WorkspaceNavGroup } from '../../app/workspace-nav'
import AppWorkspaceNavComponent from '../../app/AppWorkspaceNav.vue'

/** Owns state, effects and commands for AdminPlatformPage. */
export function useAdminPlatformPage() {
  const route = useRoute()

  const activePath = computed(() => route.path)

  const navGroups = computed<WorkspaceNavGroup[]>(() => [
    {
      label: translate("ui.platform"),
      items: [
        { to: '/admin/platform', label: translate("ui.platformInformation"), icon: Info, exact: true },
        { to: '/admin/platform/users', label: translate("ui.user"), icon: Users },
        { to: '/admin/platform/email', label: translate("ui.emailAndHumanVerification"), icon: MailCheck },
        { to: '/admin/platform/experiments', label: translate('ui.experimentalFeatures'), icon: Beaker },
      ],
    },
    {
      label: translate("ui.maintenance"),
      items: [
        { to: '/admin/platform/monitoring', label: translate("ui.monitoring"), icon: Activity },
        { to: '/admin/platform/runtimes', label: translate("ui.runtimeContainers"), icon: Container },
        { to: '/admin/platform/public-gateway', label: translate("ui.intranetTunneling"), icon: Globe },
        { to: '/admin/platform/logs', label: translate("ui.log"), icon: ScrollText },
        { to: '/admin/platform/audit', label: translate("ui.audit"), icon: History },
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
