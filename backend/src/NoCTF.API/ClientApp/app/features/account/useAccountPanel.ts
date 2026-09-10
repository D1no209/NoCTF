import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { Image as ImageIcon, LockKeyhole, LogOut, ShieldCheck, UserRound } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  authenticationGetMySchoolIdentity,
  authenticationUpdateMyWallpaperPreference,
  authenticationUpdateMyProfile,
  authenticationUpdateMySchoolIdentity,
  authenticationUploadMyAvatar,
  authenticationUploadMyWallpaper,
  changePasswordEndpoint,
  resendEmailVerificationEndpoint,
} from '../../api'
import AvatarCropDialogComponent from './AvatarCropDialog.vue'

export type AccountPanelSection = 'profile' | 'identity' | 'wallpaper' | 'security'

/** Owns the compact account popover, drafts and account commands across route changes. */
export function useAccountPanel() {
  const { user, fetchMe, logout, logoutAll } = useAuth()

  const open = ref(false)
  const activeSection = ref<AccountPanelSection | null>(null)

  function imageUploadError(error: unknown): string {
    const parsed = parseApiError(error)
    if (parsed.code === 'UploadTooLarge')
      return translate('ui.theUploadedFileIsTooLarge')
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
    if (section === 'identity' && !identityLoaded.value && !identityLoading.value)
      void loadIdentity()
  }

  const description = ref(user.value?.description ?? '')
  const savedDescription = ref(description.value)
  const profilePending = ref(false)
  const profileError = ref<string | null>(null)
  const profileSuccess = ref(false)
  const profileDirty = computed(() => description.value !== savedDescription.value)

  watch(user, current => {
    if (!profileDirty.value) {
      description.value = current?.description ?? ''
      savedDescription.value = description.value
    }
  })

  async function saveProfile() {
    if (profilePending.value || !profileDirty.value) return
    profilePending.value = true
    profileError.value = null
    profileSuccess.value = false
    const draft = description.value
    try {
      const { data, error } = await authenticationUpdateMyProfile({ body: { description: draft || null } })
      if (error || !data) throw error
      savedDescription.value = draft
      user.value = data
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
      toast.error(imageUploadError(error))
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

    wallpaperPending.value = true
    try {
      const { data, error } = await authenticationUploadMyWallpaper({ body: { file } })
      if (error || !data) throw error
      user.value = data
      await refreshWallpaper(true)
      toast.success(translate('accountPanel.wallpaperUpdated'))
    }
    catch (error) {
      toast.error(imageUploadError(error))
    }
    finally {
      wallpaperPending.value = false
    }
  }

  async function setWallpaperEnabled(enabled: boolean) {
    if (wallpaperPending.value || enabled === Boolean(user.value?.wallpaperEnabled)) return
    wallpaperPending.value = true
    try {
      const { data, error } = await authenticationUpdateMyWallpaperPreference({ body: { enabled } })
      if (error || !data) throw error
      user.value = data
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
      const { data, error } = await authenticationGetMySchoolIdentity()
      if (error || !data) throw error
      fullName.value = data.fullName ?? ''
      studentNumber.value = data.studentNumber ?? ''
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
      const { error } = await authenticationUpdateMySchoolIdentity({ body: draft })
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

  const hasUnsaved = computed(() => profileDirty.value || identityDirty.value || Boolean(currentPassword.value || newPassword.value || confirmNewPassword.value))

  function beforeUnload(event: BeforeUnloadEvent) {
    if (!hasUnsaved.value) return
    event.preventDefault()
    event.returnValue = ''
  }

  onMounted(() => window.addEventListener('beforeunload', beforeUnload))
  onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))

  const AvatarCropDialog = markRaw(AvatarCropDialogComponent)

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
    selectAvatar,
    setAvatarEditorOpen,
    uploadAvatar,
    reportAvatarError,
    setAvatarInputRef,
    wallpaperInput,
    wallpaperPending,
    wallpaperUrl,
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
    currentPassword,
    newPassword,
    confirmNewPassword,
    passwordPending,
    passwordError,
    changePassword,
    logoutAll,
    AvatarCropDialog,
  }
}

export type AccountPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAccountPanel>>>
