<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminConfirmCheatIncident,
  adminCorrectCheatIncident,
  adminDismissCheatIncident,
  adminGetCheatIncident,
  adminListCheatIncidents,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse,
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentListItemResponse,
  NoCtfDomainSubmissionsCheatIncidentStatus,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

// ---- Filters + list ----
const filterStatus = ref('')
const filterFrom = ref('')
const filterTo = ref('')
const pendingCount = ref<number | null>(null)

const { items, loading, hasMore, loadMore, reset, initialized } = useCursorPagination<
  NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentListItemResponse
>(async (cursor) => {
  const { data, error } = await adminListCheatIncidents({
    path: { competitionId },
    query: {
      status: filterStatus.value === '' ? null : Number(filterStatus.value) as NoCtfDomainSubmissionsCheatIncidentStatus,
      from: localInputToIso(filterFrom.value) ?? '1970-01-01T00:00:00Z',
      to: localInputToIso(filterTo.value) ?? '2999-12-31T23:59:59Z',
      cursor,
      limit: 30,
    },
  })
  if (error) throw error
  pendingCount.value = data?.pendingCount ?? null
  return data ?? {}
})

function applyFilters() {
  reset()
  void loadMore()
}

// ---- Detail sheet ----
const detail = ref<NoCtfapiEndpointsAdministrationCheatIncidentsCheatIncidentDetailResponse | null>(null)
const detailOpen = ref(false)
const detailLoading = ref(false)
const showFlag = ref(false)

async function openDetail(scoringEventId?: string) {
  if (!scoringEventId) return
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  showFlag.value = false
  const { data, error } = await adminGetCheatIncident({ path: { competitionId, scoringEventId } })
  if (error) toast.error(parseApiError(error).message)
  else detail.value = data ?? null
  detailLoading.value = false
}

// ---- Actions ----
const actionDialog = ref<{ mode: 'confirm' | 'dismiss' | 'correct'; scoringEventId: string } | null>(null)
const actionReason = ref('')
const actionPending = ref(false)

const ActionMeta = {
  confirm: { title: '确认作弊', description: '确认该事件为作弊,将封禁来源队伍。' },
  dismiss: { title: '驳回事件', description: '驳回该作弊事件,不产生封禁。' },
  correct: { title: '纠正事件', description: '将此前确认的作弊标记为误判并解除相关封禁。' },
} as const

function openAction(mode: 'confirm' | 'dismiss' | 'correct', scoringEventId?: string) {
  if (!scoringEventId) return
  actionDialog.value = { mode, scoringEventId }
  actionReason.value = ''
}

async function submitAction() {
  const ctx = actionDialog.value
  if (!ctx || !actionReason.value.trim()) return
  actionPending.value = true
  try {
    const path = { competitionId, scoringEventId: ctx.scoringEventId }
    const body = { reason: actionReason.value.trim() }
    const { error } =
      ctx.mode === 'confirm' ? await adminConfirmCheatIncident({ path, body })
        : ctx.mode === 'dismiss' ? await adminDismissCheatIncident({ path, body })
          : await adminCorrectCheatIncident({ path, body })
    if (error) throw error
    toast.success('操作成功')
    actionDialog.value = null
    applyFilters()
    if (detailOpen.value && detail.value?.scoringEventId) await openDetail(detail.value.scoringEventId)
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    actionPending.value = false
  }
}

onMounted(() => void loadMore())
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3">
      <Select v-model="filterStatus">
        <SelectTrigger class="w-40">
          <SelectValue placeholder="状态(全部)" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem value="0">待处理</SelectItem>
            <SelectItem value="1">已确认</SelectItem>
            <SelectItem value="2">已驳回</SelectItem>
            <SelectItem value="3">已取代</SelectItem>
            <SelectItem value="4">已纠正</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <Input v-model="filterFrom" type="datetime-local" class="w-52" aria-label="起始时间" />
      <span class="text-sm text-muted-foreground">至</span>
      <Input v-model="filterTo" type="datetime-local" class="w-52" aria-label="结束时间" />
      <Button size="sm" @click="applyFilters">应用筛选</Button>
      <Badge v-if="pendingCount !== null && pendingCount > 0" variant="destructive">
        {{ pendingCount }} 件待处理
      </Badge>
    </div>

    <Skeleton v-if="loading && !initialized" class="h-48 w-full" />
    <Empty v-else-if="initialized && items.length === 0">
      <EmptyHeader>
        <EmptyTitle>暂无作弊事件</EmptyTitle>
      </EmptyHeader>
    </Empty>
    <template v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>来源队伍</TableHead>
            <TableHead>Flag 属主队伍</TableHead>
            <TableHead>题目</TableHead>
            <TableHead class="w-20">类型</TableHead>
            <TableHead class="w-24">状态</TableHead>
            <TableHead class="w-44">检测时间</TableHead>
            <TableHead class="w-40 text-right">操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="i in items" :key="i.scoringEventId">
            <TableCell class="font-medium">
              {{ i.sourceTeamName }}
              <Badge v-if="i.sourceTeamIsBanned" variant="destructive" class="ml-1">已封禁</Badge>
            </TableCell>
            <TableCell>{{ i.ownerTeamName }}</TableCell>
            <TableCell>{{ i.challengeTitle }}</TableCell>
            <TableCell>{{ enumLabel(SubmissionKindLabel, i.submissionKind) }}</TableCell>
            <TableCell>
              <Badge :variant="i.status === 0 ? 'secondary' : i.status === 1 ? 'destructive' : 'outline'">
                {{ enumLabel(CheatIncidentStatusLabel, i.status) }}
              </Badge>
            </TableCell>
            <TableCell>{{ adminFormatDateTime(i.detectedAt) }}</TableCell>
            <TableCell class="text-right">
              <Button variant="ghost" size="sm" @click="openDetail(i.scoringEventId)">详情</Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMore">
          <Spinner v-if="loading" data-icon="inline-start" />
          加载更多
        </Button>
      </div>
    </template>

    <Sheet v-model:open="detailOpen">
      <SheetContent class="overflow-y-auto">
        <SheetHeader>
          <SheetTitle>作弊事件详情</SheetTitle>
          <SheetDescription>事件 ID:{{ detail?.scoringEventId }}</SheetDescription>
        </SheetHeader>
        <Skeleton v-if="detailLoading" class="mx-4 h-48" />
        <div v-else-if="detail" class="flex flex-col gap-3 px-4 pb-4 text-sm">
          <div class="flex justify-between"><span class="text-muted-foreground">来源队伍</span><span>{{ detail.sourceTeamName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">Flag 属主</span><span>{{ detail.ownerTeamName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">题目</span><span>{{ detail.challengeTitle }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">提交人</span><span>{{ detail.submittedByUserName }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">类型</span><span>{{ enumLabel(SubmissionKindLabel, detail.submissionKind) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">结果</span><span>{{ enumLabel(ScoringResultLabel, detail.result) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">状态</span><span>{{ enumLabel(CheatIncidentStatusLabel, detail.status) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">提交时间</span><span>{{ adminFormatDateTime(detail.submittedAt) }}</span></div>
          <div class="flex justify-between"><span class="text-muted-foreground">检测时间</span><span>{{ adminFormatDateTime(detail.detectedAt) }}</span></div>
          <template v-if="detail.resolvedByUserName">
            <div class="flex justify-between"><span class="text-muted-foreground">处理人</span><span>{{ detail.resolvedByUserName }}</span></div>
            <div class="flex justify-between"><span class="text-muted-foreground">处理时间</span><span>{{ adminFormatDateTime(detail.resolvedAt) }}</span></div>
            <div class="flex flex-col gap-1"><span class="text-muted-foreground">处理理由</span><span class="whitespace-pre-wrap">{{ detail.resolutionReason }}</span></div>
          </template>
          <template v-if="detail.sourceTeamIsBanned">
            <Separator />
            <div class="flex justify-between"><span class="text-muted-foreground">封禁时间</span><span>{{ adminFormatDateTime(detail.sourceTeamBannedAt) }}</span></div>
            <div class="flex flex-col gap-1"><span class="text-muted-foreground">封禁原因</span><span class="whitespace-pre-wrap">{{ detail.sourceTeamBanReason ?? '—' }}</span></div>
          </template>
          <Separator />
          <div class="flex flex-col gap-2">
            <span class="text-muted-foreground">提交的 Flag(证据)</span>
            <Button variant="outline" size="sm" class="w-fit" @click="showFlag = !showFlag">
              {{ showFlag ? '隐藏' : '显示完整 Flag' }}
            </Button>
            <div v-if="showFlag" class="rounded-md border bg-muted p-3 font-mono text-xs break-all">
              {{ detail.submittedFlag }}
            </div>
          </div>
          <div v-if="canWrite && (detail.canConfirm || detail.canDismiss || detail.canCorrect)" class="flex flex-wrap gap-2 pt-2">
            <Button v-if="detail.canConfirm" variant="destructive" size="sm" @click="openAction('confirm', detail!.scoringEventId)">
              确认作弊(封禁)
            </Button>
            <Button v-if="detail.canDismiss" variant="outline" size="sm" @click="openAction('dismiss', detail!.scoringEventId)">
              驳回
            </Button>
            <Button v-if="detail.canCorrect" variant="outline" size="sm" @click="openAction('correct', detail!.scoringEventId)">
              纠正(解封)
            </Button>
          </div>
        </div>
      </SheetContent>
    </Sheet>

    <AlertDialog :open="actionDialog !== null" @update:open="(v) => { if (!v) actionDialog = null }">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ actionDialog ? ActionMeta[actionDialog.mode].title : '' }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ actionDialog ? ActionMeta[actionDialog.mode].description : '' }}必须填写理由(记入审计)。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <div class="px-1 pb-2">
          <Textarea v-model="actionReason" placeholder="理由" aria-label="理由" />
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction
            :variant="actionDialog?.mode === 'confirm' ? 'destructive' : 'default'"
            :disabled="actionPending || !actionReason.trim()"
            @click="submitAction"
          >
            <Spinner v-if="actionPending" data-icon="inline-start" />
            确认
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
