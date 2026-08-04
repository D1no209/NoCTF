<script setup lang="ts">
import type { NoCtfapiEndpointsAuthenticationCurrentUserResponse } from '@/api/generated/types.gen'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import {
  Camera,
  LayoutDashboard,
  ListChecks,
  Loader2,
  Mail,
  Save,
  Trophy,
  UserRound,
  Users,
} from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { toast } from 'vue-sonner'
import { apiUrl, authApi, competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { canManagePlatformResources } from '@/api/userRole'
import AvatarEditorDialog from '@/components/home/AvatarEditorDialog.vue'
import HomeCompetitionList from '@/components/home/HomeCompetitionList.vue'
import AppLayout from '@/components/layout/AppLayout.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const queryClient = useQueryClient()

const canManage = computed(() => canManagePlatformResources(auth.userRole))

const profileDescription = ref('')
const profileInitializedFor = ref<string | null>(null)
const avatarEditorOpen = ref(false)
const avatarSourceFile = ref<File | null>(null)

const { data: currentUser, isLoading: loadingProfile } = useQuery({
  queryKey: queryKeys.currentUser,
  queryFn: authApi.getMe,
})

watch(
  currentUser,
  (profile) => {
    if (profile?.userId && profileInitializedFor.value !== profile.userId) {
      profileDescription.value = profile.description ?? ''
      profileInitializedFor.value = profile.userId
    }
  },
  { immediate: true },
)

const avatarUrl = computed(() =>
  currentUser.value?.avatarUrl ? apiUrl(currentUser.value.avatarUrl) : null,
)

const profileInitial = computed(() =>
  (currentUser.value?.userName ?? auth.user?.userName ?? 'N').charAt(0).toUpperCase(),
)

const profileDirty = computed(
  () => profileDescription.value.trim() !== (currentUser.value?.description ?? ''),
)

function cacheCurrentUser(profile: NoCtfapiEndpointsAuthenticationCurrentUserResponse) {
  queryClient.setQueryData(queryKeys.currentUser, profile)
}

const saveProfileMutation = useMutation({
  mutationFn: () => authApi.updateProfile(profileDescription.value.trim() || null),
  onSuccess: (profile) => {
    cacheCurrentUser(profile)
    profileDescription.value = profile.description ?? ''
    toast.success(t('home.profileSaved'))
  },
  onError: () => toast.error(t('home.profileSaveError')),
})

const uploadAvatarMutation = useMutation({
  mutationFn: (file: File) => authApi.uploadAvatar(file),
  onSuccess: (profile) => {
    cacheCurrentUser(profile)
    avatarEditorOpen.value = false
    avatarSourceFile.value = null
    toast.success(t('home.avatarSaved'))
  },
  onError: () => toast.error(t('home.avatarSaveError')),
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
    toast.error(t('home.avatarSourceInvalid'))
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

const { data: competitions, isLoading: loadingCompetitions } = useQuery({
  queryKey: queryKeys.competitions,
  queryFn: () => competitionApi.list(),
})

const activeCompetitions = computed(() => {
  const priority = new Map([
    ['running', 0],
    ['published', 1],
    ['visible', 2],
    ['draft', 3],
    ['paused', 4],
    ['finished', 5],
  ])

  return [...(competitions.value ?? [])]
    .sort((a, b) => {
      const statusA = priority.get(a.status) ?? 6
      const statusB = priority.get(b.status) ?? 6
      if (statusA !== statusB)
        return statusA - statusB
      return new Date(a.startTime).getTime() - new Date(b.startTime).getTime()
    })
    .slice(0, 3)
})
</script>

<template>
  <AppLayout>
    <div class="mx-auto w-full max-w-[1600px] space-y-4 px-4 py-6 md:px-6 lg:px-8">
      <section class="grid gap-4 lg:grid-cols-[minmax(0,1.65fr)_minmax(320px,0.75fr)]">
        <Card class="h-full px-5 py-5 md:px-6">
          <div class="space-y-5">
            <div class="flex items-center gap-2 border-b-2 border-border pb-3">
              <Badge variant="secondary" class="w-fit">
                {{ t('home.badge') }}
              </Badge>
            </div>
            <div class="space-y-3">
              <h1 class="text-3xl font-bold tracking-[0.08em] text-foreground md:text-4xl">
                {{ t('home.title', { name: auth.user?.userName ?? 'NoCTF' }) }}
              </h1>
              <p class="max-w-2xl text-sm leading-7 text-muted-foreground">
                {{ t('home.subtitle') }}
              </p>
            </div>
            <div class="flex flex-col gap-3 sm:flex-row">
              <Button as-child>
                <RouterLink to="/competitions">
                  <Trophy class="size-4" />
                  {{ t('home.browseCompetitions') }}
                </RouterLink>
              </Button>
              <Button v-if="canManage" variant="secondary" as-child>
                <RouterLink to="/admin">
                  <LayoutDashboard class="size-4" />
                  {{ t('nav.admin') }}
                </RouterLink>
              </Button>
            </div>
          </div>
        </Card>

        <Card class="h-full p-5 md:p-6">
          <div v-if="loadingProfile" class="space-y-4">
            <div class="flex items-center gap-4">
              <Skeleton class="size-20 rounded-full" />
              <div class="flex-1 space-y-2">
                <Skeleton class="h-5 w-32" />
                <Skeleton class="h-4 w-48" />
              </div>
            </div>
            <Skeleton class="h-28 w-full" />
          </div>
          <div v-else class="space-y-5">
            <div class="flex items-center gap-4 border-b-2 border-border pb-4">
              <div class="relative shrink-0">
                <img
                  v-if="avatarUrl"
                  :src="avatarUrl"
                  :alt="t('home.avatarAlt', { name: currentUser?.userName ?? '' })"
                  class="size-20 rounded-full border-2 border-border object-cover"
                >
                <div
                  v-else
                  class="grid size-20 place-items-center rounded-full border-2 border-border bg-muted text-2xl font-black"
                >
                  {{ profileInitial }}
                </div>
                <label
                  class="absolute -bottom-1 -right-1 grid size-8 cursor-pointer place-items-center rounded-full border-2 border-background bg-foreground text-background transition-transform hover:scale-105"
                >
                  <Camera class="size-4" />
                  <span class="sr-only">{{ t('home.changeAvatar') }}</span>
                  <input
                    class="sr-only"
                    type="file"
                    accept="image/jpeg,image/png,image/webp"
                    @change="selectAvatar"
                  >
                </label>
              </div>
              <div class="min-w-0 space-y-1">
                <div class="flex items-center gap-2">
                  <UserRound class="size-4 text-muted-foreground" />
                  <p class="truncate font-bold">
                    {{ currentUser?.userName ?? auth.user?.userName }}
                  </p>
                </div>
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <Mail class="size-3.5" />
                  <p class="truncate">
                    {{ currentUser?.email }}
                  </p>
                </div>
              </div>
            </div>

            <div class="space-y-2">
              <div class="flex items-center justify-between gap-3">
                <Label for="profile-description">{{ t('home.profileDescription') }}</Label>
                <span class="text-[11px] tabular-nums text-muted-foreground">{{ profileDescription.length }}/500</span>
              </div>
              <Textarea
                id="profile-description"
                v-model="profileDescription"
                maxlength="500"
                rows="4"
                :placeholder="t('home.profileDescriptionPlaceholder')"
                class="resize-none"
              />
            </div>
            <Button
              class="w-full"
              :disabled="!profileDirty || saveProfileMutation.isPending.value"
              @click="saveProfileMutation.mutate()"
            >
              <Loader2 v-if="saveProfileMutation.isPending.value" class="size-4 animate-spin" />
              <Save v-else class="size-4" />
              {{ t('home.saveProfile') }}
            </Button>
          </div>
        </Card>
      </section>

      <section>
        <HomeCompetitionList :competitions="activeCompetitions" :loading="loadingCompetitions" />
      </section>

      <section class="grid gap-4 md:grid-cols-3">
        <Card>
          <CardContent class="pt-6">
            <ListChecks class="mb-3 size-5 text-primary" />
            <h3 class="font-semibold">
              {{ t('home.stepRegisterTitle') }}
            </h3>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ t('home.stepRegisterText') }}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="pt-6">
            <Users class="mb-3 size-5 text-primary" />
            <h3 class="font-semibold">
              {{ t('home.stepTeamTitle') }}
            </h3>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ t('home.stepTeamText') }}
            </p>
          </CardContent>
        </Card>
        <Card>
          <CardContent class="pt-6">
            <Trophy class="mb-3 size-5 text-primary" />
            <h3 class="font-semibold">
              {{ t('home.stepCompeteTitle') }}
            </h3>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ t('home.stepCompeteText') }}
            </p>
          </CardContent>
        </Card>
      </section>
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
