<script setup lang="ts">
import type { PublicNotification } from '@/api/notificationPresentation'
import { useInfiniteQuery } from '@tanstack/vue-query'
import { Bell, BellOff, Loader2, RefreshCw } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { notificationApi } from '@/api/noctf'
import { nextNotificationPageParam, NotificationKind } from '@/api/notificationPresentation'
import { queryKeys } from '@/api/queryKeys'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { useAuthStore } from '@/stores/auth'
import { formatNotificationTime, notificationCopy } from './notificationPresentation'

const { locale, t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const open = ref(false)
const pageSize = 20

const notificationsQuery = useInfiniteQuery({
  queryKey: queryKeys.notifications,
  queryFn: ({ pageParam, signal }) => notificationApi.listPage({
    cursor: pageParam ?? undefined,
    limit: pageSize,
  }, signal),
  initialPageParam: null as string | null,
  getNextPageParam: nextNotificationPageParam,
  enabled: computed(() => auth.isAuthenticated),
  refetchInterval: 30_000,
  staleTime: 10_000,
})

const notifications = computed(() => {
  const ids = new Set<string>()
  return (notificationsQuery.data.value?.pages ?? [])
    .flatMap(page => page.items)
    .filter((notification) => {
      if (ids.has(notification.id))
        return false
      ids.add(notification.id)
      return true
    })
})

watch(open, (isOpen) => {
  if (isOpen)
    void notificationsQuery.refetch()
})

async function selectNotification(notification: PublicNotification) {
  open.value = false
  if (notification.kind === NotificationKind.DataExportReady
    || notification.kind === NotificationKind.DataExportFailed) {
    if (notification.competitionId) {
      await router.push({
        name: 'admin-competition-detail',
        params: { id: notification.competitionId },
        query: { section: 'exports' },
      })
    }
    else {
      await router.push({ name: 'admin-platform-logs', query: { tab: 'audit' } })
    }
    return
  }
  if (notification.competitionId)
    await router.push(`/competitions/${notification.competitionId}`)
}

function title(notification: PublicNotification) {
  const copy = notificationCopy(notification)
  return t(copy.titleKey)
}

function body(notification: PublicNotification) {
  const copy = notificationCopy(notification)
  return t(copy.bodyKey, copy.bodyParams ?? {})
}
</script>

<template>
  <DropdownMenu v-if="auth.isAuthenticated" v-model:open="open">
    <DropdownMenuTrigger as-child>
      <Button
        variant="ghost"
        size="icon-sm"
        class="relative shrink-0 rounded-none"
        :aria-label="t('notifications.open')"
        :title="t('notifications.open')"
      >
        <Bell class="size-4" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>

    <DropdownMenuContent
      align="end"
      :side-offset="8"
      class="w-[min(24rem,calc(100vw-1rem))] rounded-none border-2 p-0 shadow-[4px_4px_0_#bdbdbd]"
    >
      <div class="flex items-center justify-between gap-3 border-b-2 border-border px-3 py-2.5">
        <div>
          <p class="font-bold uppercase tracking-[0.12em]">
            {{ t('notifications.title') }}
          </p>
          <p class="text-xs text-muted-foreground">
            {{ t('notifications.historySummary', { count: notifications.length }) }}
          </p>
        </div>
        <Button
          variant="ghost"
          size="icon-sm"
          :aria-label="t('notifications.refresh')"
          :title="t('notifications.refresh')"
          :disabled="notificationsQuery.isFetching.value"
          @click.stop="notificationsQuery.refetch()"
        >
          <RefreshCw
            :class="{ 'animate-spin': notificationsQuery.isFetching.value }"
            aria-hidden="true"
          />
        </Button>
      </div>

      <div
        v-if="notificationsQuery.isLoading.value && notifications.length === 0"
        class="space-y-3 p-3"
        aria-busy="true"
      >
        <div v-for="index in 3" :key="index" class="space-y-2">
          <Skeleton class="h-4 w-2/3 rounded-none" />
          <Skeleton class="h-3 w-full rounded-none" />
        </div>
      </div>

      <div
        v-else-if="notificationsQuery.isError.value && notifications.length === 0"
        class="p-5 text-center"
      >
        <BellOff class="mx-auto mb-2 size-5 text-muted-foreground" aria-hidden="true" />
        <p class="font-bold">
          {{ t('notifications.loadFailed') }}
        </p>
        <Button variant="outline" size="sm" class="mt-3" @click="notificationsQuery.refetch()">
          {{ t('common.retry') }}
        </Button>
      </div>

      <div v-else-if="notifications.length === 0" class="p-6 text-center">
        <Bell class="mx-auto mb-2 size-5 text-muted-foreground" aria-hidden="true" />
        <p class="font-bold">
          {{ t('notifications.empty') }}
        </p>
        <p class="mt-1 text-xs text-muted-foreground">
          {{ t('notifications.emptyHint') }}
        </p>
      </div>

      <div v-else class="max-h-[min(28rem,70vh)] overflow-y-auto py-1">
        <button
          v-for="notification in notifications"
          :key="notification.id"
          type="button"
          class="relative block w-full border-b border-border bg-card px-3 py-3 text-left text-foreground transition-colors last:border-b-0 hover:bg-accent focus-visible:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
          @click="selectNotification(notification)"
        >
          <span class="block font-bold">{{ title(notification) }}</span>
          <span class="mt-1 block line-clamp-3 text-xs leading-5">{{ body(notification) }}</span>
          <time class="mt-1.5 block text-xs tabular-nums" :datetime="notification.createdAt">
            {{ formatNotificationTime(notification.createdAt, locale) }}
          </time>
        </button>

        <div
          v-if="notificationsQuery.hasNextPage.value || notificationsQuery.isFetchNextPageError.value"
          class="space-y-2 border-t-2 border-border p-2"
        >
          <p
            v-if="notificationsQuery.isFetchNextPageError.value"
            class="px-1 text-xs text-destructive"
            role="alert"
          >
            {{ t('notifications.loadOlderFailed') }}
          </p>
          <Button
            v-if="notificationsQuery.hasNextPage.value"
            variant="outline"
            size="sm"
            class="w-full"
            :disabled="notificationsQuery.isFetchingNextPage.value"
            @click="notificationsQuery.fetchNextPage()"
          >
            <Loader2
              v-if="notificationsQuery.isFetchingNextPage.value"
              class="animate-spin"
              aria-hidden="true"
            />
            {{ t('notifications.loadOlder') }}
          </Button>
        </div>
      </div>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
