<script setup lang="ts">
import { adminGetCompetition, listCompetitionEvents } from '~/api'
import type { NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse, NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol, NoCtfapiEndpointsCompetitionsEventsCompetitionEventLevelProtocol } from '~/api'
import { competitionEventHistoryRange } from '~/lib/competition-event-history'

type CompetitionEvent = NoCtfapiEndpointsCompetitionsEventsCompetitionEventResponse

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const { canOrganize, isAdministrator } = useAuth()
const hasStaffHistory = ref(isAdministrator.value)
const historyScopeResolved = ref(false)
const historyScopeError = ref<string | null>(null)

const initialKind = typeof route.query.kind === 'string' ? route.query.kind : 'all'
const kind = ref<string>(initialKind)

const kindOptions = [
  { value: 'all', label: '全部动态' },
  { value: 'CompetitionLifecycleChanged', label: '比赛状态' },
  { value: 'AnnouncementPublished', label: '公告' }, { value: 'ChallengePublished', label: '题目发布' }, { value: 'HintPublished', label: '提示发布' },
  { value: 'FirstBloodAwarded', label: '一血' }, { value: 'SecondBloodAwarded', label: '二血' }, { value: 'ThirdBloodAwarded', label: '三血' },
  { value: 'GameplayFactAdjudicated', label: '提交评测' }, { value: 'TeamRegistered', label: '队伍报名' }, { value: 'TeamBanned', label: '队伍封禁' },
  { value: 'QuestionOpened', label: '咨询创建' }, { value: 'QuestionReplied', label: '咨询回复' }, { value: 'QuestionStatusChanged', label: '咨询状态' },
]

const { items, loading, error, hasMore, initialized, loadMore, reset } =
  useCursorPagination<CompetitionEvent>(async (cursor) => {
    const competition = ctx.competition.value
    const range = competitionEventHistoryRange(
      hasStaffHistory.value,
      competition?.startTime,
    )
    const { data, error: err } = await listCompetitionEvents({
      path: { competitionId },
      query: {
        from: range.from,
        to: range.to,
        kind: kind.value === 'all' ? null : kind.value as NoCtfapiEndpointsCompetitionsEventsCompetitionEventKindProtocol,
        cursor,
        limit: 50,
      },
    })
    if (err || !data) throw err ?? new Error(translate("加载失败"))
    return { items: data.items, nextCursor: data.nextCursor }
  })

async function resolveHistoryScope() {
  if (!hasStaffHistory.value && canOrganize.value) {
    const { data, error: requestError, response } = await adminGetCompetition({
      path: { competitionId },
    })
    hasStaffHistory.value = data?.administrationRole != null
    if (requestError && response?.status !== 403 && response?.status !== 404) {
      historyScopeError.value = parseApiError(
        requestError,
        translate('无法确认动态历史访问范围，当前仅显示最近 30 天。'),
      ).message
    }
  }
  historyScopeResolved.value = true
}

// 竞赛详情就绪后才开始加载
watch(
  () => ctx.competition.value,
  (competition, previous) => {
    if (competition && historyScopeResolved.value && !initialized.value) void loadMore()
  },
  { immediate: true },
)

await resolveHistoryScope()
if (ctx.competition.value && !initialized.value) await loadMore()

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
      <div class="flex items-center gap-2">
        <Select v-model="kind">
          <SelectTrigger class="w-40">
            <SelectValue :placeholder="$t('全部动态')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
                {{ $t(option.label) }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
        <Badge variant="outline">
          {{ $t(hasStaffHistory ? '完整历史' : '最近 30 天') }}
        </Badge>
      </div>
      <Button variant="outline" size="sm" @click="reload">{{ $t('刷新') }}</Button>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error.message }}</AlertDescription>
    </Alert>

    <Alert v-if="historyScopeError" variant="destructive">
      <AlertDescription>{{ historyScopeError }}</AlertDescription>
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
