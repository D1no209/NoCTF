<script setup lang="ts">
import { ExternalLink, RefreshCw } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminPlatformForceTerminateRuntime,
  adminPlatformListActiveRuntimes,
  adminPlatformTerminateRuntime,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse,
  NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
} from '~/api'
import { createLatestPageRefresh } from '~/lib/latest-page-refresh'
import { adminRuntimeTeamLabel } from '~/utils/admin-runtime'

definePageMeta({ middleware: 'platform-admin' })

type PlatformRuntime = NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse

const {
  items,
  loading,
  error,
  hasMore,
  initialized,
  loadMore: loadRuntimePage,
  reset,
} = useCursorPagination<PlatformRuntime>(async (cursor) => {
  const { data, error: requestError } = await adminPlatformListActiveRuntimes({
    query: { cursor, limit: 100 },
  })
  if (requestError || !data) throw parseApiError(requestError)
  return data
})

const {
  loadNextPage: loadMore,
  refreshLatest: refresh,
} = createLatestPageRefresh({
  loadMore: loadRuntimePage,
  reset: () => reset({ preserveItems: true }),
})

const terminateTarget = ref<PlatformRuntime | null>(null)
const terminatePending = ref(false)
const forceTerminateTarget = ref<PlatformRuntime | null>(null)
const forceTerminateReason = ref('')
const forceTerminateConfirmed = ref(false)
const forceTerminatePending = ref(false)

let refreshTimer: ReturnType<typeof setInterval> | undefined

function runtimeOf(item: PlatformRuntime | null): NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse | null {
  return item?.runtime ?? null
}

function teamLabel(item: PlatformRuntime): string {
  const runtime = runtimeOf(item)
  return runtime ? adminRuntimeTeamLabel(runtime, translate) : '-'
}

function stateBadgeVariant(state?: string): 'default' | 'secondary' | 'outline' | 'destructive' {
  if (state === 'Running') return 'default'
  if (state === 'Stopping') return 'destructive'
  if (state === 'Queued') return 'outline'
  return 'secondary'
}

function canTerminate(item: PlatformRuntime): boolean {
  const state = runtimeOf(item)?.state
  return state === 'Queued' || state === 'Provisioning' || state === 'Running'
}

function openForceTermination(item: PlatformRuntime): void {
  forceTerminateTarget.value = item
  forceTerminateReason.value = ''
  forceTerminateConfirmed.value = false
}

async function submitTermination(): Promise<void> {
  const runtime = runtimeOf(terminateTarget.value)
  if (!runtime?.id || terminatePending.value) return
  terminatePending.value = true
  try {
    const { error: requestError } = await adminPlatformTerminateRuntime({
      path: { runtimeInstanceId: runtime.id },
    })
    if (requestError) throw requestError
    toast.success(translate('实例终止操作已受理'))
    terminateTarget.value = null
    await refresh()
  }
  catch (requestError) {
    toast.error(parseApiError(requestError).message)
  }
  finally {
    terminatePending.value = false
  }
}

async function submitForceTermination(): Promise<void> {
  const runtime = runtimeOf(forceTerminateTarget.value)
  const reason = forceTerminateReason.value.trim()
  if (!runtime?.id
    || reason.length < 8
    || !forceTerminateConfirmed.value
    || forceTerminatePending.value)
    return

  forceTerminatePending.value = true
  try {
    const { error: requestError } = await adminPlatformForceTerminateRuntime({
      path: { runtimeInstanceId: runtime.id },
      body: { reason },
    })
    if (requestError) throw requestError
    toast.success(translate('强制终结已交由 Runner 清理'))
    forceTerminateTarget.value = null
    await refresh()
  }
  catch (requestError) {
    toast.error(parseApiError(requestError).message)
  }
  finally {
    forceTerminatePending.value = false
  }
}

onMounted(() => {
  void loadMore()
  refreshTimer = setInterval(() => void refresh(), 10_000)
})

onBeforeUnmount(() => {
  if (refreshTimer) clearInterval(refreshTimer)
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <Card>
      <CardHeader class="flex-row items-start justify-between gap-4">
        <div>
          <CardTitle>{{ $t('运行容器') }}</CardTitle>
          <CardDescription>{{ $t('集中查看并终止全平台当前活动的 Container 与 Compose 运行环境') }}</CardDescription>
        </div>
        <Button variant="outline" size="sm" :disabled="loading" @click="refresh">
          <Spinner v-if="loading" data-icon="inline-start" />
          <RefreshCw v-else data-icon="inline-start" />
          {{ $t('刷新') }}
        </Button>
      </CardHeader>
      <CardContent class="flex flex-wrap gap-x-6 gap-y-2 text-sm text-muted-foreground">
        <span>{{ $t('当前已载入 {count} 个活动容器', { count: items.length }) }}</span>
        <span>{{ $t('页面每 10 秒自动刷新') }}</span>
      </CardContent>
    </Card>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-64 w-full" />
    <Empty v-else-if="initialized && items.length === 0 && !error" class="border border-dashed py-14">
      <EmptyHeader>
        <EmptyTitle>{{ $t('没有正在运行的容器') }}</EmptyTitle>
        <EmptyDescription>{{ $t('排队、创建中、运行中和停止中的 Container/Compose 实例会显示在这里。') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else-if="items.length > 0" class="overflow-hidden py-0">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('赛事 / 题目') }}</TableHead>
            <TableHead>{{ $t('归属队伍') }}</TableHead>
            <TableHead>{{ $t('运行位置') }}</TableHead>
            <TableHead>{{ $t('状态') }}</TableHead>
            <TableHead>{{ $t('创建 / 到期') }}</TableHead>
            <TableHead class="text-right">{{ $t('操作') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.runtime?.id">
            <TableCell class="min-w-64">
              <div class="grid gap-1">
                <span class="font-semibold">{{ item.scope === 'ChallengeTest' ? $t('题目测试') : item.competitionTitle ?? '-' }}</span>
                <span class="text-xs text-muted-foreground">{{ item.challengeTitle ?? '-' }}</span>
              </div>
            </TableCell>
            <TableCell class="min-w-40 font-medium">{{ teamLabel(item) }}</TableCell>
            <TableCell class="min-w-44">
              <div class="grid gap-1 text-xs">
                <span>{{ enumLabel(RuntimeKindLabel, item.runtime?.runtimeKind) }} · {{ enumLabel(RuntimeProviderLabel, item.runtime?.provider) }}</span>
                <span class="font-mono text-muted-foreground">{{ item.runtime?.runnerId ?? $t('尚未分配 Runner') }}</span>
              </div>
            </TableCell>
            <TableCell>
              <Badge :variant="stateBadgeVariant(item.runtime?.state)">
                {{ enumLabel(RuntimeStateLabel, item.runtime?.state) }}
              </Badge>
            </TableCell>
            <TableCell class="min-w-52 font-mono text-xs tabular-nums">
              <div>{{ adminFormatDateTime(item.runtime?.createdAt) }}</div>
              <div class="text-muted-foreground">{{ adminFormatDateTime(item.runtime?.expiresAt) }}</div>
            </TableCell>
            <TableCell class="min-w-64 text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button v-if="item.runtime?.competitionId" variant="ghost" size="sm" as-child>
                  <NuxtLink :to="`/admin/competitions/${item.runtime.competitionId}/runtimes`">
                    <ExternalLink data-icon="inline-start" /> {{ $t('赛事运行时') }}
                  </NuxtLink>
                </Button>
                <Button v-else-if="item.runtime?.challengeId" variant="ghost" size="sm" as-child>
                  <NuxtLink :to="`/admin/challenges/${item.runtime.challengeId}`">
                    <ExternalLink data-icon="inline-start" /> {{ $t('题目模板') }}
                  </NuxtLink>
                </Button>
                <Button
                  v-if="canTerminate(item)"
                  variant="destructive"
                  size="sm"
                  @click="terminateTarget = item"
                >
                  {{ $t('终止') }}
                </Button>
                <Button
                  v-if="item.runtime?.canForceTerminate"
                  variant="destructive"
                  size="sm"
                  @click="openForceTermination(item)"
                >
                  {{ $t('强制终结') }}
                </Button>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center border-t p-4">
        <Button variant="outline" :disabled="loading" @click="loadMore">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('加载更多') }}
        </Button>
      </div>
    </Card>

    <AlertDialog
      :open="terminateTarget !== null"
      @update:open="(open) => { if (!open && !terminatePending) terminateTarget = null }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('终止此运行时实例？') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('将立即停止并清理「{team}」在「{challenge}」的实例。该操作不会重建环境。', {
              team: terminateTarget ? teamLabel(terminateTarget) : '-',
              challenge: terminateTarget?.challengeTitle ?? '-',
            }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="terminatePending">{{ $t('取消') }}</AlertDialogCancel>
          <Button type="button" variant="destructive" :disabled="terminatePending" @click="submitTermination">
            <Spinner v-if="terminatePending" data-icon="inline-start" /> {{ $t('确认终止') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <AlertDialog
      :open="forceTerminateTarget !== null"
      @update:open="(open) => { if (!open && !forceTerminatePending) forceTerminateTarget = null }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ $t('强制终结卡住的实例？') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ $t('Runner 将按实例 ID 与资源标签清理实际资源，并在确认资源不存在、容量已释放后才把实例标记为 Stopped。 该操作只用于长时间停在 Provisioning 或 Stopping 的实例。') }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="platform-force-termination-reason">{{ $t('操作原因') }}</FieldLabel>
            <Textarea
              id="platform-force-termination-reason"
              v-model="forceTerminateReason"
              maxlength="512"
              :placeholder="$t('至少 8 个字符，用于定位本次资源清理操作')"
            />
            <FieldDescription>{{ forceTerminateReason.trim().length }}/512</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="platform-force-termination-confirm" v-model="forceTerminateConfirmed" />
            <FieldLabel for="platform-force-termination-confirm" class="font-normal">
              {{ $t('我确认这是卡住的实例，并理解清理失败时实例不会被强改为 Stopped。') }}
            </FieldLabel>
          </Field>
        </FieldGroup>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="forceTerminatePending">{{ $t('取消') }}</AlertDialogCancel>
          <Button
            type="button"
            variant="destructive"
            :disabled="forceTerminatePending || !forceTerminateConfirmed || forceTerminateReason.trim().length < 8"
            @click="submitForceTermination"
          >
            <Spinner v-if="forceTerminatePending" data-icon="inline-start" /> {{ $t('确认强制终结') }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
