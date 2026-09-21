import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { Image as ImageIcon, LockKeyhole, LogOut, ShieldCheck, UserRound } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  authenticationGetMyProfile,
  authenticationPatchMyProfile,
  authenticationUploadMyAvatar,
  authenticationUploadMyWallpaper,
  changePasswordEndpoint,
  resendEmailVerificationEndpoint,
  authenticationSsoBeginBinding,
  authenticationSsoGetMyBinding,
  authenticationSsoUnbindIdentity,
} from '../../api'
import type { NoCtfapiEndpointsAuthenticationMySsoBindingConfigurationResponse } from '../../api'
import AvatarCropDialogComponent from './AvatarCropDialog.vue'
import AdminDateTimeComponent from '../admin/AdminDateTime.vue'
import { exceedsUploadLimit } from './upload-limits'
import { runDownRevealTransition } from '../../motion/reveal-transition'

export type AccountPanelSection = 'profile' | 'identity' | 'wallpaper' | 'security'

/** Owns the compact account popover, drafts and account commands across route changes. */
export function useAccountPanel() {
  const route = useRoute()
  const { user, fetchMe, logout, logoutAll, invalidate, impersonation } = useAuth()
  const isImpersonating = computed(() => impersonation.value !== null)
  const { configuration: platformConfiguration } = usePlatform()
  const maximumAvatarBytes = computed(() =>
    platformConfiguration.value?.imageUploadLimits?.maximumAvatarBytes ?? null)
  const maximumWallpaperBytes = computed(() =>
    platformConfiguration.value?.imageUploadLimits?.maximumWallpaperBytes ?? null)
  const avatarRequirements = computed(() => maximumAvatarBytes.value
    ? translate('accountPanel.avatarRequirements', { limit: formatBytes(maximumAvatarBytes.value) })
    : translate('ui.pleaseSelectAJpegPngOrWebpImageNoLarger'))
  const wallpaperRequirements = computed(() => maximumWallpaperBytes.value
    ? translate('accountPanel.wallpaperRequirements', { limit: formatBytes(maximumWallpaperBytes.value) })
    : translate('accountPanel.wallpaperRequirementsFallback'))

  const open = ref(false)
  const activeSection = ref<AccountPanelSection | null>(null)

  function uploadTooLarge(maximumBytes: number | null): string {
    return maximumBytes
      ? translate('accountPanel.fileExceedsUploadLimit', { limit: formatBytes(maximumBytes) })
      : translate('ui.theUploadedFileIsTooLarge')
  }

  function imageUploadError(error: unknown, maximumBytes: number | null): string {
    const parsed = parseApiError(error)
    if (parsed.code === 'UploadTooLarge')
      return uploadTooLarge(maximumBytes)
    if (parsed.code === 'SizeInvalid'
      || parsed.code === 'SourceMetadataMismatch'
      || parsed.code === 'UnsupportedFormat'
      || parsed.code === 'InvalidDimensions'
      || parsed.code === 'PixelLimitExceeded'
      || parsed.code === 'MultipleFrames'
      || parsed.code === 'MalformedImage') {
      return translate('ui.thisImageCannotBeReadPleaseUseJpegPngOr')
    }
    return parsed.message
  }

  function setOpen(value: boolean) {
    open.value = value
  }

  function selectSection(section: AccountPanelSection) {
    activeSection.value = activeSection.value === section ? null : section
    if ((section === 'profile' || section === 'identity' || section === 'wallpaper')
      && !identityLoaded.value && !identityLoading.value)
      void loadIdentity()
    if (section === 'security' && !ssoLoaded.value && !ssoLoading.value)
      void loadSsoBinding()
  }

  const description = ref('')
  const savedDescription = ref(description.value)
  const profilePending = ref(false)
  const profileError = ref<string | null>(null)
  const profileSuccess = ref(false)
  const profileDirty = computed(() => description.value !== savedDescription.value)

  async function saveProfile() {
    if (profilePending.value || !profileDirty.value) return
    profilePending.value = true
    profileError.value = null
    profileSuccess.value = false
    const draft = description.value
    try {
      const { data, error } = await authenticationPatchMyProfile({
        body: { profile: { description: draft || null } },
      })
      if (error || !data) throw error
      savedDescription.value = draft
      profileSuccess.value = true
      toast.success(translate('ui.dataSaved'))
    }
    catch (error) {
      profileError.value = parseApiError(error).message
    }
    finally {
      profilePending.value = false
    }
  }

  const avatarInput = ref<HTMLInputElement | null>(null)
  const avatarPending = ref(false)
  const avatarEditorOpen = ref(false)
  const avatarSourceFile = ref<File | null>(null)

  function selectAvatar(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0] ?? null
    input.value = ''
    if (!file) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      toast.error(translate('ui.pleaseSelectAJpegPngOrWebpImageNoLarger'))
      return
    }
    avatarSourceFile.value = file
    avatarEditorOpen.value = true
  }

  function setAvatarEditorOpen(value: boolean) {
    if (avatarPending.value) return
    avatarEditorOpen.value = value
    if (!value) avatarSourceFile.value = null
  }

  async function uploadAvatar(file: File) {
    if (exceedsUploadLimit(file.size, maximumAvatarBytes.value)) {
      toast.error(uploadTooLarge(maximumAvatarBytes.value))
      return
    }
    avatarPending.value = true
    try {
      const { error } = await authenticationUploadMyAvatar({ body: { file } })
      if (error) throw error
      await fetchMe()
      avatarEditorOpen.value = false
      avatarSourceFile.value = null
      toast.success(translate('ui.avatarHasBeenUpdated'))
    }
    catch (error) {
      toast.error(imageUploadError(error, maximumAvatarBytes.value))
    }
    finally {
      avatarPending.value = false
    }
  }

  function reportAvatarError(error: Error) {
    toast.error(error.message)
  }

  const wallpaperInput = ref<HTMLInputElement | null>(null)
  const wallpaperPending = ref(false)
  const { wallpaperUrl, refreshWallpaper } = usePersonalWallpaper()

  async function selectWallpaper(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0] ?? null
    input.value = ''
    if (!file) return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      toast.error(translate('accountPanel.wallpaperFileInvalid'))
      return
    }
    if (exceedsUploadLimit(file.size, maximumWallpaperBytes.value)) {
      toast.error(uploadTooLarge(maximumWallpaperBytes.value))
      return
    }

    wallpaperPending.value = true
    try {
      const { data, error } = await authenticationUploadMyWallpaper({ body: { file } })
      if (error || !data) throw error
      user.value = data
      await refreshWallpaper(true)
      toast.success(translate('accountPanel.wallpaperUpdated'))
    }
    catch (error) {
      toast.error(imageUploadError(error, maximumWallpaperBytes.value))
    }
    finally {
      wallpaperPending.value = false
    }
  }

  async function setWallpaperEnabled(enabled: boolean) {
    if (wallpaperPending.value || enabled === Boolean(user.value?.wallpaperEnabled)) return
    wallpaperPending.value = true
    try {
      const { data, error } = await authenticationPatchMyProfile({
        body: { appearance: { wallpaperEnabled: enabled } },
      })
      if (error || !data) throw error
      const nextEnabled = data.appearance?.wallpaperEnabled ?? enabled
      await runDownRevealTransition('wallpaper', () => {
        if (user.value)
          user.value = { ...user.value, wallpaperEnabled: nextEnabled }
      })
      toast.success(translate(enabled
        ? 'accountPanel.wallpaperEnabled'
        : 'accountPanel.wallpaperDisabled'))
    }
    catch (error) {
      toast.error(parseApiError(error).message)
    }
    finally {
      wallpaperPending.value = false
    }
  }

  const fullName = ref('')
  const studentNumber = ref('')
  const savedIdentity = ref({ fullName: '', studentNumber: '' })
  const identityLoading = ref(false)
  const identityLoaded = ref(false)
  const identityPending = ref(false)
  const identityError = ref<string | null>(null)
  const identitySuccess = ref(false)
  const identityFieldErrors = ref<Record<string, string[]>>({})
  const identityDirty = computed(() => fullName.value !== savedIdentity.value.fullName || studentNumber.value !== savedIdentity.value.studentNumber)

  function identityFieldError(name: string) {
    return Object.entries(identityFieldErrors.value).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]?.join(' ')
  }

  async function loadIdentity() {
    identityLoading.value = true
    identityError.value = null
    try {
      const { data, error } = await authenticationGetMyProfile()
      if (error || !data) throw error
      description.value = data.description ?? ''
      savedDescription.value = description.value
      fullName.value = data.schoolIdentity?.fullName ?? ''
      studentNumber.value = data.schoolIdentity?.studentNumber ?? ''
      savedIdentity.value = { fullName: fullName.value, studentNumber: studentNumber.value }
      identityLoaded.value = true
    }
    catch (error) {
      identityError.value = parseApiError(error).message
    }
    finally {
      identityLoading.value = false
    }
  }

  async function saveIdentity() {
    if (!identityLoaded.value || identityPending.value || !identityDirty.value) return
    identityPending.value = true
    identityError.value = null
    identitySuccess.value = false
    identityFieldErrors.value = {}
    const draft = { fullName: fullName.value.trim(), studentNumber: studentNumber.value.trim() }
    try {
      const { error } = await authenticationPatchMyProfile({
        body: { schoolIdentity: draft },
      })
      if (error) throw error
      fullName.value = draft.fullName
      studentNumber.value = draft.studentNumber
      savedIdentity.value = draft
      identitySuccess.value = true
      toast.success(translate('ui.dataSaved'))
    }
    catch (error) {
      const parsed = parseApiError(error)
      identityError.value = parsed.message
      identityFieldErrors.value = parsed.fieldErrors ?? {}
    }
    finally {
      identityPending.value = false
    }
  }

  const emailPending = ref(false)
  const emailMessage = ref<string | null>(null)
  const emailError = ref(false)

  async function resendEmail() {
    emailPending.value = true
    emailMessage.value = null
    emailError.value = false
    try {
      const { error } = await resendEmailVerificationEndpoint()
      if (error) throw error
      emailMessage.value = translate('ui.verificationEmailRequestedPleaseCheckYourInbox')
    }
    catch (error) {
      emailError.value = true
      emailMessage.value = parseApiError(error).message
    }
    finally {
      emailPending.value = false
    }
  }

  const ssoConfiguration = ref<NoCtfapiEndpointsAuthenticationMySsoBindingConfigurationResponse | null>(null)
  const ssoLoading = ref(false)
  const ssoLoaded = ref(false)
  const ssoPending = ref(false)
  const ssoError = ref<string | null>(null)
  const ssoProviderId = ref('')
  const ssoPassword = ref('')

  async function loadSsoBinding() {
    ssoLoading.value = true
    ssoError.value = null
    const { data, error } = await authenticationSsoGetMyBinding()
    ssoLoading.value = false
    if (error || !data) {
      ssoError.value = parseApiError(error, translate('sso.bindingUnavailable')).message
      return
    }
    ssoConfiguration.value = data
    ssoProviderId.value ||= data.providers?.[0]?.id ?? ''
    ssoLoaded.value = true
  }

  async function beginSsoBinding() {
    if (!ssoProviderId.value || !ssoPassword.value || ssoPending.value) return
    ssoPending.value = true
    ssoError.value = null
    try {
      const { data, error } = await authenticationSsoBeginBinding({
        body: { providerId: ssoProviderId.value, password: ssoPassword.value },
      })
      if (error || !data?.authorizationUrl) throw error
      ssoPassword.value = ''
      window.location.assign(data.authorizationUrl)
    }
    catch (error) {
      ssoError.value = parseApiError(error, translate('sso.bindingStartFailed')).message
      ssoPending.value = false
    }
  }

  async function unbindSsoIdentity() {
    if (!ssoPassword.value || ssoPending.value) return
    ssoPending.value = true
    ssoError.value = null
    const { error } = await authenticationSsoUnbindIdentity({
      body: { password: ssoPassword.value },
    })
    ssoPending.value = false
    if (error) {
      ssoError.value = parseApiError(error, translate('sso.unbindingFailed')).message
      return
    }
    ssoPassword.value = ''
    invalidate()
    toast.success(translate('sso.unbindingSuccessful'))
    open.value = false
    await navigateTo('/auth/login')
  }

  watch(
    () => [route.query.account, route.query.ssoProvider] as const,
    async ([account, requestedProvider]) => {
      if (account !== 'security' || !user.value || isImpersonating.value) return
      open.value = true
      activeSection.value = 'security'
      if (!ssoLoaded.value && !ssoLoading.value) await loadSsoBinding()
      if (typeof requestedProvider === 'string'
        && ssoConfiguration.value?.providers?.some(provider => provider.id === requestedProvider)) {
        ssoProviderId.value = requestedProvider
      }
      const query = { ...route.query }
      delete query.account
      delete query.ssoProvider
      await navigateTo({ path: route.path, query, hash: route.hash }, { replace: true })
    },
    { immediate: true },
  )

  const currentPassword = ref('')
  const newPassword = ref('')
  const confirmNewPassword = ref('')
  const passwordPending = ref(false)
  const passwordError = ref<string | null>(null)

  async function changePassword() {
    passwordError.value = null
    if (newPassword.value !== confirmNewPassword.value) {
      passwordError.value = translate('ui.theNewPasswordsEnteredTwiceAreInconsistent')
      return
    }
    passwordPending.value = true
    try {
      const { error } = await changePasswordEndpoint({
        body: { currentPassword: currentPassword.value, newPassword: newPassword.value },
      })
      if (error) throw error
      toast.success(translate('ui.thePasswordHasBeenChangedPleaseLogInAgain'))
      currentPassword.value = ''
      newPassword.value = ''
      confirmNewPassword.value = ''
      await logoutAll()
    }
    catch (error) {
      passwordError.value = parseApiError(error).message
    }
    finally {
      passwordPending.value = false
    }
  }

  async function signOut() {
    open.value = false
    await logout()
  }

  const hasUnsaved = computed(() => profileDirty.value || identityDirty.value
    || Boolean(ssoPassword.value || currentPassword.value || newPassword.value || confirmNewPassword.value))

  function beforeUnload(event: BeforeUnloadEvent) {
    if (!hasUnsaved.value) return
    event.preventDefault()
    event.returnValue = ''
  }

  onMounted(() => window.addEventListener('beforeunload', beforeUnload))
  onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))

  const AvatarCropDialog = markRaw(AvatarCropDialogComponent)
  const AdminDateTime = markRaw(AdminDateTimeComponent)

  function setAvatarInputRef(element: Element | ComponentPublicInstance | null) {
    avatarInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof avatarInput.value
  }

  function setWallpaperInputRef(element: Element | ComponentPublicInstance | null) {
    wallpaperInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof wallpaperInput.value
  }

  return {
    UserRound,
    LockKeyhole,
    ShieldCheck,
    ImageIcon,
    LogOut,
    user,
    isImpersonating,
    fetchMe,
    open,
    activeSection,
    setOpen,
    selectSection,
    signOut,
    description,
    profilePending,
    profileError,
    profileSuccess,
    profileDirty,
    saveProfile,
    avatarInput,
    avatarPending,
    avatarEditorOpen,
    avatarSourceFile,
    avatarRequirements,
    selectAvatar,
    setAvatarEditorOpen,
    uploadAvatar,
    reportAvatarError,
    setAvatarInputRef,
    wallpaperInput,
    wallpaperPending,
    wallpaperUrl,
    wallpaperRequirements,
    selectWallpaper,
    setWallpaperEnabled,
    setWallpaperInputRef,
    fullName,
    studentNumber,
    identityLoading,
    identityLoaded,
    identityPending,
    identityError,
    identitySuccess,
    identityDirty,
    identityFieldError,
    loadIdentity,
    saveIdentity,
    emailPending,
    emailMessage,
    emailError,
    resendEmail,
    ssoConfiguration,
    ssoLoading,
    ssoLoaded,
    ssoPending,
    ssoError,
    ssoProviderId,
    ssoPassword,
    loadSsoBinding,
    beginSsoBinding,
    unbindSsoIdentity,
    currentPassword,
    newPassword,
    confirmNewPassword,
    passwordPending,
    passwordError,
    changePassword,
    logoutAll,
    AvatarCropDialog,
    AdminDateTime,
  }
}

export type AccountPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAccountPanel>>>
