<script setup lang="ts">
import { ArrowRight, Bell, Megaphone } from '@lucide/vue'
import { listNotificationsEndpoint, readNotificationThreadEndpoint } from '~/api'
import type { NoCtfapiEndpointsNotificationsNotificationResponse } from '~/api'

const props = withDefaults(defineProps<{
  competitionId?: string
  heading?: string
  description?: string
}>(), {
  competitionId: undefined,
  heading: '通知中心',
  description: '竞赛动态、评测结果与队伍事件会通知到这里',
})

type Notification = NoCtfapiEndpointsNotificationsNotificationResponse

const route = useRoute()
const router = useRouter()
const { markAllRead } = useNotificationUnread()
const selected = ref<Notification | null>(null)
const thread = ref<Notification[]>([])
const threadLoading = ref(false)
const threadError = ref<string | null>(null)

const { items, loading, error, hasMore, initialized, loadMore } =
  useCursorPagination<Notification>(async (cursor) => {
    const { data, error: requestError } = await listNotificationsEndpoint({
      query: {
        competitionId: props.competitionId,
        cursor,
        limit: 50,
      },
    })
    if (requestError || !data) throw requestError ?? new Error('加载通知失败')
    return { items: data.items, nextCursor: data.nextCursor }
  })

function sourceLabel(notification: Notification): string {
  if (notification.sourceDisplayName) return notification.sourceDisplayName
  return notification.sourceType === 0 ? '赛事系统' : '赛事工作人员'
}

function threadText(notification: Notification): string {
  const body = notificationBody(notification)
  if (body) return body
  const payload = notification.content && typeof notification.content === 'object'
    ? notification.content as Record<string, unknown>
    : {}
  if (notification.kind === 'QuestionStatusChanged') {
    const from = typeof payload.from === 'string' ? payload.from : '未知'
    const to = typeof payload.to === 'string' ? payload.to : '未知'
    return `咨询状态：${from} → ${to}`
  }
  return notificationText(notification)
}

async function openNotification(notification: Notification, updateRoute = true): Promise<void> {
  selected.value = notification
  thread.value = []
  threadError.value = null
  if (updateRoute) {
    await router.replace({
      query: { ...route.query, notification: notification.id },
    })
  }

  const rootId = notificationThreadRootId(notification)
  if (!rootId) return
  threadLoading.value = true
  try {
    const { data, error: requestError } = await readNotificationThreadEndpoint({
      path: { notificationId: rootId },
    })
    if (requestError || !data) throw requestError ?? new Error('加载通知详情失败')
    thread.value = data.items ?? []
  }
  catch (requestError) {
    threadError.value = parseApiError(requestError, '加载通知详情失败').message
  }
  finally {
    threadLoading.value = false
  }
}

async function closeDetail(): Promise<void> {
  selected.value = null
  thread.value = []
  threadError.value = null
  const query = { ...route.query }
  delete query.notification
  await router.replace({ query })
}

async function openFromRoute(): Promise<void> {
  const selectedId = typeof route.query.notification === 'string'
    ? route.query.notification
    : null
  if (!selectedId || selected.value?.id === selectedId) return
  const notification = items.value.find(item => item.id === selectedId)
  if (notification) await openNotification(notification, false)
}

onMounted(async () => {
  await loadMore()
  markAllRead(items.value[0]?.id)
  await openFromRoute()
})

watch(() => route.query.notification, () => void openFromRoute())

const actionTarget = computed(() =>
  thread.value.find(item => item.kind === 'QuestionOpened') ?? selected.value,
)
const actionPath = computed(() => actionTarget.value ? notificationTargetPath(actionTarget.value) : null)
const showAction = computed(() => selected.value?.kind !== 'CompetitionAnnouncement' && actionPath.value !== null)
</script>

<template>
  <div class="flex flex-col gap-6">
    <header class="flex items-start gap-3">
      <div class="mt-0.5 grid size-9 shrink-0 place-items-center rounded-lg bg-primary/10 text-primary">
        <Bell class="size-4" aria-hidden="true" />
      </div>
      <div>
        <h2 class="text-xl font-semibold">{{ heading }}</h2>
        <p class="text-sm text-muted-foreground">{{ description }}</p>
      </div>
    </header>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div class="grid min-h-96 items-start gap-6 lg:grid-cols-[minmax(18rem,2fr)_minmax(24rem,3fr)]">
      <section aria-label="通知列表" class="min-w-0">
        <div v-if="loading && !initialized" class="flex flex-col gap-2">
          <Skeleton v-for="i in 5" :key="i" class="h-20 w-full" />
        </div>

        <Empty v-else-if="initialized && !items.length" class="border py-12">
          <EmptyHeader>
            <EmptyTitle>暂无通知</EmptyTitle>
            <EmptyDescription>官方公告和与你有关的比赛消息会出现在这里</EmptyDescription>
          </EmptyHeader>
        </Empty>

        <ul v-else class="flex flex-col gap-2">
          <li v-for="notification in items" :key="notification.id">
            <button
              type="button"
              class="w-full rounded-lg border px-4 py-3 text-left transition-colors hover:border-primary/50 hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              :class="selected?.id === notification.id ? 'border-primary bg-primary/5' : ''"
              @click="openNotification(notification)"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <p class="line-clamp-2 text-sm font-medium">{{ notificationTitle(notification) }}</p>
                  <p class="mt-1 text-xs text-muted-foreground">
                    {{ sourceLabel(notification) }} · {{ formatDateTime(notification.sentAt) }}
                  </p>
                </div>
                <Badge v-if="notification.kind === 'CompetitionAnnouncement'" variant="secondary" class="shrink-0">
                  公告
                </Badge>
              </div>
            </button>
          </li>
        </ul>

        <div v-if="hasMore" class="mt-4 flex justify-center">
          <Button variant="outline" :disabled="loading" @click="loadMore">
            <Spinner v-if="loading" data-icon="inline-start" />
            加载更多
          </Button>
        </div>
      </section>

      <section aria-label="通知详情" class="min-w-0 rounded-xl border bg-card">
        <Empty v-if="!selected" class="py-16">
          <EmptyHeader>
            <EmptyMedia variant="icon"><Megaphone /></EmptyMedia>
            <EmptyTitle>选择一条通知查看详情</EmptyTitle>
            <EmptyDescription>公告正文、发布信息、后续回复和状态变化会在这里完整展示</EmptyDescription>
          </EmptyHeader>
        </Empty>

        <template v-else>
          <div class="flex items-start justify-between gap-4 border-b px-5 py-4">
            <div class="min-w-0">
              <Badge v-if="selected.kind === 'CompetitionAnnouncement'" variant="secondary" class="mb-2">官方公告</Badge>
              <h3 class="text-lg font-semibold leading-snug">{{ notificationTitle(selected) }}</h3>
              <p class="mt-2 text-xs text-muted-foreground">
                发布人：{{ sourceLabel(selected) }} · {{ formatDateTime(selected.sentAt) }}
              </p>
            </div>
            <Button type="button" variant="ghost" size="sm" class="shrink-0" @click="closeDetail">
              关闭
            </Button>
          </div>

          <div class="flex flex-col gap-5 px-5 py-5">
            <p v-if="notificationBody(selected)" class="max-w-[72ch] text-sm leading-7 whitespace-pre-wrap">
              {{ notificationBody(selected) }}
            </p>

            <Button v-if="showAction" as-child class="w-fit">
              <NuxtLink :to="actionPath ?? '/notifications'">
                {{ notificationActionLabel(selected) }}
                <ArrowRight data-icon="inline-end" />
              </NuxtLink>
            </Button>

            <Separator />
            <div>
              <h4 class="mb-3 text-sm font-semibold">后续记录</h4>
              <Skeleton v-if="threadLoading" class="h-28 w-full" />
              <Alert v-else-if="threadError" variant="destructive">
                <AlertDescription>{{ threadError }}</AlertDescription>
              </Alert>
              <p v-else-if="thread.length <= 1" class="text-sm text-muted-foreground">暂无后续回复或状态变化。</p>
              <ol v-else class="flex flex-col gap-4">
                <li v-for="entry in thread" :key="entry.id" class="grid grid-cols-[0.5rem_1fr] gap-3">
                  <span class="mt-1.5 size-2 rounded-full bg-primary" aria-hidden="true" />
                  <div class="min-w-0">
                    <div class="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-muted-foreground">
                      <span class="font-medium text-foreground">{{ sourceLabel(entry) }}</span>
                      <span>{{ formatDateTime(entry.sentAt) }}</span>
                    </div>
                    <p class="mt-1 max-w-[72ch] text-sm leading-6 whitespace-pre-wrap">{{ threadText(entry) }}</p>
                  </div>
                </li>
              </ol>
            </div>
          </div>
        </template>
      </section>
    </div>
  </div>
</template>
