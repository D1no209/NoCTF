<script setup lang="ts">
import type { UserNotification } from '@/api/noctf'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { Bell, BellOff, CheckCheck, Loader2 } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { notificationApi } from '@/api/noctf'
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
const queryClient = useQueryClient()
const open = ref(false)

const notificationsQuery = useQuery({
  queryKey: queryKeys.notifications,
  queryFn: () => notificationApi.list(20),
  enabled: computed(() => auth.isAuthenticated),
  refetchInterval: 30_000,
  staleTime: 10_000,
})

const markRead = useMutation({
  mutationFn: (id: string) => notificationApi.markRead(id),
  onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications }),
})

const markAllRead = useMutation({
  mutationFn: () => notificationApi.markAllRead(),
  onSuccess: () => queryClient.invalidateQueries({ queryKey: queryKeys.notifications }),
})

const unreadCount = computed(() => notificationsQuery.data.value?.unreadCount ?? 0)
const unreadLabel = computed(() => unreadCount.value > 99 ? '99+' : String(unreadCount.value))
const buttonLabel = computed(() => unreadCount.value > 0
  ? t('notifications.openUnread', { count: unreadCount.value })
  : t('notifications.open'))

watch(open, (isOpen) => {
  if (isOpen)
    notificationsQuery.refetch()
})

async function selectNotification(notification: UserNotification) {
  if (!notification.isRead) {
    try {
      await markRead.mutateAsync(notification.id)
    }
    catch {
      // Reading a notification must not block its competition navigation.
    }
  }

  open.value = false
  if (notification.competitionId)
    await router.push(`/competitions/${notification.competitionId}`)
}

function title(notification: UserNotification) {
  const copy = notificationCopy(notification)
  return t(copy.titleKey, copy.params)
}

function body(notification: UserNotification) {
  const copy = notificationCopy(notification)
  return t(copy.bodyKey, copy.params)
}
</script>

<template>
  <DropdownMenu v-if="auth.isAuthenticated" v-model:open="open">
    <DropdownMenuTrigger as-child>
      <Button
        variant="ghost"
        size="icon-sm"
        class="relative shrink-0 rounded-none"
        :aria-label="buttonLabel"
        :title="buttonLabel"
      >
        <Bell class="size-4" aria-hidden="true" />
        <span
          v-if="unreadCount > 0"
          class="absolute -right-1.5 -top-1.5 min-w-5 border-2 border-muted bg-destructive px-1 text-center text-[0.8rem] font-bold leading-4 text-white"
          aria-hidden="true"
        >
          {{ unreadLabel }}
        </span>
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
            {{ t('notifications.unreadSummary', { count: unreadCount }) }}
          </p>
        </div>
        <Button
          v-if="unreadCount > 0"
          variant="ghost"
          size="sm"
          :disabled="markAllRead.isPending.value"
          @click="markAllRead.mutate()"
        >
          <Loader2 v-if="markAllRead.isPending.value" class="animate-spin" />
          <CheckCheck v-else />
          {{ t('notifications.markAllRead') }}
        </Button>
      </div>

      <div v-if="notificationsQuery.isLoading.value" class="space-y-3 p-3" aria-busy="true">
        <div v-for="index in 3" :key="index" class="space-y-2">
          <Skeleton class="h-4 w-2/3 rounded-none" />
          <Skeleton class="h-3 w-full rounded-none" />
        </div>
      </div>

      <div v-else-if="notificationsQuery.isError.value" class="p-5 text-center">
        <BellOff class="mx-auto mb-2 size-5 text-muted-foreground" aria-hidden="true" />
        <p class="font-bold">
          {{ t('notifications.loadFailed') }}
        </p>
        <Button variant="outline" size="sm" class="mt-3" @click="notificationsQuery.refetch()">
          {{ t('common.retry') }}
        </Button>
      </div>

      <div v-else-if="!notificationsQuery.data.value?.items.length" class="p-6 text-center">
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
          v-for="notification in notificationsQuery.data.value?.items"
          :key="notification.id"
          type="button"
          class="relative block w-full border-b border-border px-3 py-3 text-left transition-colors last:border-b-0 hover:bg-accent focus-visible:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
          :class="notification.isRead ? 'text-muted-foreground' : 'bg-card text-foreground'"
          @click="selectNotification(notification)"
        >
          <span v-if="!notification.isRead" class="absolute right-3 top-3 size-2 bg-destructive" aria-hidden="true" />
          <span class="block pr-5 font-bold">{{ title(notification) }}</span>
          <span class="mt-1 block line-clamp-3 text-xs leading-5">{{ body(notification) }}</span>
          <time class="mt-1.5 block text-xs tabular-nums" :datetime="notification.createdAt">
            {{ formatNotificationTime(notification.createdAt, locale) }}
          </time>
        </button>
      </div>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
