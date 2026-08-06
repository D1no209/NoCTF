<script setup lang="ts">
import type { NoCtfapiEndpointsAuthenticationCurrentUserResponse } from '@/api/generated/types.gen'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  Camera,
  Eye,
  EyeOff,
  KeyRound,
  Loader2,
  LockKeyhole,
  Mail,
  Save,
  ShieldCheck,
  UserRound,
} from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { ApiError, apiUrl, authApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import AvatarEditorDialog from '@/components/home/AvatarEditorDialog.vue'
import AppLayout from '@/components/layout/AppLayout.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useAuthStore } from '@/stores/auth'
import { useScoreStore } from '@/stores/score'

const { t } = useI18n()
const auth = useAuthStore()
const score = useScoreStore()
const router = useRouter()
const queryClient = useQueryClient()

const description = ref('')
const isEmailPublic = ref(false)
const initializedUserId = ref<string | null>(null)
const avatarEditorOpen = ref(false)
const avatarSourceFile = ref<File | null>(null)
const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const currentPasswordVisible = ref(false)
const newPasswordVisible = ref(false)
const confirmPasswordVisible = ref(false)

const { data: currentUser, isLoading, isError, refetch } = useQuery({
  queryKey: queryKeys.currentUser,
  queryFn: authApi.getMe,
})

watch(
  currentUser,
  (profile) => {
    if (!profile?.userId || initializedUserId.value === profile.userId)
      return
    description.value = profile.description ?? ''
    isEmailPublic.value = profile.isEmailPublic ?? false
    initializedUserId.value = profile.userId
  },
  { immediate: true },
)

const avatarUrl = computed(() =>
  currentUser.value?.avatarUrl ? apiUrl(currentUser.value.avatarUrl) : null,
)
const profileInitial = computed(() =>
  (currentUser.value?.userName ?? auth.user?.userName ?? 'N').charAt(0).toUpperCase(),
)
const descriptionDirty = computed(
  () => description.value.trim() !== (currentUser.value?.description ?? ''),
)
const privacyDirty = computed(
  () => isEmailPublic.value !== (currentUser.value?.isEmailPublic ?? false),
)
const passwordValid = computed(() =>
  currentPassword.value.length > 0
  && newPassword.value.length >= 8
  && newPassword.value !== currentPassword.value
  && confirmPassword.value === newPassword.value,
)

function cacheCurrentUser(profile: NoCtfapiEndpointsAuthenticationCurrentUserResponse) {
  queryClient.setQueryData(queryKeys.currentUser, profile)
}

function applyProfile(profile: NoCtfapiEndpointsAuthenticationCurrentUserResponse) {
  cacheCurrentUser(profile)
  description.value = profile.description ?? ''
  isEmailPublic.value = profile.isEmailPublic ?? false
}

const saveDescriptionMutation = useMutation({
  mutationFn: () => authApi.updateProfile({
    description: description.value.trim() || null,
    isEmailPublic: currentUser.value?.isEmailPublic ?? false,
  }),
  onSuccess: (profile) => {
    applyProfile(profile)
    toast.success(t('profile.descriptionSaved'))
  },
  onError: () => toast.error(t('profile.descriptionSaveError')),
})

const savePrivacyMutation = useMutation({
  mutationFn: () => authApi.updateProfile({
    description: currentUser.value?.description ?? null,
    isEmailPublic: isEmailPublic.value,
  }),
  onSuccess: (profile) => {
    applyProfile(profile)
    toast.success(t('profile.privacySaved'))
  },
  onError: () => toast.error(t('profile.privacySaveError')),
})

const profileSavePending = computed(
  () => saveDescriptionMutation.isPending.value || savePrivacyMutation.isPending.value,
)

const uploadAvatarMutation = useMutation({
  mutationFn: (file: File) => authApi.uploadAvatar(file),
  onSuccess: (profile) => {
    applyProfile(profile)
    avatarEditorOpen.value = false
    avatarSourceFile.value = null
    toast.success(t('profile.avatarSaved'))
  },
  onError: () => toast.error(t('profile.avatarSaveError')),
})

const changePasswordMutation = useMutation({
  mutationFn: () => authApi.changePassword({
    currentPassword: currentPassword.value,
    newPassword: newPassword.value,
  }),
  onSuccess: async () => {
    queryClient.clear()
    auth.logout()
    score.reset()
    toast.success(t('profile.passwordChanged'))
    await router.replace({ name: 'login' })
  },
  onError: error => toast.error(
    error instanceof ApiError && error.status === 409
      ? t('profile.currentPasswordInvalid')
      : t('profile.passwordChangeError'),
  ),
})

function selectAvatar(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0] ?? null
  input.value = ''
  if (!file)
    return
  if (
    !['image/jpeg', 'image/png', 'image/webp'].includes(file.type)
    || file.size > 12 * 1024 * 1024
  ) {
    toast.error(t('profile.avatarSourceInvalid'))
    return
  }
  avatarSourceFile.value = file
  avatarEditorOpen.value = true
}

function onAvatarEditorChange(open: boolean) {
  avatarEditorOpen.value = open
  if (!open && !uploadAvatarMutation.isPending.value)
    avatarSourceFile.value = null
}
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1180px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
      <header class="border-b-2 border-border pb-5">
        <Badge variant="secondary">
          {{ t('profile.badge') }}
        </Badge>
        <h1 class="mt-3 text-3xl font-black tracking-[0.06em] md:text-4xl">
          {{ t('profile.title') }}
        </h1>
        <p class="mt-2 max-w-2xl text-sm leading-6 text-muted-foreground">
          {{ t('profile.subtitle') }}
        </p>
      </header>

      <div v-if="isLoading" class="grid items-start gap-5 lg:grid-cols-[280px_minmax(0,1fr)]">
        <Skeleton class="h-80 w-full" />
        <Skeleton class="h-[920px] w-full" />
      </div>
      <div v-else-if="isError" class="border-2 border-destructive bg-destructive/10 p-6 text-center">
        <p class="text-sm text-destructive">
          {{ t('profile.loadError') }}
        </p>
        <Button class="mt-4" variant="outline" @click="refetch()">
          {{ t('common.retry') }}
        </Button>
      </div>
      <div v-else class="grid items-start gap-5 lg:grid-cols-[280px_minmax(0,1fr)]">
        <Card class="gap-0 p-5 lg:sticky lg:top-24">
          <div class="flex flex-col items-center text-center">
            <img
              v-if="avatarUrl"
              :src="avatarUrl"
              :alt="t('profile.avatarAlt', { name: currentUser?.userName ?? '' })"
              class="size-28 rounded-full border-2 border-border object-cover"
            >
            <div
              v-else
              class="grid size-28 place-items-center rounded-full border-2 border-border bg-muted text-4xl font-black"
            >
              {{ profileInitial }}
            </div>
            <p class="mt-4 max-w-full truncate text-xl font-black">
              {{ currentUser?.userName }}
            </p>
            <p class="mt-1 flex max-w-full items-center gap-2 truncate text-xs text-muted-foreground">
              <Mail class="size-3.5 shrink-0" />
              {{ currentUser?.email }}
            </p>
          </div>
          <div class="mt-5 border-t-2 border-border pt-4">
            <Badge variant="outline" class="w-full justify-center gap-2 py-2">
              <Mail v-if="isEmailPublic" class="size-3.5" />
              <LockKeyhole v-else class="size-3.5" />
              {{ t(isEmailPublic ? 'profile.emailPublic' : 'profile.emailPrivate') }}
            </Badge>
            <p class="mt-3 text-center text-xs leading-5 text-muted-foreground">
              {{ currentUser?.description || t('profile.descriptionEmpty') }}
            </p>
          </div>
        </Card>

        <Card :decorated="false" class="gap-0 overflow-hidden py-0">
          <section class="grid gap-5 p-5 md:grid-cols-[220px_minmax(0,1fr)] md:p-6">
            <div>
              <h2 class="flex items-center gap-2 font-black">
                <UserRound class="size-5" />
                {{ t('profile.basicTitle') }}
              </h2>
              <p class="mt-2 text-xs leading-5 text-muted-foreground">
                {{ t('profile.basicDescription') }}
              </p>
            </div>
            <div class="min-w-0 space-y-4">
              <div class="space-y-2">
                <div class="flex items-center justify-between gap-3">
                  <Label for="profile-description">{{ t('profile.descriptionLabel') }}</Label>
                  <span class="font-mono text-[11px] tabular-nums text-muted-foreground">{{ description.length }}/500</span>
                </div>
                <Textarea
                  id="profile-description"
                  v-model="description"
                  :disabled="profileSavePending"
                  maxlength="500"
                  rows="5"
                  :placeholder="t('profile.descriptionPlaceholder')"
                  class="resize-none"
                />
              </div>
              <div class="flex justify-end">
                <Button
                  :disabled="!descriptionDirty || profileSavePending"
                  @click="saveDescriptionMutation.mutate()"
                >
                  <Loader2 v-if="saveDescriptionMutation.isPending.value" class="size-4 animate-spin" />
                  <Save v-else class="size-4" />
                  {{ t('profile.saveDescription') }}
                </Button>
              </div>
            </div>
          </section>

          <section class="grid gap-5 border-t-2 border-border p-5 md:grid-cols-[220px_minmax(0,1fr)] md:p-6">
            <div>
              <h2 class="flex items-center gap-2 font-black">
                <Camera class="size-5" />
                {{ t('profile.avatarTitle') }}
              </h2>
              <p class="mt-2 text-xs leading-5 text-muted-foreground">
                {{ t('profile.avatarSectionDescription') }}
              </p>
            </div>
            <div class="min-w-0 space-y-4">
              <div class="flex items-center gap-4 border-2 border-dashed border-border bg-muted/40 p-4">
                <Camera class="size-7 shrink-0 text-muted-foreground" />
                <p class="text-sm leading-6 text-muted-foreground">
                  {{ t('profile.avatarSourceHelp') }}
                </p>
              </div>
              <label class="flex justify-end">
                <Button as="span" class="cursor-pointer">
                  <Camera class="size-4" />
                  {{ t('profile.chooseAvatar') }}
                </Button>
                <input
                  class="sr-only"
                  type="file"
                  accept="image/jpeg,image/png,image/webp"
                  @change="selectAvatar"
                >
              </label>
            </div>
          </section>

          <section class="grid gap-5 border-t-2 border-border p-5 md:grid-cols-[220px_minmax(0,1fr)] md:p-6">
            <div>
              <h2 class="flex items-center gap-2 font-black">
                <ShieldCheck class="size-5" />
                {{ t('profile.privacyTitle') }}
              </h2>
              <p class="mt-2 text-xs leading-5 text-muted-foreground">
                {{ t('profile.privacyDescription') }}
              </p>
            </div>
            <div class="min-w-0 space-y-4">
              <label class="flex cursor-pointer items-start gap-4 border-2 border-border bg-muted/40 p-4">
                <input
                  v-model="isEmailPublic"
                  type="checkbox"
                  :disabled="profileSavePending"
                  class="mt-0.5 size-5 shrink-0 accent-foreground"
                >
                <span>
                  <span class="block text-sm font-bold">{{ t('profile.publicEmailLabel') }}</span>
                  <span class="mt-1 block text-xs leading-5 text-muted-foreground">
                    {{ t('profile.publicEmailHelp') }}
                  </span>
                </span>
              </label>
              <div class="flex items-center gap-2 border-2 border-border bg-muted px-3 py-2 text-xs text-muted-foreground">
                <LockKeyhole class="size-4 shrink-0" />
                {{ t('profile.ownerAdminEmailHelp') }}
              </div>
              <div class="flex justify-end">
                <Button
                  :disabled="!privacyDirty || profileSavePending"
                  @click="savePrivacyMutation.mutate()"
                >
                  <Loader2 v-if="savePrivacyMutation.isPending.value" class="size-4 animate-spin" />
                  <Save v-else class="size-4" />
                  {{ t('profile.savePrivacy') }}
                </Button>
              </div>
            </div>
          </section>

          <section class="grid gap-5 border-t-2 border-border p-5 md:grid-cols-[220px_minmax(0,1fr)] md:p-6">
            <div>
              <h2 class="flex items-center gap-2 font-black">
                <KeyRound class="size-5" />
                {{ t('profile.passwordTitle') }}
              </h2>
              <p class="mt-2 text-xs leading-5 text-muted-foreground">
                {{ t('profile.passwordDescription') }}
              </p>
            </div>
            <div class="min-w-0 space-y-4">
              <div class="space-y-2">
                <Label for="current-password">{{ t('profile.currentPassword') }}</Label>
                <div class="relative">
                  <Input
                    id="current-password"
                    v-model="currentPassword"
                    :type="currentPasswordVisible ? 'text' : 'password'"
                    autocomplete="current-password"
                    class="pr-11"
                  />
                  <button
                    type="button"
                    class="absolute inset-y-0 right-0 grid w-10 place-items-center text-muted-foreground hover:text-foreground"
                    :aria-label="t(currentPasswordVisible ? 'auth.hidePassword' : 'auth.showPassword')"
                    :aria-pressed="currentPasswordVisible"
                    @click="currentPasswordVisible = !currentPasswordVisible"
                  >
                    <EyeOff v-if="currentPasswordVisible" class="size-4" />
                    <Eye v-else class="size-4" />
                  </button>
                </div>
              </div>
              <div class="space-y-2">
                <Label for="new-password">{{ t('profile.newPassword') }}</Label>
                <div class="relative">
                  <Input
                    id="new-password"
                    v-model="newPassword"
                    :type="newPasswordVisible ? 'text' : 'password'"
                    autocomplete="new-password"
                    minlength="8"
                    class="pr-11"
                  />
                  <button
                    type="button"
                    class="absolute inset-y-0 right-0 grid w-10 place-items-center text-muted-foreground hover:text-foreground"
                    :aria-label="t(newPasswordVisible ? 'auth.hidePassword' : 'auth.showPassword')"
                    :aria-pressed="newPasswordVisible"
                    @click="newPasswordVisible = !newPasswordVisible"
                  >
                    <EyeOff v-if="newPasswordVisible" class="size-4" />
                    <Eye v-else class="size-4" />
                  </button>
                </div>
              </div>
              <div class="space-y-2">
                <Label for="confirm-password">{{ t('profile.confirmPassword') }}</Label>
                <div class="relative">
                  <Input
                    id="confirm-password"
                    v-model="confirmPassword"
                    :type="confirmPasswordVisible ? 'text' : 'password'"
                    autocomplete="new-password"
                    minlength="8"
                    class="pr-11"
                  />
                  <button
                    type="button"
                    class="absolute inset-y-0 right-0 grid w-10 place-items-center text-muted-foreground hover:text-foreground"
                    :aria-label="t(confirmPasswordVisible ? 'auth.hidePassword' : 'auth.showPassword')"
                    :aria-pressed="confirmPasswordVisible"
                    @click="confirmPasswordVisible = !confirmPasswordVisible"
                  >
                    <EyeOff v-if="confirmPasswordVisible" class="size-4" />
                    <Eye v-else class="size-4" />
                  </button>
                </div>
              </div>
              <p v-if="newPassword && newPassword.length < 8" class="text-xs text-destructive">
                {{ t('profile.passwordMinimum') }}
              </p>
              <p v-else-if="confirmPassword && confirmPassword !== newPassword" class="text-xs text-destructive">
                {{ t('profile.passwordMismatch') }}
              </p>
              <div class="flex justify-end">
                <Button
                  :disabled="!passwordValid || changePasswordMutation.isPending.value"
                  @click="changePasswordMutation.mutate()"
                >
                  <Loader2 v-if="changePasswordMutation.isPending.value" class="size-4 animate-spin" />
                  <KeyRound v-else class="size-4" />
                  {{ t('profile.changePassword') }}
                </Button>
              </div>
            </div>
          </section>
        </Card>
      </div>
    </div>

    <AvatarEditorDialog
      :open="avatarEditorOpen"
      :file="avatarSourceFile"
      :saving="uploadAvatarMutation.isPending.value"
      @update:open="onAvatarEditorChange"
      @save="uploadAvatarMutation.mutate"
    />
  </AppLayout>
</template>
