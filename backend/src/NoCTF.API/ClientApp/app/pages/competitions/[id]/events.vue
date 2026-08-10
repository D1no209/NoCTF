<script setup lang="ts">
import { listCompetitionEvents } from '~/api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse, NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol } from '~/api'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!

const initialKind = typeof route.query.kind === 'string' ? route.query.kind : 'all'
const kind = ref<string>(initialKind)

const kindOptions = [
  { value: 'all', label: translate("全部动态") },
  { value: 'CompetitionLifecycleChanged', label: translate("比赛状态") },
  { value: 'AnnouncementPublished', label: translate("公告") }, { value: 'ChallengePublished', label: translate("题目发布") }, { value: 'HintPublished', label: translate("提示发布") },
  { value: 'FirstBloodAwarded', label: translate("一血") }, { value: 'SecondBloodAwarded', label: translate("二血") }, { value: 'ThirdBloodAwarded', label: translate("三血") },
  { value: 'GameplayFactAdjudicated', label: translate("提交评测") }, { value: 'TeamRegistered', label: translate("队伍报名") }, { value: 'TeamBanned', label: translate("队伍封禁") },
  { value: 'QuestionOpened', label: translate("咨询创建") }, { value: 'QuestionReplied', label: translate("咨询回复") }, { value: 'QuestionStatusChanged', label: translate("咨询状态") },
]

const { items, loading, error, hasMore, initialized, loadMore, reset } =
  useCursorPagination<CompetitionEvent>(async (cursor) => {
    const competition = ctx.competition.value
    const now = Date.now()
    const start = competition?.startTime ? new Date(competition.startTime).getTime() : now
    // 后端限制 from/to 跨度 ≤31 天
    const from = new Date(Math.max(start, now - 30 * 24 * 3600 * 1000)).toISOString()
    const { data, error: err } = await listCompetitionEvents({
      path: { competitionId },
      query: {
        from,
        to: new Date(now).toISOString(),
        kind: kind.value === 'all' ? null : kind.value as NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
        cursor,
        limit: 50,
      },
    })
    if (err || !data) throw err ?? new Error(translate("加载失败"))
    return { items: data.items, nextCursor: data.nextCursor }
  })

// 竞赛详情就绪后才开始加载
watch(
  () => ctx.competition.value,
  (competition, previous) => {
    if (competition && !initialized.value) void loadMore()
  },
  { immediate: true },
)

watch(kind, () => {
  reset()
  void loadMore()
})

function reload() {
  reset()
  void loadMore()
}

// 实时:竞赛事件变更 → 全量刷新列表
let unwatch: (() => void) | undefined
onMounted(() => {
  unwatch = watchCompetition(competitionId, {
    competitionEventChanged: () => reload(),
  })
})
onUnmounted(() => unwatch?.())

const levelVariant = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) =>
  level === 'Error' ? ('destructive' as const) : level === 'Warning' ? ('secondary' as const) : ('outline' as const)
const levelLabel = (level?: NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol) => (level === 'Error' ? translate("警告") : level === 'Warning' ? translate("注意") : translate("信息"))
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between gap-2">
      <Select v-model="kind">
        <SelectTrigger class="w-40">
          <SelectValue :placeholder="$t('全部动态')" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Button variant="outline" size="sm" @click="reload">{{ $t('刷新') }}</Button>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <div v-if="loading && !initialized" class="flex flex-col gap-2">
      <Skeleton v-for="i in 6" :key="i" class="h-12 w-full" />
    </div>

    <Empty v-else-if="initialized && !items.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('暂无动态') }}</EmptyTitle>
        <EmptyDescription>{{ $t('公告、题目发布、血榜等动态会出现在这里') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <ul v-else class="flex flex-col gap-2">
      <li
        v-for="event in items"
        :key="event.id"
        class="flex items-start gap-3 rounded-md border px-3 py-2"
      >
        <Badge :variant="levelVariant(event.level)" class="mt-0.5 shrink-0">
          {{ levelLabel(event.level) }}
        </Badge>
        <div class="flex min-w-0 flex-col gap-0.5">
          <p class="text-sm">{{ competitionEventText(event) }}</p>
          <p class="font-mono text-xs text-muted-foreground tabular-nums">{{ formatDateTime(event.occurredAt) }}</p>
        </div>
      </li>
    </ul>

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" :disabled="loading" @click="loadMore">
        <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('加载更多') }} </Button>
    </div>
  </div>
</template>
