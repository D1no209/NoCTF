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
import { emptyPlatformRuntimeFilters, platformRuntimeQuery } from '~/utils/platform-runtime-filters'

definePageMeta({ middleware: 'platform-admin' })

type PlatformRuntime = NoCtfapiEndpointsAdministrationPlatformPlatformRuntimeResponse

const filters = reactive(emptyPlatformRuntimeFilters())
const appliedQuery = ref(platformRuntimeQuery(filters))
const detailTarget = ref<PlatformRuntime | null>(null)

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
    query: { ...appliedQuery.value, cursor, limit: 50 },
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

const detail = computed(() => items.value.find(item => item.runtime?.id === detailTarget.value?.runtime?.id) ?? detailTarget.value)

async function applyFilters(): Promise<void> {
  appliedQuery.value = platformRuntimeQuery(filters)
  reset()
  await refresh()
}

function clearFilters(): void {
  Object.assign(filters, emptyPlatformRuntimeFilters())
  void applyFilters()
}

const terminateTarget = ref<PlatformRuntime | null>(null)
const terminatePending = ref(false)
const terminationError = ref<string | null>(null)
const forceTerminateTarget = ref<PlatformRuntime | null>(null)
const forceTerminateReason = ref('')
const forceTerminateConfirmed = ref(false)
const forceTerminatePending = ref(false)
const forceTerminationError = ref<string | null>(null)

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

function openTermination(item: PlatformRuntime): void {
  terminationError.value = null
  terminateTarget.value = item
}

function openForceTermination(item: PlatformRuntime): void {
  forceTerminationError.value = null
  forceTerminateTarget.value = item
  forceTerminateReason.value = ''
  forceTerminateConfirmed.value = false
}

async function submitTermination(): Promise<void> {
  const runtime = runtimeOf(terminateTarget.value)
  if (!runtime?.id || terminatePending.value) return
  terminatePending.value = true
  terminationError.value = null
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
    terminationError.value = parseApiError(requestError).message
    toast.error(terminationError.value)
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
  forceTerminationError.value = null
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
    forceTerminationError.value = parseApiError(requestError).message
    toast.error(forceTerminationError.value)
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
  <div class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <h2 class="text-lg font-semibold">{{ $t('运行容器') }}</h2>
      <Button variant="outline" size="sm" :disabled="loading" @click="refresh">
        <Spinner v-if="loading" data-icon="inline-start" />
        <RefreshCw v-else data-icon="inline-start" />
        {{ $t('刷新') }}
      </Button>
    </div>
    <Card>
      <CardHeader class="sr-only">
        <CardTitle>{{ $t('应用筛选') }}</CardTitle>
        <CardDescription>{{ $t('仅显示活动容器，可按赛事、题目或来源队伍搜索。') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="applyFilters">
          <FieldGroup class="grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(12rem,1fr)_repeat(3,minmax(7rem,9rem))_auto] xl:items-end">
            <Field>
              <FieldLabel for="platform-runtime-search" class="sr-only">{{ $t('搜索赛事、题目或队伍') }}</FieldLabel>
              <Input id="platform-runtime-search" v-model="filters.search" maxlength="200" :placeholder="$t('搜索赛事、题目或队伍')" />
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-scope" class="sr-only">{{ $t('来源(全部)') }}</FieldLabel>
              <Select v-model="filters.scope">
                <SelectTrigger id="platform-runtime-scope" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('来源(全部)') }}</SelectItem>
                  <SelectItem value="Competition">{{ $t('赛事容器') }}</SelectItem>
                  <SelectItem value="ChallengeTest">{{ $t('题目测试') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-state" class="sr-only">{{ $t('状态(全部)') }}</FieldLabel>
              <Select v-model="filters.state">
                <SelectTrigger id="platform-runtime-state" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('状态(全部)') }}</SelectItem>
                  <SelectItem value="Queued">{{ $t('排队中') }}</SelectItem>
                  <SelectItem value="Provisioning">{{ $t('准备中') }}</SelectItem>
                  <SelectItem value="Running">{{ $t('运行中') }}</SelectItem>
                  <SelectItem value="Stopping">{{ $t('停止中') }}</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="platform-runtime-kind" class="sr-only">{{ $t('类型(全部)') }}</FieldLabel>
              <Select v-model="filters.kind">
                <SelectTrigger id="platform-runtime-kind" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent><SelectGroup>
                  <SelectItem value="all">{{ $t('类型(全部)') }}</SelectItem>
                  <SelectItem value="Container">{{ $t('容器') }}</SelectItem>
                  <SelectItem value="Compose">Compose</SelectItem>
                </SelectGroup></SelectContent>
              </Select>
            </Field>
            <Field orientation="horizontal">
              <Button type="submit" size="sm" :disabled="loading">
                <Spinner v-if="loading" data-icon="inline-start" />{{ $t('应用筛选') }}
              </Button>
              <Button type="button" variant="ghost" size="sm" :disabled="loading" @click="clearFilters">{{ $t('清空') }}</Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
    <div class="flex flex-wrap justify-between gap-2 text-xs text-muted-foreground" role="status">
      <span>{{ $t('当前已载入 {count} 个活动容器', { count: items.length }) }}</span>
      <span>{{ $t('页面每 10 秒自动刷新') }}</span>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading && !initialized" class="h-64 w-full" />
    <Empty v-else-if="initialized && items.length === 0 && !error" class="border border-dashed py-14">
      <EmptyHeader>
        <EmptyTitle>{{ $t('没有符合条件的运行时实例') }}</EmptyTitle>
        <EmptyDescription>{{ $t('排队、创建中、运行中和停止中的 Container/Compose 实例会显示在这里。') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <template v-else-if="items.length > 0">
      <Table class="min-w-[48rem] table-fixed">
        <TableHeader>
          <TableRow>
            <TableHead class="w-[20%]">{{ $t('队伍') }}</TableHead>
            <TableHead class="w-[29%]">{{ $t('赛事 / 题目') }}</TableHead>
            <TableHead class="w-[9%]">{{ $t('类型') }}</TableHead>
            <TableHead class="w-[10%]">{{ $t('状态') }}</TableHead>
            <TableHead class="w-[17%]">{{ $t('到期时间') }}</TableHead>
            <TableHead class="w-[15%] text-right">{{ $t('操作') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.runtime?.id">
            <TableCell class="whitespace-normal break-words font-medium">{{ teamLabel(item) }}</TableCell>
            <TableCell class="whitespace-normal break-words">
              <div class="grid gap-1">
                <span>{{ item.challengeTitle ?? '-' }}</span>
                <span class="text-xs text-muted-foreground">{{ item.scope === 'ChallengeTest' ? $t('题目测试') : item.competitionTitle ?? '-' }}</span>
              </div>
            </TableCell>
            <TableCell class="whitespace-normal break-words">
              {{ enumLabel(RuntimeKindLabel, item.runtime?.runtimeKind) }}
            </TableCell>
            <TableCell>
              <Badge :variant="stateBadgeVariant(item.runtime?.state)">
                {{ enumLabel(RuntimeStateLabel, item.runtime?.state) }}
              </Badge>
            </TableCell>
            <TableCell class="whitespace-normal font-mono text-xs tabular-nums">
              {{ adminFormatDateTime(item.runtime?.expiresAt) }}
            </TableCell>
            <TableCell class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <Button variant="ghost" size="sm" @click="detailTarget = item">{{ $t('详情') }}</Button>
                <Button
                  v-if="canTerminate(item)"
                  variant="destructive"
                  size="sm"
                  :disabled="terminatePending || forceTerminatePending"
                  @click="openTermination(item)"
                >
                  {{ $t('终止') }}
                </Button>
                <Button
                  v-if="item.runtime?.canForceTerminate"
                  variant="destructive"
                  size="sm"
                  :disabled="terminatePending || forceTerminatePending"
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
    </template>

    <Sheet :open="detailTarget !== null" @update:open="(open) => { if (!open) detailTarget = null }">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>{{ $t('运行时详情') }}</SheetTitle>
          <SheetDescription class="break-all font-mono text-xs">{{ detail?.runtime?.id }}</SheetDescription>
        </SheetHeader>
        <div v-if="detail" class="flex flex-col gap-4 px-4 pb-4 text-sm">
          <dl class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-3">
            <dt class="text-muted-foreground">{{ $t('队伍') }}</dt><dd class="break-words">{{ teamLabel(detail) }}</dd>
            <dt class="text-muted-foreground">{{ $t('赛事 / 题目') }}</dt>
            <dd class="break-words">{{ detail.challengeTitle }}<p class="mt-1 text-xs text-muted-foreground">{{ detail.competitionTitle ?? $t('题目测试') }}</p></dd>
            <dt class="text-muted-foreground">{{ $t('类型') }}</dt><dd>{{ enumLabel(RuntimeKindLabel, detail.runtime?.runtimeKind) }}</dd>
            <dt class="text-muted-foreground">{{ $t('状态') }}</dt><dd><Badge :variant="stateBadgeVariant(detail.runtime?.state)">{{ enumLabel(RuntimeStateLabel, detail.runtime?.state) }}</Badge></dd>
            <dt class="text-muted-foreground">{{ $t('运行位置') }}</dt><dd class="break-all">{{ enumLabel(RuntimeProviderLabel, detail.runtime?.provider) }}<p class="mt-1 font-mono text-xs">{{ detail.runtime?.runnerId ?? $t('尚未分配 Runner') }}</p></dd>
            <dt class="text-muted-foreground">{{ $t('创建时间') }}</dt><dd class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(detail.runtime?.createdAt) }}</dd>
            <dt class="text-muted-foreground">{{ $t('到期时间') }}</dt><dd class="font-mono text-xs tabular-nums">{{ adminFormatDateTime(detail.runtime?.expiresAt) }}</dd>
          </dl>
          <template v-if="detail.runtime?.urls?.length">
            <Separator />
            <p class="font-medium">{{ $t('访问入口') }}</p>
            <code v-for="url in detail.runtime.urls" :key="url" class="whitespace-pre-wrap break-all text-xs">{{ url }}</code>
          </template>
          <Separator />
          <Button v-if="detail.runtime?.competitionId" variant="outline" as-child>
            <NuxtLink :to="`/admin/competitions/${detail.runtime.competitionId}/runtimes`"><ExternalLink data-icon="inline-start" />{{ $t('赛事运行时') }}</NuxtLink>
          </Button>
          <Button v-else-if="detail.runtime?.challengeId" variant="outline" as-child>
            <NuxtLink :to="`/admin/challenges/${detail.runtime.challengeId}`"><ExternalLink data-icon="inline-start" />{{ $t('题目模板') }}</NuxtLink>
          </Button>
        </div>
      </SheetContent>
    </Sheet>

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
        <Alert v-if="terminationError" variant="destructive">
          <AlertDescription>{{ terminationError }}</AlertDescription>
        </Alert>
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
          <Alert v-if="forceTerminationError" variant="destructive">
            <AlertDescription>{{ forceTerminationError }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="platform-force-termination-reason">{{ $t('操作原因') }}</FieldLabel>
            <Textarea
              id="platform-force-termination-reason"
              v-model="forceTerminateReason"
              :disabled="forceTerminatePending"
              maxlength="512"
              :placeholder="$t('至少 8 个字符，用于定位本次资源清理操作')"
            />
            <FieldDescription>{{ forceTerminateReason.trim().length }}/512</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="platform-force-termination-confirm" v-model="forceTerminateConfirmed" :disabled="forceTerminatePending" />
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
