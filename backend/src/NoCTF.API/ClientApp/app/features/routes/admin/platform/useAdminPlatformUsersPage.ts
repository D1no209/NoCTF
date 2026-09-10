import { markRaw } from 'vue'

import { KeyRound, Trash2 } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformDeleteUser, adminPlatformGetUser, adminPlatformInvalidateUserTokens, adminPlatformListUsers, adminPlatformPreviewUserDeletion, adminPlatformUpdateUserAccountStatus, adminPlatformUpdateUserEmailVerification, adminPlatformUpdateUserRole } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionMode, NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse, NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse, NoCtfapiEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol, NoCtfapiEndpointsAdministrationPlatformUpdatePlatformUserAccountStatusConflictCode } from '../../../../api'
import { createLatestRequestGuard } from '../../../../lib/latest-request'
import PrivateAccountPanelComponent from '../../../account/PrivateAccountPanel.vue'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type PlatformUser = NoCtfapiEndpointsAdministrationPlatformPlatformUserResponse

type DeletionPreview = NoCtfapiEndpointsAdministrationPlatformPlatformUserDeletionPreviewResponse

type ManagedAccountStatus = NoCtfapiEndpointsAdministrationPlatformPlatformManagedUserAccountStatusProtocol

type AccountStatusConflictCode = NoCtfapiEndpointsAdministrationPlatformUpdatePlatformUserAccountStatusConflictCode

type EmailVerificationDraft = 'Verified' | 'Unverified'

/** Owns state, effects and commands for AdminPlatformUsersPage. */
export function useAdminPlatformUsersPage() {
  const { user: currentUser } = useAuth()

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

  const detailRequests = createLatestRequestGuard()

  watch(detailOpen, (open) => {
    if (open) return
    detailRequests.invalidate()
    detailLoading.value = false
  })

  async function openDetail(user: PlatformUser): Promise<void> {
    if (!user.id) return
    const request = detailRequests.begin()
    detailOpen.value = true
    detailLoading.value = true
    detail.value = null
    const { data, error } = await adminPlatformGetUser({ path: { userId: user.id } })
    if (!detailRequests.isCurrent(request)) return
    detailLoading.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      detailOpen.value = false
      return
    }
    detail.value = data ?? null
    pendingRole.value = data?.role ?? 'User'
    if (data?.accountStatus === 'Active' || data?.accountStatus === 'Banned' || data?.accountStatus === 'Disabled') {
      pendingAccountStatus.value = data.accountStatus
    }
    pendingEmailVerification.value = data?.emailVerified ? 'Verified' : 'Unverified'
  }

  async function saveRole(): Promise<void> {
    if (!detail.value?.id) return
    roleSaving.value = true
    const { data, error, response } = await adminPlatformUpdateUserRole({
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
    detail.value = data ?? detail.value
    toast.success(translate("ui.roleUpdated"))
    await load()
  }

  function accountStatusConflictMessage(code: string | undefined): string | null {
    switch (code as AccountStatusConflictCode | undefined) {
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
    const { data, error } = await adminPlatformUpdateUserAccountStatus({
      path: { userId: detail.value.id },
      body: { accountStatus: pendingAccountStatus.value },
    })
    accountStatusSaving.value = false
    if (error) {
      const apiError = parseApiError(error)
      toast.error(accountStatusConflictMessage(apiError.code) ?? apiError.message)
      return
    }

    if (data) {
      detail.value = data
      const index = users.value.findIndex(user => user.id === data.id)
      if (index >= 0) users.value.splice(index, 1, data)
    }
    toast.success(pendingAccountStatus.value === 'Active'
      ? translate("ui.accountActivated")
      : translate("ui.accountStatusUpdatedTheUserSExistingSessionsHaveBeen"))
  }

  async function saveEmailVerification(): Promise<void> {
    if (!detail.value?.id || detail.value.accountStatus === 'Anonymized') return
    emailVerificationSaving.value = true
    const { data, error } = await adminPlatformUpdateUserEmailVerification({
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

    if (data) {
      detail.value = data
      const index = users.value.findIndex(user => user.id === data.id)
      if (index >= 0) users.value.splice(index, 1, data)
    }
    toast.success(pendingEmailVerification.value === 'Verified'
      ? translate("ui.emailMarkedAsVerifiedByAnAdministrator")
      : translate("ui.emailVerificationStatusRevoked"))
  }

  async function invalidateTokens(): Promise<void> {
    if (!detail.value?.id) return
    invalidating.value = true
    const { error } = await adminPlatformInvalidateUserTokens({ path: { userId: detail.value.id } })
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
  })

  const PrivateAccountPanel = markRaw(PrivateAccountPanelComponent)

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  return {
      KeyRound,
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
