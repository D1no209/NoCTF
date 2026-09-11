import { markRaw } from 'vue'

import { Copy, KeyRound, LogIn, ShieldOff, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformDeleteUser, adminPlatformDeleteUserTokens, adminPlatformGetUser, adminPlatformIssueUserToken, adminPlatformListUsers, adminPlatformListUserTokens, adminPlatformPatchUser, adminPlatformPreviewUserDeletion, adminPlatformRevokeUserToken } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformAdminIssuedAccessTokenResponse, NoCtfapiEndpointsAdministrationPlatformIssuePlatformUserTokenResponse, NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionMode, NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse, NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse, NoCtfapiEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol } from '../../../../api'
import { createLatestRequestGuard } from '../../../../lib/latest-request'
import PrivateAccountPanelComponent from '../../../account/PrivateAccountPanel.vue'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse

type DeletionPreview = NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse

type ManagedAccountStatus = NoCtfapiEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol

type EmailVerificationDraft = 'Verified' | 'Unverified'

type TokenIntent = 'issue' | 'impersonate'

type IssuedToken = NoCtfapiEndpointsAdministrationPlatformIssuePlatformUserTokenResponse

type ActiveIssuedToken = NoCtfapiEndpointsAdministrationPlatformAdminIssuedAccessTokenResponse

/** Owns state, effects and commands for AdminPlatformUsersPage. */
export function useAdminPlatformUsersPage() {
  const { user: currentUser, startImpersonation } = useAuth()
  const route = useRoute()

  const users = ref<PlatformUser[]>([])

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const search = ref('')

  const roleFilter = ref('all')

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

  const filteredUsers = computed(() => {
    const keyword = search.value.trim().toLowerCase()
    return users.value.filter((user) => {
      if (roleFilter.value !== 'all' && user.role !== roleFilter.value) return false
      if (!keyword) return true
      return (user.userName ?? '').toLowerCase().includes(keyword)
        || (user.email ?? '').toLowerCase().includes(keyword)
    })
  })

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminPlatformListUsers()
    loading.value = false
    if (error) {
      loadError.value = parseApiError(error).message
      return
    }
    users.value = data?.items ?? []
  }

  const detailOpen = ref(false)

  const detailLoading = ref(false)

  const detail = ref<PlatformUser | null>(null)

  const pendingRole = ref('User')

  const pendingAccountStatus = ref<ManagedAccountStatus>('Active')

  const pendingEmailVerification = ref<EmailVerificationDraft>('Unverified')

  const roleSaving = ref(false)

  const accountStatusSaving = ref(false)

  const emailVerificationSaving = ref(false)

  const invalidating = ref(false)

  const activeTokens = ref<ActiveIssuedToken[]>([])

  const activeTokensLoading = ref(false)

  const revokingTokenId = ref<string | null>(null)

  const tokenOpen = ref(false)

  const tokenIntent = ref<TokenIntent>('issue')

  const tokenIssuing = ref(false)

  const tokenExpiresInSeconds = ref(3600)

  const tokenReason = ref('')

  const issuedToken = ref<IssuedToken | null>(null)

  const detailRequests = createLatestRequestGuard()

  const tokenIssueRequests = createLatestRequestGuard()

  const tokenListRequests = createLatestRequestGuard()

  function clearIssuedTokens(): void {
    tokenListRequests.invalidate()
    activeTokensLoading.value = false
    activeTokens.value = []
  }

  watch(detailOpen, (open) => {
    if (open) return
    detailRequests.invalidate()
    detailLoading.value = false
    clearIssuedTokens()
  })

  async function openDetail(user: PlatformUser): Promise<void> {
    if (!user.id) return
    const request = detailRequests.begin()
    detailOpen.value = true
    detailLoading.value = true
    detail.value = null
    clearIssuedTokens()
    const { data, error } = await adminPlatformGetUser({ path: { userId: user.id } })
    if (!detailRequests.isCurrent(request)) return
    detailLoading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      detailOpen.value = false
      return
    }
    detail.value = data?.user ?? null
    pendingRole.value = data?.user?.role ?? 'User'
    if (data?.user?.accountStatus === 'Active' || data?.user?.accountStatus === 'Banned' || data?.user?.accountStatus === 'Disabled') {
      pendingAccountStatus.value = data.user.accountStatus
    }
    pendingEmailVerification.value = data?.user?.emailVerified ? 'Verified' : 'Unverified'
    await loadIssuedTokens()
  }

  async function loadIssuedTokens(): Promise<void> {
    const targetUserId = detail.value?.id
    if (!targetUserId) {
      clearIssuedTokens()
      return
    }
    const request = tokenListRequests.begin()
    activeTokensLoading.value = true
    const { data, error } = await adminPlatformListUserTokens({
      path: { userId: targetUserId },
    })
    if (!tokenListRequests.isCurrent(request) || detail.value?.id !== targetUserId) return
    activeTokensLoading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    activeTokens.value = data?.items ?? []
  }

  function openToken(target: PlatformUser, intent: TokenIntent): void {
    if (!target.id || target.accountStatus !== 'Active') return
    tokenIssueRequests.invalidate()
    detail.value = target
    tokenIntent.value = intent
    tokenExpiresInSeconds.value = 3600
    tokenReason.value = ''
    issuedToken.value = null
    tokenOpen.value = true
  }

  function setTokenOpen(open: boolean): void {
    tokenOpen.value = open
    if (open) return
    tokenIssueRequests.invalidate()
    tokenIssuing.value = false
    issuedToken.value = null
    tokenReason.value = ''
  }

  async function issueToken(): Promise<void> {
    const target = detail.value
    const reason = tokenReason.value.trim()
    if (tokenIssuing.value || !target?.id || reason.length < 3) return
    const request = tokenIssueRequests.begin()
    const intent = tokenIntent.value
    tokenIssuing.value = true
    const { data, error } = await adminPlatformIssueUserToken({
      path: { userId: target.id },
      body: {
        expiresInSeconds: tokenExpiresInSeconds.value,
        reason,
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
    void loadIssuedTokens()
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

  async function revokeIssuedToken(token: ActiveIssuedToken): Promise<void> {
    if (revokingTokenId.value || !detail.value?.id || !token.jwtId) return
    revokingTokenId.value = token.jwtId
    const { error } = await adminPlatformRevokeUserToken({
      path: { userId: detail.value.id, jwtId: token.jwtId },
    })
    revokingTokenId.value = null
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    tokenListRequests.invalidate()
    activeTokens.value = activeTokens.value.filter(item => item.jwtId !== token.jwtId)
    toast.success(translate("ui.issuedTokenRevoked"))
  }

  async function saveRole(): Promise<void> {
    if (!detail.value?.id) return
    roleSaving.value = true
    const { data, error, response } = await adminPlatformPatchUser({
      path: { userId: detail.value.id },
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
    detail.value = data?.user ?? detail.value
    clearIssuedTokens()
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
    accountStatusSaving.value = true
    const { data, error } = await adminPlatformPatchUser({
      path: { userId: detail.value.id },
      body: { accountStatus: pendingAccountStatus.value },
    })
    accountStatusSaving.value = false
    if (error) {
      const apiError = parseApiError(error)
      toast.error(accountStatusConflictMessage(apiError.code) ?? apiError.message)
      return
    }

    if (data?.user) {
      detail.value = data.user
      const index = users.value.findIndex(user => user.id === data.user?.id)
      if (index >= 0) users.value.splice(index, 1, data.user)
    }
    clearIssuedTokens()
    toast.success(pendingAccountStatus.value === 'Active'
      ? translate("ui.accountActivated")
      : translate("ui.accountStatusUpdatedTheUserSExistingSessionsHaveBeen"))
  }

  async function saveEmailVerification(): Promise<void> {
    if (!detail.value?.id || detail.value.accountStatus === 'Anonymized') return
    emailVerificationSaving.value = true
    const { data, error } = await adminPlatformPatchUser({
      path: { userId: detail.value.id },
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
      detail.value = data.user
      const index = users.value.findIndex(user => user.id === data.user?.id)
      if (index >= 0) users.value.splice(index, 1, data.user)
    }
    clearIssuedTokens()
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
    clearIssuedTokens()
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
  })

  const PrivateAccountPanel = markRaw(PrivateAccountPanelComponent)

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  return {
      KeyRound,
      Copy,
      LogIn,
      ShieldOff,
      Trash2,
      currentUser,
      loading,
      loadError,
      search,
      roleFilter,
      ROLE_LABELS,
      STATUS_LABELS,
      MANAGED_ACCOUNT_STATUS_OPTIONS,
      REFERENCE_LABELS,
      filteredUsers,
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
      activeTokens,
      activeTokensLoading,
      revokingTokenId,
      tokenOpen,
      tokenIntent,
      tokenIssuing,
      tokenExpiresInSeconds,
      tokenReason,
      issuedToken,
      openToken,
      setTokenOpen,
      issueToken,
      copyIssuedToken,
      revokeIssuedToken,
      openDetail,
      saveRole,
      saveAccountStatus,
      saveEmailVerification,
      invalidateTokens,
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
