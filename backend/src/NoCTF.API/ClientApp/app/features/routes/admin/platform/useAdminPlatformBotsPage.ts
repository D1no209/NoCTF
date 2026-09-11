import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { Bot, Copy, KeyRound, Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformCreateBot, adminPlatformIssueUserToken, adminPlatformListUsers } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformIssuePlatformUserTokenResponse, NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse, NoCtfapiEndpointsAuthenticationUserRoleProtocol } from '../../../../api'
import { createLatestRequestGuard } from '../../../../lib/latest-request'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse

/** Owns state, effects and commands for AdminPlatformBotsPage. */
export function useAdminPlatformBotsPage() {
  const ROLE_LABELS: Record<string, string> = { User: "ui.user", Organizer: "ui.organizer", Administrator: "ui.administrator" }

  const bots = ref<PlatformUser[]>([])

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminPlatformListUsers()
    loading.value = false
    if (error) {
      loadError.value = parseApiError(error).message
      return
    }
    bots.value = (data?.items ?? []).filter(user => user.kind === 'Bot')
  }

  const createOpen = ref(false)

  const creating = ref(false)

  const botName = ref('')

  const botRole = ref<NoCtfapiEndpointsAuthenticationUserRoleProtocol>('User')

  function openCreate(): void {
    botName.value = ''
    botRole.value = 'User'
    createOpen.value = true
  }

  async function createBot(): Promise<void> {
    if (!botName.value.trim()) return
    creating.value = true
    const { error } = await adminPlatformCreateBot({
      body: { userName: botName.value.trim(), role: botRole.value },
    })
    creating.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    createOpen.value = false
    toast.success(translate("ui.botCreated"))
    await load()
  }

  const issueOpen = ref(false)

  const issuing = ref(false)

  const issueTarget = ref<PlatformUser | null>(null)

  const expiresInSeconds = ref(3600)

  const issueReason = ref('')

  const issuedToken = ref<NoCtfapiEndpointsAdministrationPlatformIssuePlatformUserTokenResponse | null>(null)

  const tokenIssueRequests = createLatestRequestGuard()

  function openIssue(bot: PlatformUser): void {
    tokenIssueRequests.invalidate()
    issueTarget.value = bot
    expiresInSeconds.value = 3600
    issueReason.value = ''
    issuedToken.value = null
    issueOpen.value = true
  }

  async function issueToken(): Promise<void> {
    if (issuing.value || !issueTarget.value?.id || issueReason.value.trim().length < 3) return
    const request = tokenIssueRequests.begin()
    issuing.value = true
    const { data, error } = await adminPlatformIssueUserToken({
      path: { userId: issueTarget.value.id },
      body: {
        expiresInSeconds: expiresInSeconds.value,
        reason: issueReason.value.trim(),
      },
    })
    if (!tokenIssueRequests.isCurrent(request) || !issueOpen.value) return
    issuing.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    issuedToken.value = data ?? null
  }

  async function copyToken(): Promise<void> {
    if (!issuedToken.value?.accessToken) return
    try {
      await navigator.clipboard.writeText(issuedToken.value.accessToken)
      toast.success(translate("ui.copiedToClipboard"))
    }
    catch {
      toast.error(translate("ui.copyFailedPleaseManuallySelectCopy"))
    }
  }

  onMounted(() => {
    void load()
  })

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  const viewBindings = {
      Bot,
      Copy,
      KeyRound,
      Plus,
      ROLE_LABELS,
      bots,
      loading,
      loadError,
      createOpen,
      creating,
      botName,
      botRole,
      openCreate,
      createBot,
      issueOpen,
      issuing,
      issueTarget,
      expiresInSeconds,
      issueReason,
      issuedToken,
      openIssue,
      issueToken,
      copyToken,
      AdminDateTime
    }
  const viewState = proxyRefs(viewBindings)

  function onClickCreateOpen(value: typeof viewState.createOpen) {
    viewState.createOpen = value
  }

  function onClickIssueOpen(value: typeof viewState.issueOpen) {
    viewState.issueOpen = value
    if (!value) {
      tokenIssueRequests.invalidate()
      issuing.value = false
      issuedToken.value = null
      issueReason.value = ''
    }
  }

  return { ...viewBindings, onClickCreateOpen, onClickIssueOpen }
}

export type AdminPlatformBotsPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformBotsPage>>>
