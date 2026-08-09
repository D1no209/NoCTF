<script setup lang="ts">
import { listNotificationsEndpoint } from '~/api'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '~/api'

definePageMeta({ middleware: 'auth' })

type Notification = NoCtfapiEndpointsNotificationsNotificationResponse
const { markAllRead } = useNotificationUnread()

const { items, loading, error, hasMore, initialized, loadMore } =
  useCursorPagination<Notification>(async (cursor) => {
    const { data, error: err } = await listNotificationsEndpoint({
      query: { cursor, limit: 50 },
    })
    if (err || !data) throw err ?? new Error('加载失败')
    return { items: data.items, nextCursor: data.nextCursor }
  })

onMounted(async () => {
  await loadMore()
  markAllRead(items.value[0]?.id)
})
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-8">
    <div>
      <h1 class="text-2xl font-semibold">通知中心</h1>
      <p class="text-sm text-muted-foreground">竞赛动态、评测结果与队伍事件会通知到这里</p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="i in 5" :key="i" class="h-14 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>暂无通知</EmptyTitle>
        <EmptyDescription>报名参赛后,与你相关的竞赛事件会出现在这里</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <ul v-else class="flex flex-col gap-2">
      <li v-for="notification in items" :key="notification.id">
        <NuxtLink
          :to="notificationCompetitionId(notification) ? `/competitions/${notificationCompetitionId(notification)}` : '/notifications'"
          class="block rounded-md border px-4 py-3 transition-colors hover:border-primary/50"
        >
          <p class="text-sm">{{ notificationText(notification) }}</p>
          <p class="mt-1 text-xs text-muted-foreground">{{ formatDateTime(notification.sentAt) }}</p>
        </NuxtLink>
      </li>
    </ul>

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" />
        加载更多
      </Button>
    </div>
  </div>
</template>
