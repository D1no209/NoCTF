<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import {
  ArrowRight,
  EyeOff,
  LayoutDashboard,
  ListChecks,
  Mail,
  Trophy,
  UserRound,
  Users,
} from 'lucide-vue-next'
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import { apiUrl, authApi, competitionApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { canManagePlatformResources } from '@/api/userRole'
import HomeCompetitionList from '@/components/home/HomeCompetitionList.vue'
import AppLayout from '@/components/layout/AppLayout.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const canManage = computed(() => canManagePlatformResources(auth.userRole))

const { data: currentUser, isLoading: loadingProfile } = useQuery({
  queryKey: queryKeys.currentUser,
  queryFn: authApi.getMe,
})

const avatarUrl = computed(() =>
  currentUser.value?.avatarUrl ? apiUrl(currentUser.value.avatarUrl) : null,
)

const profileInitial = computed(() =>
  (currentUser.value?.userName ?? auth.user?.userName ?? 'N').charAt(0).toUpperCase(),
)

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
            <Skeleton class="h-16 w-full" />
          </div>
          <div v-else class="space-y-5">
            <div class="flex items-center gap-4 border-b-2 border-border pb-4">
              <img
                v-if="avatarUrl"
                :src="avatarUrl"
                :alt="t('profile.avatarAlt', { name: currentUser?.userName ?? '' })"
                class="size-20 shrink-0 rounded-full border-2 border-border object-cover"
              >
              <div
                v-else
                class="grid size-20 shrink-0 place-items-center rounded-full border-2 border-border bg-muted text-2xl font-black"
              >
                {{ profileInitial }}
              </div>
              <div class="min-w-0 space-y-1">
                <div class="flex items-center gap-2">
                  <UserRound class="size-4 text-muted-foreground" />
                  <p class="truncate font-bold">
                    {{ currentUser?.userName ?? auth.user?.userName }}
                  </p>
                </div>
                <div class="flex items-center gap-2 text-xs text-muted-foreground">
                  <Mail v-if="currentUser?.isEmailPublic" class="size-3.5" />
                  <EyeOff v-else class="size-3.5" />
                  <span>{{ t(currentUser?.isEmailPublic ? 'profile.emailPublic' : 'profile.emailPrivate') }}</span>
                </div>
              </div>
            </div>

            <p class="line-clamp-3 min-h-10 text-sm leading-6 text-muted-foreground">
              {{ currentUser?.description || t('profile.descriptionEmpty') }}
            </p>
            <Button variant="outline" class="w-full" as-child>
              <RouterLink :to="{ name: 'profile' }">
                {{ t('home.editProfile') }}
                <ArrowRight class="size-4" />
              </RouterLink>
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
  </AppLayout>
</template>
