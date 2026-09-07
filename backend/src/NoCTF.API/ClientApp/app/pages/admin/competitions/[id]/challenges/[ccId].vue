<script setup lang="ts">
import { Plus } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminChallengeConfigurationGet,
  adminChallengeConfigurationUpdate,
  adminCompetitionConfigurationGet,
  adminCreateManualAdjustment,
  adminCreateCompetitionChallengeHint,
  adminDeleteCompetitionChallengeHint,
  adminGetCompetitionChallenge,
  adminListGameplayFacts,
  adminListCompetitionChallengeHints,
  adminListTeams,
  adminRestoreCompetitionChallengeHint,
  adminUpdateCompetitionChallenge,
  adminUpdateCompetitionChallengeHint,
  getLeaderboardEndpoint,
  getScoreboardSchemaEndpoint,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengesChallengeConfigurationResponse,
  NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse,
  NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse,
  NoCtfapiEndpointsGameplayFactsGameplayFactListResponse,
  NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import type { GameModeValue } from '~/utils/game-config'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const ccId = route.params.ccId as string
const { competitionId, competition, canWrite, canJudge } = useCompetitionAdmin()

// ---- Challenge detail ----
const challenge = ref<NoCtfapiEndpointsChallengesChallengeResponse | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const activeSection = ref('general')

async function loadChallenge() {
  loading.value = true
  const { data, error } = await adminGetCompetitionChallenge({
    path: { competitionId, competitionChallengeId: ccId },
    query: { includeDeleted: false },
  })
  if (error) loadError.value = parseApiError(error).message
  else challenge.value = data ?? null
  loading.value = false
}

// ---- Edit form ----
const editCustomTitle = ref('')
const editOrder = ref(0)
const editPublished = ref(false)
const savingEdit = ref(false)

watch(challenge, (c) => {
  if (!c) return
  editCustomTitle.value = c.customTitle ?? ''
  editOrder.value = c.order ?? 0
  editPublished.value = c.isPublished ?? false
}, { immediate: true })

async function saveEdit() {
  if (!challenge.value) return
  savingEdit.value = true
  try {
    const { data, error } = await adminUpdateCompetitionChallenge({
      path: { competitionId, competitionChallengeId: ccId },
      body: {
        customTitle: editCustomTitle.value.trim() || null,
        order: editOrder.value,
        isPublished: editPublished.value,
      },
    })
    if (error) throw error
    challenge.value = data ?? challenge.value
    toast.success(translate("题目设置已保存"))
  }
  catch (e) {
    toastWriteError(e)
  }
  finally {
    savingEdit.value = false
  }
}

// ---- Per-challenge configuration ----
const config = ref<NoCtfapiEndpointsAdministrationChallengesChallengeConfigurationResponse | null>(null)
const configLoading = ref(true)
const savingConfig = ref(false)
const inheritedConfigJson = ref<string | null>(null)

async function loadConfig() {
  configLoading.value = true
  const [challengeResult, competitionResult] = await Promise.all([
    adminChallengeConfigurationGet({
      path: { competitionId, competitionChallengeId: ccId },
    }),
    adminCompetitionConfigurationGet({ path: { competitionId } }),
  ])
  if (!challengeResult.error && challengeResult.data) config.value = challengeResult.data
  if (!competitionResult.error && competitionResult.data) {
    inheritedConfigJson.value = competitionResult.data.json ?? null
  }
  configLoading.value = false
}

async function saveConfig(json: string) {
  if (!config.value) return
  savingConfig.value = true
  try {
    const { data, error } = await adminChallengeConfigurationUpdate({
      path: { competitionId, competitionChallengeId: ccId },
      body: { json },
    })
    if (error) throw error
    config.value = data ?? config.value
    toast.success(translate("题目配置已保存"))
  }
  catch (e) {
    toastWriteError(e)
  }
  finally {
    savingConfig.value = false
  }
}

// ---- Hints ----
const hints = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse[]>([])
const hintsLoading = ref(true)
const hintsLoadError = ref<string | null>(null)
const includeDeletedHints = ref(false)
const hintDialogOpen = ref(false)
const editingHint = ref<NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse | null>(null)
const hintForm = ref({ content: '', cost: 0, publishedAt: '' })
const hintError = ref<string | null>(null)
const savingHint = ref(false)
const pendingHintId = ref<string | null>(null)

async function loadHints() {
  hintsLoading.value = true
  const { data, error } = await adminListCompetitionChallengeHints({
    path: { competitionId, competitionChallengeId: ccId },
    query: { includeDeleted: includeDeletedHints.value },
  })
  if (error || !data) {
    hintsLoadError.value = parseApiError(error).message
  }
  else {
    hintsLoadError.value = null
    hints.value = data.items ?? []
  }
  hintsLoading.value = false
}
watch(includeDeletedHints, loadHints)

function openHintDialog(hint?: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
  editingHint.value = hint ?? null
  hintError.value = null
  hintForm.value = {
    content: hint?.content ?? '',
    cost: hint?.cost ?? 0,
    publishedAt: isoToLocalInput(hint?.publishedAt),
  }
  hintDialogOpen.value = true
}

async function saveHint() {
  if (!hintForm.value.content.trim()) {
    hintError.value = translate('请输入提示内容')
    return
  }
  savingHint.value = true
  hintError.value = null
  const body = {
    content: hintForm.value.content,
    cost: hintForm.value.cost,
    publishedAt: localInputToIso(hintForm.value.publishedAt) ?? null,
  }
  try {
    const path = { competitionId, competitionChallengeId: ccId }
    const { error } = editingHint.value?.id
      ? await adminUpdateCompetitionChallengeHint({ path: { ...path, hintId: editingHint.value.id }, body })
      : await adminCreateCompetitionChallengeHint({ path, body })
    if (error) throw error
    toast.success(editingHint.value ? translate("提示已更新") : translate("提示已添加"))
    hintDialogOpen.value = false
    await loadHints()
  }
  catch (e) {
    hintError.value = parseApiError(e).message
  }
  finally {
    savingHint.value = false
  }
}

async function deleteHint(h: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
  if (!h.id) return
  pendingHintId.value = h.id
  try {
    const { error } = await adminDeleteCompetitionChallengeHint({
      path: { competitionId, competitionChallengeId: ccId, hintId: h.id },
    })
    if (error) throw error
    toast.success(translate("提示已删除"))
    await loadHints()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    pendingHintId.value = null
  }
}

async function restoreHint(h: NoCtfapiEndpointsAdministrationChallengesChallengeHintResponse) {
  if (!h.id) return
  pendingHintId.value = h.id
  try {
    const { error } = await adminRestoreCompetitionChallengeHint({
      path: { competitionId, competitionChallengeId: ccId, hintId: h.id },
    })
    if (error) throw error
    toast.success(translate("提示已恢复"))
    await loadHints()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    pendingHintId.value = null
  }
}

// ---- Per-team challenge scoring ----
interface ChallengeTeamScoringRow {
  team: NoCtfapiEndpointsTeamsTeamResponse
  score: number
  adjustment: number
  progressLabel: string
  progressVariant: 'default' | 'secondary' | 'outline'
}

const scoringTeams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])
const scoringFacts = ref<NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[]>([])
const scoringSnapshot = ref<NoCtfapiEndpointsCompetitionsScoreboardSnapshotResponse | null>(null)
const scoringSchema = ref<NoCtfapiEndpointsCompetitionsScoreboardSchemaResponse | null>(null)
const scoringLoading = ref(true)
const scoringError = ref<string | null>(null)
let scoringLoadGeneration = 0

async function loadAllChallengeFacts(): Promise<NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[]> {
  const facts: NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[] = []
  const seenCursors = new Set<string>()
  let cursor: string | null = null
  do {
    const response: {
      data?: NoCtfapiEndpointsGameplayFactsGameplayFactListResponse
      error?: unknown
    } = await adminListGameplayFacts({
      path: { competitionId },
      query: { competitionChallengeId: ccId, cursor, limit: 200 },
    })
    if (response.error || !response.data)
      throw response.error ?? new Error(translate('加载本题判分记录失败'))
    facts.push(...(response.data.items ?? []))
    cursor = response.data.nextCursor ?? null
    if (cursor && seenCursors.has(cursor)) throw new Error(translate('判分记录分页游标重复'))
    if (cursor) seenCursors.add(cursor)
  } while (cursor)
  return facts
}

async function loadChallengeTeamScoring(): Promise<void> {
  const generation = ++scoringLoadGeneration
  scoringLoading.value = true
  scoringError.value = null
  try {
    const [teamsResult, facts, leaderboardResult, schemaResult] = await Promise.all([
      adminListTeams({ path: { competitionId } }),
      loadAllChallengeFacts(),
      getLeaderboardEndpoint({ path: { competitionId } }),
      getScoreboardSchemaEndpoint({ path: { competitionId } }),
    ])
    if (generation !== scoringLoadGeneration) return
    if (teamsResult.error || !teamsResult.data) throw teamsResult.error ?? new Error(translate('加载队伍失败'))
    scoringTeams.value = teamsResult.data.items ?? []
    scoringFacts.value = facts
    scoringSnapshot.value = leaderboardResult.data && 'teams' in leaderboardResult.data
      ? leaderboardResult.data
      : null
    scoringSchema.value = schemaResult.data && 'columns' in schemaResult.data
      ? schemaResult.data
      : null
  }
  catch (requestError) {
    if (generation === scoringLoadGeneration)
      scoringError.value = parseApiError(requestError, translate('加载本题队伍判分失败')).message
  }
  finally {
    if (generation === scoringLoadGeneration) scoringLoading.value = false
  }
}

function teamFacts(teamId?: string): NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[] {
  return teamId ? scoringFacts.value.filter(fact => fact.teamId === teamId) : []
}

function hasSuccessfulFact(
  facts: NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse[],
  kind: NoCtfapiEndpointsGameplayFactsGameplayFactListItemResponse['kind'],
): boolean {
  return facts.some(fact => fact.kind === kind
    && (fact.result === 'Correct' || fact.result === 'Controlled'))
}

function progressForTeam(teamId?: string): Pick<ChallengeTeamScoringRow, 'progressLabel' | 'progressVariant'> {
  const facts = teamFacts(teamId)
  switch (competition.value?.mode) {
    case 'Awdp': {
      const attack = hasSuccessfulFact(facts, 'BreakAttempt')
      const defense = hasSuccessfulFact(facts, 'FixAttempt')
      if (attack && defense) return { progressLabel: translate('攻击与防御成功'), progressVariant: 'default' }
      if (attack) return { progressLabel: translate('攻击成功'), progressVariant: 'secondary' }
      if (defense) return { progressLabel: translate('防御成功'), progressVariant: 'secondary' }
      return { progressLabel: translate('尚未成功'), progressVariant: 'outline' }
    }
    case 'Awd':
      return hasSuccessfulFact(facts, 'FlagAttempt')
        ? { progressLabel: translate('攻击成功'), progressVariant: 'default' }
        : { progressLabel: translate('尚未攻击成功'), progressVariant: 'outline' }
    case 'Koh':
      return hasSuccessfulFact(facts, 'KohControlObservation')
        ? { progressLabel: translate('已取得控制'), progressVariant: 'default' }
        : { progressLabel: translate('尚未取得控制'), progressVariant: 'outline' }
    default:
      return hasSuccessfulFact(facts, 'FlagAttempt')
        ? { progressLabel: translate('已解出'), progressVariant: 'default' }
        : { progressLabel: translate('尚未解出'), progressVariant: 'outline' }
  }
}

function manualAdjustmentForTeam(teamId?: string): number {
  return teamFacts(teamId)
    .filter(fact => fact.kind === 'ManualAdjustment' && fact.result === 'Applied')
    .reduce((total, fact) => total + (Number.parseInt(fact.value ?? '0', 10) || 0), 0)
}

function projectedChallengeScore(teamId?: string): number {
  if (!teamId) return 0
  const snapshotTeam = scoringSnapshot.value?.teams?.find(team => team.teamId === teamId)
  if (!snapshotTeam) return manualAdjustmentForTeam(teamId)
  const adjustment = manualAdjustmentForTeam(teamId)
  if (competition.value?.mode === 'Awdp') {
    const score = snapshotTeam.challengeScores?.find(item => item.competitionChallengeId === ccId)
    return (score?.attackScore ?? 0) + (score?.defenseScore ?? 0) + adjustment
  }
  const indexes = new Set(scoringSchema.value?.columns
    ?.filter(column => column.competitionChallengeId === ccId)
    .map(column => column.index)
    .filter((index): index is number => index !== undefined) ?? [])
  const slotScore = snapshotTeam.slots
    ?.filter(slot => slot.columnIndex !== undefined && indexes.has(slot.columnIndex))
    .reduce((total, slot) => total + (slot.netPoints ?? 0), 0) ?? 0
  return slotScore + adjustment
}

const scoringDisplayNames = computed(() => buildTeamDisplayNames(scoringTeams.value))
const scoringRows = computed<ChallengeTeamScoringRow[]>(() => scoringTeams.value
  .map((team) => {
    const adjustment = manualAdjustmentForTeam(team.id)
    return {
      team,
      score: projectedChallengeScore(team.id),
      adjustment,
      ...progressForTeam(team.id),
    }
  })
  .sort((left, right) => teamDisplayName(left.team, scoringDisplayNames.value)
    .localeCompare(teamDisplayName(right.team, scoringDisplayNames.value))))

const adjustmentTarget = ref<ChallengeTeamScoringRow | null>(null)
const adjustmentDelta = ref(0)
const adjustmentPending = ref(false)
const adjustmentError = ref<string | null>(null)
const adjustmentValid = computed(() => Number.isInteger(adjustmentDelta.value) && adjustmentDelta.value !== 0)

function openAdjustment(row: ChallengeTeamScoringRow): void {
  adjustmentTarget.value = row
  adjustmentDelta.value = 0
  adjustmentError.value = null
}

function closeAdjustment(open: boolean): void {
  if (!open && !adjustmentPending.value) adjustmentTarget.value = null
}

async function submitAdjustment(): Promise<void> {
  const teamId = adjustmentTarget.value?.team.id
  if (!teamId || !adjustmentValid.value || adjustmentPending.value) return
  adjustmentPending.value = true
  adjustmentError.value = null
  try {
    const { error } = await adminCreateManualAdjustment({
      path: { competitionId },
      body: { teamId, competitionChallengeId: ccId, delta: adjustmentDelta.value },
    })
    if (error) throw error
    toast.success(translate('本题判分已修正'))
    adjustmentTarget.value = null
    await loadChallengeTeamScoring()
  }
  catch (requestError) {
    adjustmentError.value = parseApiError(requestError, translate('修正本题判分失败')).message
  }
  finally {
    adjustmentPending.value = false
  }
}

onMounted(() => {
  void loadChallenge()
  void loadConfig()
  void loadHints()
  void loadChallengeTeamScoring()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <Button variant="ghost" size="sm" as-child class="w-fit">
      <NuxtLink :to="`/admin/competitions/${competitionId}/challenges`">{{ $t('← 返回题目列表') }}</NuxtLink>
    </Button>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>
    <Skeleton v-else-if="loading" class="h-32 w-full" />

    <template v-else-if="challenge">
      <div class="flex flex-wrap items-center gap-3">
        <h2 class="text-display text-xl">{{ challenge.title }}</h2>
        <Badge :variant="challenge.isPublished ? 'default' : 'outline'">
          {{ challenge.isPublished ? $t('已发布') : $t('未发布') }}
        </Badge>
        <Badge variant="secondary">{{ directionLabel(challenge.direction) }}</Badge>
      </div>

      <Tabs v-model="activeSection" default-value="general" class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_14rem] lg:items-start">
        <TabsList class="h-auto w-full justify-start overflow-x-auto p-1.5 lg:sticky lg:top-24 lg:col-start-2 lg:row-start-1 lg:flex-col lg:overflow-visible">
          <TabsTrigger value="general" class="h-10 flex-1 px-4 text-sm lg:w-full lg:flex-none lg:justify-start">{{ $t('基本设置') }}</TabsTrigger>
          <TabsTrigger value="config" class="h-10 flex-1 px-4 text-sm lg:w-full lg:flex-none lg:justify-start">{{ $t('题目配置') }}</TabsTrigger>
          <TabsTrigger value="hints" class="h-10 flex-1 px-4 text-sm lg:w-full lg:flex-none lg:justify-start">{{ $t('提示') }}</TabsTrigger>
          <TabsTrigger value="scoring" class="h-10 flex-1 px-4 text-sm lg:w-full lg:flex-none lg:justify-start">{{ $t('队伍判分') }}</TabsTrigger>
        </TabsList>

        <TabsContent value="general" class="mt-0 lg:col-start-1 lg:row-start-1">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('基本设置') }}</CardTitle>
            </CardHeader>
            <CardContent>
              <form @submit.prevent="saveEdit">
                <FieldGroup>
                  <Field>
                    <FieldLabel for="cc-title">{{ $t('比赛题目名称') }}</FieldLabel>
                    <Input
                      id="cc-title"
                      v-model="editCustomTitle"
                      maxlength="160"
                      :readonly="!canWrite"
                      :placeholder="$t('留空时使用题库模板标题')"
                    />
                    <FieldDescription>{{ $t('只修改本场比赛中的展示名称,不会更改题库模板') }}</FieldDescription>
                  </Field>
                  <Field>
                    <FieldLabel for="cc-order">{{ $t('顺序') }}</FieldLabel>
                    <Input id="cc-order" v-model.number="editOrder" type="number" min="0" :readonly="!canWrite" />
                  </Field>
                  <Field orientation="horizontal">
                    <Switch id="cc-published" v-model="editPublished" :disabled="!canWrite" />
                    <FieldLabel for="cc-published" class="font-normal">{{ $t('发布该题目(对选手可见)') }}</FieldLabel>
                  </Field>
                  <Field v-if="canWrite">
                    <Button type="submit" :disabled="savingEdit">
                      <Spinner v-if="savingEdit" data-icon="inline-start" /> {{ $t('保存设置') }} </Button>
                  </Field>
                </FieldGroup>
              </form>
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="config" class="mt-0 lg:col-start-1 lg:row-start-1">
          <Card>
            <CardHeader>
              <CardTitle>{{ $t('题目规则') }}</CardTitle>
              <CardDescription>{{ $t('该题目在本竞赛中的模式专属规则;未覆盖的字段继承竞赛默认') }}</CardDescription>
            </CardHeader>
            <CardContent>
              <ChallengeRulesEditor
                :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
                :json="config?.json"
                :inherited-json="inheritedConfigJson"
                :readonly="!canWrite"
                :loading="configLoading"
                :saving="savingConfig"
                :hidden-keys="challenge.usesDynamicFlag ? [] : ['flagTemplate']"
                @save="saveConfig"
              />
            </CardContent>
          </Card>
        </TabsContent>

        <TabsContent value="hints" class="mt-0 lg:col-start-1 lg:row-start-1">
          <div class="flex flex-col gap-3">
            <div class="flex items-center justify-between">
              <div class="flex items-center gap-2">
                <Checkbox id="show-deleted-hints" v-model="includeDeletedHints" />
                <label for="show-deleted-hints" class="text-sm text-muted-foreground">{{ $t('显示已删除') }}</label>
              </div>
              <Button v-if="canWrite" size="sm" @click="openHintDialog()">
                <Plus data-icon="inline-start" /> {{ $t('添加提示') }} </Button>
            </div>
            <Alert v-if="hintsLoadError" variant="destructive">
              <AlertDescription>{{ hintsLoadError }}</AlertDescription>
            </Alert>
            <Skeleton v-if="hintsLoading" class="h-32 w-full" />
            <Empty v-else-if="!hintsLoadError && hints.length === 0" class="border border-dashed py-12">
              <EmptyHeader>
                <EmptyTitle>{{ $t('暂无提示') }}</EmptyTitle>
              </EmptyHeader>
            </Empty>
            <Table v-else-if="hints.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('内容') }}</TableHead>
                  <TableHead class="w-20">{{ $t('扣分') }}</TableHead>
                  <TableHead class="w-44">{{ $t('发布时间') }}</TableHead>
                  <TableHead class="w-24">{{ $t('状态') }}</TableHead>
                  <TableHead v-if="canWrite" class="w-44 text-right">{{ $t('操作') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="h in hints" :key="h.id" :class="{ 'opacity-60': h.deletedAt }">
                  <TableCell class="max-w-96 truncate">{{ h.content }}</TableCell>
                  <TableCell class="font-mono tabular-nums">{{ h.cost }}</TableCell>
                  <TableCell class="font-mono text-xs tabular-nums">{{ h.publishedAt ? adminFormatDateTime(h.publishedAt) : $t('未发布') }}</TableCell>
                  <TableCell>
                    <Badge v-if="h.deletedAt" variant="destructive">{{ $t('已删除') }}</Badge>
                    <Badge v-else :variant="h.publishedAt ? 'default' : 'outline'">
                      {{ h.publishedAt ? $t('已发布') : $t('未发布') }}
                    </Badge>
                  </TableCell>
                  <TableCell v-if="canWrite" class="text-right">
                    <div class="flex justify-end gap-1">
                      <template v-if="!h.deletedAt">
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="openHintDialog(h)">{{ $t('编辑') }}</Button>
                        <Button variant="ghost" size="sm" :disabled="pendingHintId === h.id" @click="deleteHint(h)">{{ $t('删除') }}</Button>
                      </template>
                      <Button v-else variant="outline" size="sm" :disabled="pendingHintId === h.id" @click="restoreHint(h)">{{ $t('恢复') }}</Button>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </TabsContent>

        <TabsContent value="scoring" class="mt-0 lg:col-start-1 lg:row-start-1">
          <div class="flex flex-col gap-4">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h3 class="text-lg font-semibold">{{ $t('本题队伍判分') }}</h3>
                <p class="text-sm text-muted-foreground">{{ $t('查看所有报名队伍的完成状态与本题得分。') }}</p>
              </div>
              <Button variant="outline" size="sm" :disabled="scoringLoading" @click="loadChallengeTeamScoring">
                <Spinner v-if="scoringLoading" data-icon="inline-start" />{{ $t('刷新') }}
              </Button>
            </div>

            <Alert v-if="scoringError" variant="destructive">
              <AlertDescription>{{ scoringError }}</AlertDescription>
            </Alert>
            <Skeleton v-if="scoringLoading" class="h-48 w-full" />
            <Empty v-else-if="!scoringError && scoringRows.length === 0" class="border border-dashed py-12">
              <EmptyHeader><EmptyTitle>{{ $t('暂无注册队伍') }}</EmptyTitle></EmptyHeader>
            </Empty>
            <Table v-else-if="scoringRows.length > 0">
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t('队伍') }}</TableHead>
                  <TableHead class="w-28">{{ $t('注册状态') }}</TableHead>
                  <TableHead class="w-32">{{ $t('完成状态') }}</TableHead>
                  <TableHead class="w-32 text-right">{{ $t('本题得分') }}</TableHead>
                  <TableHead class="w-32 text-right">{{ $t('人工修正') }}</TableHead>
                  <TableHead v-if="canJudge" class="w-28 text-right">{{ $t('操作') }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="row in scoringRows" :key="row.team.id">
                  <TableCell>
                    <div class="flex items-center gap-2">
                      <span class="font-medium">{{ teamDisplayName(row.team, scoringDisplayNames) }}</span>
                      <Badge v-if="row.team.isBanned" variant="destructive">{{ $t('已封禁') }}</Badge>
                    </div>
                  </TableCell>
                  <TableCell>
                    <Badge :variant="row.team.registrationStatus === 'Approved' ? 'default' : row.team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                      {{ enumLabel(TeamRegistrationStatusLabel, row.team.registrationStatus) }}
                    </Badge>
                  </TableCell>
                  <TableCell><Badge :variant="row.progressVariant">{{ row.progressLabel }}</Badge></TableCell>
                  <TableCell class="text-right font-mono font-semibold tabular-nums">{{ row.score }} pts</TableCell>
                  <TableCell class="text-right font-mono tabular-nums" :class="row.adjustment < 0 ? 'text-destructive' : row.adjustment > 0 ? 'text-emerald-600' : 'text-muted-foreground'">
                    {{ row.adjustment > 0 ? '+' : '' }}{{ row.adjustment }} pts
                  </TableCell>
                  <TableCell v-if="canJudge" class="text-right">
                    <Button variant="outline" size="sm" @click="openAdjustment(row)">{{ $t('修正判分') }}</Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </TabsContent>
      </Tabs>

      <Dialog :open="adjustmentTarget !== null" @update:open="closeAdjustment">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ $t('修正本题判分') }}</DialogTitle>
            <DialogDescription>
              {{ $t('为队伍「{team}」记录本题正负分修正。', { team: adjustmentTarget ? teamDisplayName(adjustmentTarget.team, scoringDisplayNames) : '-' }) }}
            </DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="adjustmentError" variant="destructive"><AlertDescription>{{ adjustmentError }}</AlertDescription></Alert>
            <dl v-if="adjustmentTarget" class="grid grid-cols-2 gap-3 rounded-md border p-3 text-sm">
              <div>
                <dt class="text-muted-foreground">{{ $t('当前本题得分') }}</dt>
                <dd class="font-mono text-lg font-semibold tabular-nums">{{ adjustmentTarget.score }} pts</dd>
              </div>
              <div>
                <dt class="text-muted-foreground">{{ $t('调整后') }}</dt>
                <dd class="font-mono text-lg font-semibold tabular-nums">{{ adjustmentTarget.score + adjustmentDelta }} pts</dd>
              </div>
            </dl>
            <Field>
              <FieldLabel for="challenge-score-delta">{{ $t('修正分值') }}</FieldLabel>
              <Input id="challenge-score-delta" v-model.number="adjustmentDelta" type="number" step="1" />
              <FieldDescription>{{ $t('请输入非零整数，例如 25 或 -10。') }}</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" :disabled="adjustmentPending" @click="adjustmentTarget = null">{{ $t('取消') }}</Button>
            <Button :disabled="adjustmentPending || !adjustmentValid" @click="submitAdjustment">
              <Spinner v-if="adjustmentPending" data-icon="inline-start" />{{ $t('确认调整') }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog v-model:open="hintDialogOpen">
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ editingHint ? $t('编辑提示') : $t('添加提示') }}</DialogTitle>
            <DialogDescription>{{ $t('发布时间留空表示暂不发布') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Alert v-if="hintError" variant="destructive">
              <AlertDescription>{{ hintError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="hint-content">{{ $t('提示内容') }}</FieldLabel>
              <Textarea id="hint-content" v-model="hintForm.content" required />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="hint-cost">{{ $t('扣分') }}</FieldLabel>
                <Input id="hint-cost" v-model.number="hintForm.cost" type="number" min="0" />
              </Field>
              <Field>
                <FieldLabel for="hint-publish">{{ $t('发布时间(可选)') }}</FieldLabel>
                <Input id="hint-publish" v-model="hintForm.publishedAt" type="datetime-local" />
              </Field>
            </div>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" @click="hintDialogOpen = false">{{ $t('取消') }}</Button>
            <Button :disabled="savingHint" @click="saveHint">
              <Spinner v-if="savingHint" data-icon="inline-start" /> {{ $t('保存') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </template>
  </div>
</template>
