import { proxyRefs } from 'vue'
import { markRaw } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import { UserRound, LockKeyhole, ShieldCheck } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { authenticationUpdateMyProfile, authenticationUploadMyAvatar, changePasswordEndpoint, resendEmailVerificationEndpoint } from '../../../api'
import AvatarCropDialogComponent from '../../account/AvatarCropDialog.vue'
import SchoolIdentityFormComponent from '../../account/SchoolIdentityForm.vue'

/** Owns state, effects and commands for AccountIndexPage. */
export function useAccountIndexPage() {
  const { user, fetchMe, logoutAll } = useAuth()

  const description = ref(user.value?.description ?? '')

  const profilePending = ref(false)

  const savedDescription = ref(description.value)

  const profileError = ref<string | null>(null)

  const profileSuccess = ref(false)

  const profileDirty = computed(() => description.value !== savedDescription.value)

  const schoolDirty = ref(false)

  const passwordError = ref<string | null>(null)

  const emailPending = ref(false)

  const emailMessage = ref<string | null>(null)

  const emailError = ref(false)

  async function resendEmail() {
    emailPending.value = true
    emailMessage.value = null
    emailError.value = false
    try {
      const result = await resendEmailVerificationEndpoint()
      if (result.error) throw result.error
      emailMessage.value = translate("ui.verificationEmailRequestedPleaseCheckYourInbox")
    } catch (e) { emailError.value = true; emailMessage.value = parseApiError(e).message }
    finally { emailPending.value = false }
  }

  watch(user, (u) => {
    if (!profileDirty.value) {
      description.value = u?.description ?? ''
      savedDescription.value = description.value
    }
  })

  async function saveProfile() {
    profilePending.value = true
    profileError.value = null
    profileSuccess.value = false
    const draft = description.value
    try {
      const { data, error } = await authenticationUpdateMyProfile({
        body: { description: draft || null },
      })
      if (error || !data) throw parseApiError(error)
      savedDescription.value = draft
      user.value = data
      profileSuccess.value = true
      toast.success(translate("ui.dataSaved"))
    }
    catch (e) {
      profileError.value = parseApiError(e).message
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
    if (!file)
      return
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 12 * 1024 * 1024) {
      toast.error(translate("ui.pleaseSelectAJpegPngOrWebpImageNoLarger"))
      return
    }
    avatarSourceFile.value = file
    avatarEditorOpen.value = true
  }

  function setAvatarEditorOpen(open: boolean) {
    if (avatarPending.value)
      return
    avatarEditorOpen.value = open
    if (!open)
      avatarSourceFile.value = null
  }

  async function uploadAvatar(file: File) {
    avatarPending.value = true
    try {
      const { error } = await authenticationUploadMyAvatar({ body: { file } })
      if (error) throw parseApiError(error)
      await fetchMe()
      avatarEditorOpen.value = false
      avatarSourceFile.value = null
      toast.success(translate("ui.avatarHasBeenUpdated"))
    }
    catch (e) {
      toast.error(parseApiError(e).message)
    }
    finally {
      avatarPending.value = false
    }
  }

  const currentPassword = ref('')

  const newPassword = ref('')

  const confirmNewPassword = ref('')

  const passwordPending = ref(false)

  async function changePassword() {
    passwordError.value = null
    if (newPassword.value !== confirmNewPassword.value) {
      passwordError.value = translate("ui.theNewPasswordsEnteredTwiceAreInconsistent")
      return
    }
    passwordPending.value = true
    try {
      const { error } = await changePasswordEndpoint({
        body: { currentPassword: currentPassword.value, newPassword: newPassword.value },
      })
      if (error) throw parseApiError(error)
      toast.success(translate("ui.thePasswordHasBeenChangedPleaseLogInAgain"))
      currentPassword.value = ''
      newPassword.value = ''
      confirmNewPassword.value = ''
      await logoutAll()
    }
    catch (e) {
      passwordError.value = parseApiError(e).message
    }
    finally {
      passwordPending.value = false
    }
  }

  const hasUnsaved = computed(() => profileDirty.value || schoolDirty.value || Boolean(currentPassword.value || newPassword.value || confirmNewPassword.value))

  onBeforeRouteLeave(() => !hasUnsaved.value || window.confirm(translate("ui.youHaveUnsavedChangesLeaveAnyway")))

  function beforeUnload(event: BeforeUnloadEvent) {
    if (hasUnsaved.value) { event.preventDefault(); event.returnValue = '' }
  }

  onMounted(() => window.addEventListener('beforeunload', beforeUnload))

  onBeforeUnmount(() => window.removeEventListener('beforeunload', beforeUnload))

  const AvatarCropDialog = markRaw(AvatarCropDialogComponent)

  const SchoolIdentityForm = markRaw(SchoolIdentityFormComponent)

  function setAvatarInputRef(element: Element | ComponentPublicInstance | null) { avatarInput.value = (element instanceof Element ? element : element?.$el ?? null) as typeof avatarInput.value }

  const viewBindings = {
      UserRound,
      LockKeyhole,
      ShieldCheck,
      toast,
      user,
      fetchMe,
      logoutAll,
      description,
      profilePending,
      profileError,
      profileSuccess,
      profileDirty,
      schoolDirty,
      passwordError,
      emailPending,
      emailMessage,
      emailError,
      resendEmail,
      saveProfile,
      avatarInput,
      avatarPending,
      avatarEditorOpen,
      avatarSourceFile,
      selectAvatar,
      setAvatarEditorOpen,
      uploadAvatar,
      currentPassword,
      newPassword,
      confirmNewPassword,
      passwordPending,
      changePassword,
      AvatarCropDialog,
      SchoolIdentityForm,
      setAvatarInputRef
    }
  const viewState = proxyRefs(viewBindings)

  function onDirtySchoolDirty(value: typeof viewState.schoolDirty) {
    viewState.schoolDirty = value
  }

  return { ...viewBindings, onDirtySchoolDirty }
}

export type AccountIndexPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAccountIndexPage>>>
