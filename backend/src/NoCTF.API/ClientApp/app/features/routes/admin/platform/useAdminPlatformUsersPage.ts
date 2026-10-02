import { markRaw } from 'vue'
import { useAdminDetailRoute } from '~/features/admin/useAdminDetailRoute'

import { Copy, KeyRound, LogIn, Plus, Trash2, Unlink } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformCreateBot, adminPlatformDeleteUser, adminPlatformDeleteUserTokens, adminPlatformGetUser, adminPlatformIssueUserToken, adminPlatformListUsers, adminPlatformPatchUser, adminPlatformPreviewUserDeletion, adminPlatformSsoGetConfiguration, adminPlatformUnbindSsoIdentity } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformIssuePlatformUserTokenResponse, NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionMode, NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse, NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse, NoCtfapiEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol, NoCtfapiEndpointsAdministrationPlatformSsoProviderResponse, NoCtfapiEndpointsAuthenticationUserKindProtocol, NoCtfapiEndpointsAuthenticationUserRoleProtocol } from '../../../../api'
import { createLatestRequestGuard } from '../../../../lib/latest-request'
import PrivateAccountPanelComponent from '../../../account/PrivateAccountPanel.vue'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'

type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse

type DeletionPreview = NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse

type ManagedAccountStatus = NoCtfapiEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol

type EmailVerificationDraft = 'Verified' | 'Unverified'

type TokenIntent = 'issue' | 'impersonate'

type IssuedToken = NoCtfapiEndpointsAdministrationPlatformIssuePlatformUserTokenResponse

/** Owns state, effects and commands for AdminPlatformUsersPage. */
export function useAdminPlatformUsersPage() {
  const { user: currentUser, impersonation, startImpersonation, invalidate } = useAuth()
  const route = useRoute()

  const users = ref<PlatformUser[]>([])

  const loadError = ref<string | null>(null)

  const search = ref('')

  const roleFilter = ref<'all' | 'Bot' | NoCtfapiEndpointsAuthenticationUserRoleProtocol>(
    route.query.filter === 'Bot' ? 'Bot' : 'all',
  )

  const ssoProviders = ref<NoCtfapiEndpointsAdministrationPlatformSsoProviderResponse[]>([])

  const ssoProviderFilter = ref('all')

  const ROLE_LABELS: Record<string, string> = { User: "ui.user", Organizer: "ui.organizer", Administrator: "ui.administrator" }

  const STATUS_LABELS: Record<string, string> = { Active: "ui.normal", Banned: "ui.banned2", Disabled: "ui.disabled", Anonymized: "ui.anonymous" }

  const MANAGED_ACCOUNT_STATUS_OPTIONS: ReadonlyArray<{ value: ManagedAccountStatus, label: string }> = [
    { value: 'Active', label: "ui.normal" },
    { value: 'Banned', label: "ui.banned2" },
    { value: 'Disabled', label: "ui.disabled" },
  ]

  const REFERENCE_LABELS: Record<string, string> = {
    CompetitionOwner: "ui.competitionLeader",
    CompetitionCollaborator: "ui.competitionCollaborator",
    ChallengeOwner: "ui.questionBankTemplatePersonInCharge",
    ChallengeManager: "ui.questionBankTemplateManager",
    TeamCaptain: "ui.teamCaptain",
    TeamMember: "ui.teamMembers",
    Submission: "ui.submitRecord",
    PatchUpload: "ui.patchUpload2",
    Notification: "ui.notifications",
    GameplayFact: "ui.gameplayFacts",
    CompetitionLifecycleAudit: "ui.competitionLifeCycleAudit",
    CompetitionQuestion: "ui.competitionQuestions",
    CompetitionQuestionEntry: "ui.qAReply",
    CompetitionEvent: "ui.competitionEvent",
    UserAccountLifecycleAudit: "ui.accountLifeCycleAudit",
  }

  const pagination = useOffsetPagination<PlatformUser>(async ({ offset, limit, desc }) => {
    const query = {
      keyword: search.value.trim() || null,
      kind: roleFilter.value === 'Bot' ? 'Bot' as NoCtfapiEndpointsAuthenticationUserKindProtocol : null,
      role: roleFilter.value !== 'all' && roleFilter.value !== 'Bot' ? roleFilter.value : null,
      ssoProviderId: ssoProviderFilter.value === 'all' ? null : ssoProviderFilter.value,
      offset,
      limit,
      desc,
    }
    const { data, error } = await adminPlatformListUsers({ query })
    if (error || !data) throw error ?? new Error('Failed to load platform users.')
    users.value = data.items ?? []
    return { items: users.value, total: data.total ?? 0 }
  }, { initialDesc: false })

  const loading = computed(() => pagination.loading.value && !pagination.initialized.value)

  const filteredUsers = computed(() => users.value)

  async function load(): Promise<void> {
    loadError.value = null
    await pagination.loadPage(pagination.page.value)
    loadError.value = pagination.error.value?.message ?? null
  }

  watch([search, roleFilter, ssoProviderFilter], () => {
    pagination.reset()
    void load()
  })

  async function loadSsoProviders(): Promise<void> {
    const { data } = await adminPlatformSsoGetConfiguration()
    ssoProviders.value = data?.providers ?? []
  }

  const createBotOpen = ref(false)

  const creatingBot = ref(false)

  const botName = ref('')

  const botRole = ref<NoCtfapiEndpointsAuthenticationUserRoleProtocol>('User')

  function openCreateBot(): void {
    botName.value = ''
    botRole.value = 'User'
    createBotOpen.value = true
  }

  function setCreateBotOpen(open: boolean): void {
    if (creatingBot.value) return
    createBotOpen.value = open
  }

  async function createBot(): Promise<void> {
    if (creatingBot.value || !botName.value.trim()) return
    creatingBot.value = true
    const { error } = await adminPlatformCreateBot({
      body: { userName: botName.value.trim(), role: botRole.value },
    })
    creatingBot.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    createBotOpen.value = false
    roleFilter.value = 'Bot'
    toast.success(translate("ui.botCreated"))
    await load()
  }

  const selection = useAdminDetailRoute<PlatformUser>('userId', '/admin/platform/users', async (userId, signal) => {
    const { data, error } = await adminPlatformGetUser({ path: { userId }, signal })
    if (error || !data?.user) throw error ?? new Error(translate('adminNavigation.notFound'))
    return data.user
  })
  const { open: detailOpen, loading: detailLoading, data: detail, error: detailError } = selection

  const pendingRole = ref('User')

  const pendingAccountStatus = ref<ManagedAccountStatus>('Active')

  const pendingEmailVerification = ref<EmailVerificationDraft>('Unverified')

  const roleSaving = ref(false)

  const accountStatusSaving = ref(false)

  const emailVerificationSaving = ref(false)

  const invalidating = ref(false)

  const ssoUnbinding = ref(false)

  const tokenOpen = ref(false)

  const tokenIntent = ref<TokenIntent>('issue')

  const identitySwitchActive = computed(() => impersonation.value !== null)

  const tokenIssuing = ref(false)

  const tokenExpiresInSeconds = ref(3600)

  const issuedToken = ref<IssuedToken | null>(null)

  const tokenIssueRequests = createLatestRequestGuard()

  watch(detail, (target) => {
    if (!target) return
    pendingRole.value = target.role ?? 'User'
    if (target.accountStatus === 'Active' || target.accountStatus === 'Banned' || target.accountStatus === 'Disabled')
      pendingAccountStatus.value = target.accountStatus
    pendingEmailVerification.value = target.emailVerified ? 'Verified' : 'Unverified'
  })

  async function openDetail(user: PlatformUser): Promise<void> {
    if (user.id) await selection.select(user.id)
  }

  function openToken(target: PlatformUser, intent: TokenIntent): void {
    if (!target.id || target.accountStatus !== 'Active'
      || intent === 'impersonate' && identitySwitchActive.value) return
    tokenIssueRequests.invalidate()
    detail.value = target
    tokenIntent.value = intent
    tokenExpiresInSeconds.value = 3600
    issuedToken.value = null
    tokenOpen.value = true
  }

  function setTokenOpen(open: boolean): void {
    tokenOpen.value = open
    if (open) return
    tokenIssueRequests.invalidate()
    tokenIssuing.value = false
    issuedToken.value = null
  }

  async function issueToken(): Promise<void> {
    const target = detail.value
    if (tokenIssuing.value || !target?.id) return
    const request = tokenIssueRequests.begin()
    const intent = tokenIntent.value
    tokenIssuing.value = true
    const { data, error } = await adminPlatformIssueUserToken({
      path: { userId: target.id },
      body: {
        expiresInSeconds: tokenExpiresInSeconds.value,
      },
    })
    if (!tokenIssueRequests.isCurrent(request) || !tokenOpen.value) return
    tokenIssuing.value = false
    if (error || !data?.accessToken || !data.expiresAt || !data.targetUserId || !data.targetUserName) {
      toast.error(parseApiError(error).message)
      return
    }
    if (intent === 'impersonate') {
      setTokenOpen(false)
      detailOpen.value = false
      try {
        await startImpersonation({
          accessToken: data.accessToken,
          expiresAt: data.expiresAt,
          targetUserId: data.targetUserId,
          targetUserName: data.targetUserName,
          returnPath: route.fullPath,
        })
      }
      catch (impersonationError) {
        toast.error(parseApiError(impersonationError).message)
      }
      return
    }
    issuedToken.value = data
  }

  async function copyIssuedToken(): Promise<void> {
    if (!issuedToken.value?.accessToken) return
    try {
      await navigator.clipboard.writeText(issuedToken.value.accessToken)
      toast.success(translate("ui.copiedToClipboard"))
    }
    catch {
      toast.error(translate("ui.copyFailedPleaseManuallySelectCopy"))
    }
  }

  async function unbindManagedSsoIdentity(): Promise<void> {
    const target = detail.value
    if (!target?.id || !target.ssoBinding || ssoUnbinding.value) return
    ssoUnbinding.value = true
    const { error } = await adminPlatformUnbindSsoIdentity({
      path: { userId: target.id },
    })
    ssoUnbinding.value = false
    if (error) {
      toast.error(parseApiError(error, translate('sso.adminUnbindFailed')).message)
      return
    }
    toast.success(translate('sso.adminUnbindSuccessful'))
    if (target.id === currentUser.value?.userId) {
      invalidate()
      detailOpen.value = false
      await navigateTo('/auth/login')
      return
    }
    if (selection.selectedId.value === target.id) detail.value = { ...target, ssoBinding: null }
    await load()
  }

  async function saveRole(): Promise<void> {
    if (!detail.value?.id) return
    const targetId = detail.value.id
    roleSaving.value = true
    const { data, error, response } = await adminPlatformPatchUser({
      path: { userId: targetId },
      body: { role: pendingRole.value as 'User' | 'Organizer' | 'Administrator' },
    })
    roleSaving.value = false
    if (error) {
      if (response?.status === 409) {
        toast.error(translate("ui.thisUserIsStillTheOwnerOrAdministratorOfThe"))
      }
      else {
        toast.error(parseApiError(error).message)
      }
      return
    }
    if (selection.selectedId.value === targetId) detail.value = data?.user ?? detail.value
    toast.success(translate("ui.roleUpdated"))
    await load()
  }

  function accountStatusConflictMessage(code: string | undefined): string | null {
    switch (code) {
      case 'LastAdministratorProtected':
        return translate("ui.theLastActiveAdministratorCannotBeDeactivated")
      case 'AnonymizedAccountImmutable':
        return translate("ui.anAnonymizedAccountCannotBeRestored")
      default:
        return null
    }
  }

  async function saveAccountStatus(): Promise<void> {
    if (!detail.value?.id || detail.value.accountStatus === 'Anonymized') return
    const targetId = detail.value.id
    accountStatusSaving.value = true
    const { data, error } = await adminPlatformPatchUser({
      path: { userId: targetId },
      body: { accountStatus: pendingAccountStatus.value },
    })
    accountStatusSaving.value = false
    if (error) {
      const apiError = parseApiError(error)
      toast.error(accountStatusConflictMessage(apiError.code) ?? apiError.message)
      return
    }

    if (data?.user) {
      if (selection.selectedId.value === targetId) detail.value = data.user
      const index = users.value.findIndex(user => user.id === data.user?.id)
      if (index >= 0) users.value.splice(index, 1, data.user)
    }
    toast.success(pendingAccountStatus.value === 'Active'
      ? translate("ui.accountActivated")
      : translate("ui.accountStatusUpdatedTheUserSExistingSessionsHaveBeen"))
  }

  async function saveEmailVerification(): Promise<void> {
    if (!detail.value?.id || detail.value.accountStatus === 'Anonymized') return
    const targetId = detail.value.id
    emailVerificationSaving.value = true
    const { data, error } = await adminPlatformPatchUser({
      path: { userId: targetId },
      body: { emailVerified: pendingEmailVerification.value === 'Verified' },
    })
    emailVerificationSaving.value = false
    if (error) {
      const apiError = parseApiError(error)
      toast.error(apiError.code === 'AnonymizedAccountImmutable'
        ? translate("ui.anAnonymizedAccountCannotBeChanged")
        : apiError.message)
      return
    }

    if (data?.user) {
      if (selection.selectedId.value === targetId) detail.value = data.user
      const index = users.value.findIndex(user => user.id === data.user?.id)
      if (index >= 0) users.value.splice(index, 1, data.user)
    }
    toast.success(pendingEmailVerification.value === 'Verified'
      ? translate("ui.emailMarkedAsVerifiedByAnAdministrator")
      : translate("ui.emailVerificationStatusRevoked"))
  }

  async function invalidateTokens(): Promise<void> {
    if (invalidating.value || !detail.value?.id) return
    invalidating.value = true
    const { error } = await adminPlatformDeleteUserTokens({ path: { userId: detail.value.id } })
    invalidating.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    toast.success(translate("ui.allTokensForThisUserHaveBeenRevoked"))
  }

  const deleteOpen = ref(false)

  const previewLoading = ref(false)

  const preview = ref<DeletionPreview | null>(null)

  const deletionMode = ref<NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionMode>('Anonymize')

  const deletionReason = ref('')

  const deleting = ref(false)

  async function startDelete(): Promise<void> {
    if (!detail.value?.id) return
    deleteOpen.value = true
    previewLoading.value = true
    preview.value = null
    deletionReason.value = ''
    const { data, error } = await adminPlatformPreviewUserDeletion({ path: { userId: detail.value.id } })
    previewLoading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      deleteOpen.value = false
      return
    }
    preview.value = data ?? null
    deletionMode.value = data?.canAnonymize ? 'Anonymize' : 'HardDelete'
  }

  function deletionConflictMessage(error: unknown): string {
    const code = parseApiError(error).code
    switch (code) {
      case 'SelfDeletionForbidden':
        return translate("ui.cannotDeleteOwnAccount")
      case 'LastAdministratorProtected':
        return translate("ui.cannotDeleteLastAdministrator")
      case 'HardDeleteBlocked':
        return translate("ui.thisUserHasBusinessReferencesAndCannotBePhysicallyDeleted")
      case 'AlreadyAnonymized':
        return translate("ui.thisUserHasBeenAnonymized")
      default:
        return parseApiError(error).message
    }
  }

  async function confirmDelete(): Promise<void> {
    if (!detail.value?.id || !deletionReason.value.trim()) return
    deleting.value = true
    const { error } = await adminPlatformDeleteUser({
      path: { userId: detail.value.id },
      body: { mode: deletionMode.value, reason: deletionReason.value.trim() },
    })
    deleting.value = false
    if (error) {
      toast.error(deletionConflictMessage(error))
      return
    }
    toast.success(deletionMode.value === 'HardDelete' ? translate("ui.userHasBeenPhysicallyDeleted") : translate("ui.userHasBeenAnonymized"))
    deleteOpen.value = false
    detailOpen.value = false
    await load()
  }

  onMounted(() => {
    void load()
    void loadSsoProviders()
  })

  const PrivateAccountPanel = markRaw(PrivateAccountPanelComponent)

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  return {
      KeyRound,
      Copy,
      LogIn,
      Plus,
      Trash2,
      Unlink,
      currentUser,
      loading,
      loadError,
      search,
      roleFilter,
      ssoProviders,
      ssoProviderFilter,
      ROLE_LABELS,
      STATUS_LABELS,
      MANAGED_ACCOUNT_STATUS_OPTIONS,
      REFERENCE_LABELS,
      filteredUsers,
      page: pagination.page,
      pageCount: pagination.pageCount,
      total: pagination.total,
      pageLimit: pagination.limit,
      pageLoading: pagination.loading,
      loadPage: pagination.loadPage,
      setPageSize: pagination.setPageSize,
      createBotOpen,
      creatingBot,
      botName,
      botRole,
      openCreateBot,
      setCreateBotOpen,
      createBot,
      detailError,
      detailOpen,
      detailLoading,
      detail,
      pendingRole,
      pendingAccountStatus,
      pendingEmailVerification,
      roleSaving,
      accountStatusSaving,
      emailVerificationSaving,
      invalidating,
      ssoUnbinding,
      tokenOpen,
      tokenIntent,
      identitySwitchActive,
      tokenIssuing,
      tokenExpiresInSeconds,
      issuedToken,
      openToken,
      setTokenOpen,
      issueToken,
      copyIssuedToken,
      openDetail,
      saveRole,
      saveAccountStatus,
      saveEmailVerification,
      invalidateTokens,
      unbindManagedSsoIdentity,
      deleteOpen,
      previewLoading,
      preview,
      deletionMode,
      deletionReason,
      deleting,
      startDelete,
      confirmDelete,
      PrivateAccountPanel,
      AdminDateTime
    }
}

export type AdminPlatformUsersPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformUsersPage>>>
