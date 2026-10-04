
import { ResponseMetadata } from '../../../../lib/api'

import { api, RequestPolicyOption } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { markRaw } from 'vue'
import { useAdminDetailRoute } from '~/features/admin/useAdminDetailRoute'

import { Copy, KeyRound, LogIn, Plus, Trash2, Unlink } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationPlatformIssuePlatformUserTokenResponse, NoCTFAPIEndpointsAdministrationPlatformPlatformUserDeletionMode, NoCTFAPIEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse, NoCTFAPIEndpointsAdministrationPlatformPlatformUserResponse, NoCTFAPIEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol, NoCTFAPIEndpointsAdministrationPlatformSsoProviderResponse, NoCTFAPIEndpointsAuthenticationUserKindProtocol, NoCTFAPIEndpointsAuthenticationUserRoleProtocol } from '../../../../api/models'
import { createLatestRequestGuard } from '../../../../lib/latest-request'
import PrivateAccountPanelComponent from '../../../account/PrivateAccountPanel.vue'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'
import { useOffsetPagination } from '../../../../composables/useOffsetPagination'

type PlatformUser = NoCTFAPIEndpointsAdministrationPlatformPlatformUserResponse

type DeletionPreview = NoCTFAPIEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse

type ManagedAccountStatus = NoCTFAPIEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol

type EmailVerificationDraft = 'Verified' | 'Unverified'

type TokenIntent = 'issue' | 'impersonate'

type IssuedToken = NoCTFAPIEndpointsAdministrationPlatformIssuePlatformUserTokenResponse

/** Owns state, effects and commands for AdminPlatformUsersPage. */
export function useAdminPlatformUsersPage() {
  const { user: currentUser, impersonation, startImpersonation, invalidate } = useAuth()
  const route = useRoute()

  const users = ref<PlatformUser[]>([])

  const loadError = ref<UiMessage | null>(null)

  const search = ref('')

  const roleFilter = ref<'all' | 'Bot' | NoCTFAPIEndpointsAuthenticationUserRoleProtocol>(
    route.query.filter === 'Bot' ? 'Bot' : 'all',
  )

  const ssoProviders = ref<NoCTFAPIEndpointsAdministrationPlatformSsoProviderResponse[]>([])

  const ssoProviderFilter = ref('all')

  const ROLE_LABELS: Record<string, string> = { User: "administration.label.user", Organizer: "administration.label.organizer", Administrator: "administration.label.administrator" }

  const STATUS_LABELS: Record<string, string> = { Active: "administration.label.normal", Banned: "common.label.banned", Disabled: "administration.label.disabled", Anonymized: "common.label.anonymous" }

  const MANAGED_ACCOUNT_STATUS_OPTIONS: ReadonlyArray<{ value: ManagedAccountStatus, label: string }> = [
    { value: 'Active', label: "administration.label.normal" },
    { value: 'Banned', label: "common.label.banned" },
    { value: 'Disabled', label: "administration.label.disabled" },
  ]

  const REFERENCE_LABELS: Record<string, string> = {
    CompetitionOwner: "common.label.competitionLeader",
    CompetitionCollaborator: "common.label.competitionCollaborator",
    ChallengeOwner: "common.platformUsers.description.questionBankTemplatePerson",
    ChallengeManager: "common.label.questionBankTemplateManager",
    TeamCaptain: "common.label.teamCaptain",
    TeamMember: "common.label.teamMembers",
    Submission: "common.label.submitRecord",
    PatchUpload: "common.label.patchUpload.platformUsersPage",
    Notification: "common.label.notifications",
    GameplayFact: "common.label.gameplayFacts",
    CompetitionLifecycleAudit: "common.label.competitionLifeCycleAudit",
    CompetitionQuestion: "common.label.competitionQuestions",
    CompetitionQuestionEntry: "common.label.qReply",
    CompetitionEvent: "administration.label.competitionEvent",
    UserAccountLifecycleAudit: "account.label.accountLifeCycleAudit",
  }

  const pagination = useOffsetPagination<PlatformUser>(async ({ offset, limit, desc }) => {
    const query = {
      keyword: search.value.trim() || undefined,
      kind: roleFilter.value === 'Bot' ? 'Bot' as NoCTFAPIEndpointsAuthenticationUserKindProtocol : undefined,
      role: roleFilter.value !== 'all' && roleFilter.value !== 'Bot' ? roleFilter.value : undefined,
      ssoProviderId: ssoProviderFilter.value === 'all' ? undefined : ssoProviderFilter.value,
      offset,
      limit,
      desc,
    }
    let error: unknown;
    const data = await api.api.v1.admin.platform.users.get({ queryParameters: query }).catch(cause => { error = cause; return undefined });
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
    const data = await api.api.v1.admin.platform.sso.get();
    ssoProviders.value = data?.providers ?? []
  }

  const createBotOpen = ref(false)

  const creatingBot = ref(false)

  const botName = ref('')

  const botRole = ref<NoCTFAPIEndpointsAuthenticationUserRoleProtocol>('User')

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
    let error: unknown;
    await api.api.v1.admin.platform.bots.post({ userName: botName.value.trim(), role: botRole.value }).catch(cause => { error = cause; return undefined });
    creatingBot.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    createBotOpen.value = false
    roleFilter.value = 'Bot'
    toast.success(describeMessage("administration.label.botCreated"))
    await load()
  }

  const selection = useAdminDetailRoute<PlatformUser>('userId', '/admin/platform/users', async (userId, signal) => {
    let error: unknown;
    const data = await api.api.v1.admin.platform.users.byUserId(userId).get({ options: [new RequestPolicyOption({ signal: signal })] }).catch(cause => { error = cause; return undefined });
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.users.byUserId(target.id).tokens.post({
        expiresInSeconds: tokenExpiresInSeconds.value,
      }).catch(cause => { error = cause; return undefined });
    if (!tokenIssueRequests.isCurrent(request) || !tokenOpen.value) return
    tokenIssuing.value = false
    if (error || !data?.accessToken || !data.expiresAt || !data.targetUserId || !data.targetUserName) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    if (intent === 'impersonate') {
      setTokenOpen(false)
      detailOpen.value = false
      try {
        await startImpersonation({
          accessToken: data.accessToken,
          expiresAt: data.expiresAt.toISOString(),
          targetUserId: data.targetUserId,
          targetUserName: data.targetUserName,
          returnPath: route.fullPath,
        })
      }
      catch (impersonationError) {
        toast.error(parseApiError(impersonationError).displayMessage)
      }
      return
    }
    issuedToken.value = data
  }

  async function copyIssuedToken(): Promise<void> {
    if (!issuedToken.value?.accessToken) return
    try {
      await navigator.clipboard.writeText(issuedToken.value.accessToken)
      toast.success(describeMessage("common.label.copiedClipboard"))
    }
    catch {
      toast.error(describeMessage("common.kohPanel.error.copyManuallySelectFailed"))
    }
  }

  async function unbindManagedSsoIdentity(): Promise<void> {
    const target = detail.value
    if (!target?.id || !target.ssoBinding || ssoUnbinding.value) return
    ssoUnbinding.value = true
    let error: unknown;
    await api.api.v1.admin.platform.users.byUserId(target.id).ssoBinding.delete().catch(cause => { error = cause; return undefined });
    ssoUnbinding.value = false
    if (error) {
      toast.error(parseApiError(error, describeMessage('sso.adminUnbindFailed')).displayMessage)
      return
    }
    toast.success(describeMessage('sso.adminUnbindSuccessful'))
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
    let error: unknown;
    const response = new ResponseMetadata();
    const data = await api.api.v1.admin.platform.users.byUserId(targetId).patch({ role: pendingRole.value as 'User' | 'Organizer' | 'Administrator' }, { options: [new RequestPolicyOption({ response: response })] }).catch(cause => { error = cause; return undefined });
    roleSaving.value = false
    if (error) {
      if (response?.status === 409) {
        toast.error(describeMessage("administration.platformUsers.description.userStillOwnerAdministrator"))
      }
      else {
        toast.error(parseApiError(error).displayMessage)
      }
      return
    }
    if (selection.selectedId.value === targetId) detail.value = data?.user ?? detail.value
    toast.success(describeMessage("administration.label.roleUpdated"))
    await load()
  }

  function accountStatusConflictMessage(code: string | null | undefined): string | null {
    switch (code) {
      case 'LastAdministratorProtected':
        return translate("administration.platformUsers.validation.lastActiveFormat")
      case 'AnonymizedAccountImmutable':
        return translate("administration.platformUsers.validation.anonymizedAccountFormat")
      default:
        return null
    }
  }

  async function saveAccountStatus(): Promise<void> {
    if (!detail.value?.id || detail.value.accountStatus === 'Anonymized') return
    const targetId = detail.value.id
    accountStatusSaving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.users.byUserId(targetId).patch({ accountStatus: pendingAccountStatus.value }).catch(cause => { error = cause; return undefined });
    accountStatusSaving.value = false
    if (error) {
      const apiError = parseApiError(error)
      toast.error(accountStatusConflictMessage(apiError.code) ?? apiError.displayMessage)
      return
    }

    if (data?.user) {
      if (selection.selectedId.value === targetId) detail.value = data.user
      const index = users.value.findIndex(user => user.id === data.user?.id)
      if (index >= 0) users.value.splice(index, 1, data.user)
    }
    toast.success(pendingAccountStatus.value === 'Active'
      ? translate("administration.label.accountActivated")
      : translate("administration.platformUsers.description.accountStatusUpdatedUser"))
  }

  async function saveEmailVerification(): Promise<void> {
    if (!detail.value?.id || detail.value.accountStatus === 'Anonymized') return
    const targetId = detail.value.id
    emailVerificationSaving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.users.byUserId(targetId).patch({ emailVerified: pendingEmailVerification.value === 'Verified' }).catch(cause => { error = cause; return undefined });
    emailVerificationSaving.value = false
    if (error) {
      const apiError = parseApiError(error)
      toast.error(apiError.code === 'AnonymizedAccountImmutable'
        ? translate("administration.platformUsers.validation.anonymizedAccountFormat.platformUsersPage")
        : apiError.displayMessage)
      return
    }

    if (data?.user) {
      if (selection.selectedId.value === targetId) detail.value = data.user
      const index = users.value.findIndex(user => user.id === data.user?.id)
      if (index >= 0) users.value.splice(index, 1, data.user)
    }
    toast.success(pendingEmailVerification.value === 'Verified'
      ? translate("administration.platformUsers.description.emailMarkedVerifiedAdministrator")
      : translate("administration.label.emailVerificationStatusRevoked"))
  }

  async function invalidateTokens(): Promise<void> {
    if (invalidating.value || !detail.value?.id) return
    invalidating.value = true
    let error: unknown;
    await api.api.v1.admin.platform.users.byUserId(detail.value.id).tokens.delete().catch(cause => { error = cause; return undefined });
    invalidating.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    toast.success(describeMessage("administration.platformUsers.description.tokensUserRevoked"))
  }

  const deleteOpen = ref(false)

  const previewLoading = ref(false)

  const preview = ref<DeletionPreview | null>(null)

  const deletionMode = ref<NoCTFAPIEndpointsAdministrationPlatformPlatformUserDeletionMode>('Anonymize')

  const deletionReason = ref('')

  const deleting = ref(false)

  async function startDelete(): Promise<void> {
    if (!detail.value?.id) return
    deleteOpen.value = true
    previewLoading.value = true
    preview.value = null
    deletionReason.value = ''
    let error: unknown;
    const data = await api.api.v1.admin.platform.users.byUserId(detail.value.id).deletionPreview.get().catch(cause => { error = cause; return undefined });
    previewLoading.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      deleteOpen.value = false
      return
    }
    preview.value = data ?? null
    deletionMode.value = data?.canAnonymize ? 'Anonymize' : 'HardDelete'
  }

  function deletionConflictMessage(error: unknown): UiMessage {
    const code = parseApiError(error).code
    switch (code) {
      case 'SelfDeletionForbidden':
        return translate("administration.validation.deleteOwnFormat")
      case 'LastAdministratorProtected':
        return translate("administration.validation.deleteLastFormat")
      case 'HardDeleteBlocked':
        return translate("administration.platformUsers.validation.userBusinessFormat")
      case 'AlreadyAnonymized':
        return translate("administration.platformUsers.label.userAnonymized")
      default:
        return parseApiError(error).displayMessage
    }
  }

  async function confirmDelete(): Promise<void> {
    if (!detail.value?.id || !deletionReason.value.trim()) return
    deleting.value = true
    let error: unknown;
    await api.api.v1.admin.platform.users.byUserId(detail.value.id).delete({ mode: deletionMode.value, reason: deletionReason.value.trim() }).catch(cause => { error = cause; return undefined });
    deleting.value = false
    if (error) {
      toast.error(deletionConflictMessage(error))
      return
    }
    toast.success(deletionMode.value === 'HardDelete' ? translate("administration.platformUsers.label.userPhysicallyDeleted") : translate("administration.label.userAnonymized"))
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
