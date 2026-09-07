<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminAcceptTeamBanAppeal,
  adminApproveTeam,
  adminBanTeam,
  adminCreateManualAdjustment,
  adminCorrectTeamBan,
  adminCompetitionTracksGet,
  adminListCompetitionChallenges,
  adminListTeamBanAppeals,
  adminListTeams,
  adminRejectTeam,
  adminUpholdTeamBanAppeal,
  adminTeamTrackAssign,
  userProfileGet,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse,
  NoCtfapiEndpointsAuthenticationPublicUserProfileResponse,
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { competitionTrackErrorMessage } from '~/lib/competition-track'

definePageMeta({ middleware: 'auth' })

const { competitionId, canJudge, canWrite } = useCompetitionAdmin()
const route = useRoute()

const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const pendingId = ref<string | null>(null)
const tracks = ref<NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])
const selectedTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const teamMembers = ref<NoCtfapiEndpointsAuthenticationPublicUserProfileResponse[]>([])
const teamDetailLoading = ref(false)
const teamDisplayNames = computed(() => buildTeamDisplayNames(teams.value))
const displayTeamName = (team: NoCtfapiEndpointsTeamsTeamResponse) =>
  teamDisplayName(team, teamDisplayNames.value)

// ---- Manual score adjustment ----
const scoreAdjustmentTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const scoreAdjustmentChallenges = ref<NoCtfapiEndpointsChallengesChallengeResponse[]>([])
const scoreAdjustmentChallengeId = ref('')
const scoreAdjustmentDelta = ref(0)
const scoreAdjustmentLoading = ref(false)
const scoreAdjustmentPending = ref(false)
const scoreAdjustmentError = ref<string | null>(null)
const scoreAdjustmentValid = computed(() =>
  Boolean(scoreAdjustmentTeam.value?.id)
  && Boolean(scoreAdjustmentChallengeId.value)
  && Number.isInteger(scoreAdjustmentDelta.value)
  && scoreAdjustmentDelta.value !== 0,
)

async function openScoreAdjustment(team: NoCtfapiEndpointsTeamsTeamResponse): Promise<void> {
  if (!team.id || !canJudge.value) return
  scoreAdjustmentTeam.value = team
  scoreAdjustmentChallenges.value = []
  scoreAdjustmentChallengeId.value = ''
  scoreAdjustmentDelta.value = 0
  scoreAdjustmentError.value = null
  scoreAdjustmentLoading.value = true
  try {
    const { data, error: requestError } = await adminListCompetitionChallenges({
      path: { competitionId },
      query: { includeDeleted: false },
    })
    if (requestError) throw requestError
    if (scoreAdjustmentTeam.value?.id !== team.id) return
    scoreAdjustmentChallenges.value = (data?.items ?? [])
      .filter(challenge => Boolean(challenge.id) && !challenge.deletedAt)
      .sort((left, right) => (left.order ?? 0) - (right.order ?? 0))
  }
  catch (requestError) {
    if (scoreAdjustmentTeam.value?.id === team.id)
      scoreAdjustmentError.value = parseApiError(requestError, translate('加载题目列表失败')).message
  }
  finally {
    if (scoreAdjustmentTeam.value?.id === team.id) scoreAdjustmentLoading.value = false
  }
}

function closeScoreAdjustment(open: boolean): void {
  if (!open && !scoreAdjustmentPending.value) scoreAdjustmentTeam.value = null
}

async function submitScoreAdjustment(): Promise<void> {
  const teamId = scoreAdjustmentTeam.value?.id
  if (!teamId || !scoreAdjustmentValid.value || scoreAdjustmentPending.value) return
  scoreAdjustmentPending.value = true
  scoreAdjustmentError.value = null
  try {
    const { error: requestError } = await adminCreateManualAdjustment({
      path: { competitionId },
      body: {
        teamId,
        competitionChallengeId: scoreAdjustmentChallengeId.value,
        delta: scoreAdjustmentDelta.value,
      },
    })
    if (requestError) throw requestError
    toast.success(translate('得分修正已记录'))
    scoreAdjustmentTeam.value = null
  }
  catch (requestError) {
    scoreAdjustmentError.value = parseApiError(requestError, translate('记录得分修正失败')).message
  }
  finally {
    scoreAdjustmentPending.value = false
  }
}

async function openTeamDetail(team: NoCtfapiEndpointsTeamsTeamResponse): Promise<void> {
  selectedTeam.value = team
  teamMembers.value = []
  teamDetailLoading.value = true
  const memberIds = team.memberIds ?? []
  const responses = await Promise.all(memberIds.map(userId => userProfileGet({ path: { userId } })))
  if (selectedTeam.value?.id === team.id) {
    teamMembers.value = responses.flatMap(response => response.data ? [response.data] : [])
    teamDetailLoading.value = false
  }
}

async function load() {
  loading.value = true
  error.value = null
  const [teamResult, trackResult] = await Promise.all([
    adminListTeams({ path: { competitionId } }),
    adminCompetitionTracksGet({ path: { competitionId } }),
  ])
  if (teamResult.error || !teamResult.data) error.value = parseApiError(teamResult.error).message
  else teams.value = teamResult.data.items ?? []
  if (!trackResult.error && trackResult.data) {
    tracks.value = trackResult.data.items ?? []
  }
  loading.value = false
}

async function assignTrack(team: NoCtfapiEndpointsTeamsTeamResponse, trackKey: string) {
  if (!team.id || !canWrite.value || team.trackKey === trackKey) return
  pendingId.value = team.id
  try {
    const { error: requestError } = await adminTeamTrackAssign({
      path: { competitionId, teamId: team.id },
      body: { trackKey },
    })
    if (requestError) throw requestError
    toast.success(translate('队伍赛道已更新'))
    await load()
  }
  catch (requestError) {
    toast.error(competitionTrackErrorMessage(requestError, translate('更新队伍赛道失败')))
  }
  finally {
    pendingId.value = null
  }
}

async function simpleAction(team: NoCtfapiEndpointsTeamsTeamResponse, action: 'approve' | 'reject') {
  if (!team.id) return
  pendingId.value = team.id
  try {
    const path = { competitionId, teamId: team.id }
    const { error } = action === 'approve'
      ? await adminApproveTeam({ path })
      : await adminRejectTeam({ path })
    if (error) throw error
    toast.success(translate("操作成功"))
    await load()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    pendingId.value = null
  }
}

// ---- Ban / correct-ban with reason ----
const banDialog = ref<{ team: NoCtfapiEndpointsTeamsTeamResponse; mode: 'ban' | 'correct' } | null>(null)
const banReason = ref('')
const banAnnouncePublicly = ref(false)
const banPending = ref(false)
const banReasonValid = computed(() => {
  const length = banReason.value.trim().length
  return length <= 512 && (banDialog.value?.mode === 'correct' ? length >= 8 : length > 0)
})

function openBan(team: NoCtfapiEndpointsTeamsTeamResponse, mode: 'ban' | 'correct') {
  banDialog.value = { team, mode }
  banReason.value = ''
  banAnnouncePublicly.value = false
}

async function submitBan() {
  const ctx = banDialog.value
  if (!ctx?.team.id || !banReasonValid.value) return
  banPending.value = true
  try {
    const path = { competitionId, teamId: ctx.team.id }
    const { error } = ctx.mode === 'ban'
      ? await adminBanTeam({
          path,
          body: {
            reason: banReason.value.trim(),
            announcePublicly: banAnnouncePublicly.value,
          },
        })
      : await adminCorrectTeamBan({ path, body: { reason: banReason.value.trim() } })
    if (error) throw error
    toast.success(ctx.mode === 'ban' ? translate("队伍已封禁") : translate("封禁已纠正"))
    banDialog.value = null
    await load()
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    banPending.value = false
  }
}

// ---- Appeals ----
const appeals = ref<NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse[]>([])
const appealsLoading = ref(true)
const appealsError = ref<string | null>(null)
const appealDialog = ref<{ banCase: NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse; mode: 'accept' | 'uphold' } | null>(null)
const appealReason = ref('')
const appealPending = ref(false)

async function loadAppeals() {
  appealsLoading.value = true
  const { data, error } = await adminListTeamBanAppeals({ path: { competitionId } })
  if (error || !data) {
    appealsError.value = parseApiError(error).message
  }
  else {
    appealsError.value = null
    appeals.value = data.items ?? []
    await nextTick()
    const appealId = typeof route.query.appeal === 'string' ? route.query.appeal : null
    if (appealId) document.getElementById(`appeal-${appealId}`)?.scrollIntoView({ block: 'center' })
  }
  appealsLoading.value = false
}

function openAppeal(banCase: NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse, mode: 'accept' | 'uphold') {
  appealDialog.value = { banCase, mode }
  appealReason.value = ''
}

async function submitAppeal() {
  const ctx = appealDialog.value
  const appealId = ctx?.banCase.appeal?.id
  if (!ctx || !appealId) return
  if (!appealReason.value.trim()) return
  appealPending.value = true
  try {
    const path = { competitionId, appealId }
    const { error } = ctx.mode === 'accept'
      ? await adminAcceptTeamBanAppeal({ path, body: { reason: appealReason.value.trim() } })
      : await adminUpholdTeamBanAppeal({ path, body: { reason: appealReason.value.trim() } })
    if (error) throw error
    toast.success(ctx.mode === 'accept' ? translate("申诉已接受,队伍解封") : translate("申诉已驳回,维持封禁"))
    appealDialog.value = null
    await Promise.all([load(), loadAppeals()])
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    appealPending.value = false
  }
}

onMounted(() => {
  void load()
  void loadAppeals()
})
</script>

<template>
  <div class="flex flex-col gap-8">
    <div id="ban-appeals" class="flex scroll-mt-24 flex-col gap-4">
      <h2 class="text-lg font-semibold">{{ $t('团队管理') }}</h2>
      <Alert v-if="error" variant="destructive">
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>
      <Skeleton v-if="loading" class="h-48 w-full" />
      <Empty v-else-if="teams.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('暂无注册队伍') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <Table v-else>
        <TableHeader>
          <TableRow>
            <TableHead>{{ $t('队名') }}</TableHead>
            <TableHead class="min-w-36">{{ $t('赛道') }}</TableHead>
            <TableHead class="w-24">{{ $t('人数') }}</TableHead>
            <TableHead class="w-28">{{ $t('注册状态') }}</TableHead>
            <TableHead class="w-28">{{ $t('封禁状态') }}</TableHead>
            <TableHead class="w-44">{{ $t('注册时间') }}</TableHead>
            <TableHead v-if="canWrite || canJudge" class="w-64 text-right">{{ $t('操作') }}</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="t in teams" :key="t.id">
            <TableCell>
              <button
                type="button"
                class="rounded-sm font-medium underline-offset-4 hover:text-primary hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                @click="openTeamDetail(t)"
              >
                {{ displayTeamName(t) }}
              </button>
            </TableCell>
            <TableCell>
              <Select
                v-if="canWrite"
                :model-value="t.trackKey"
                :disabled="pendingId === t.id"
                @update:model-value="value => assignTrack(t, String(value))"
              >
                <SelectTrigger class="min-w-32"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="track in tracks" :key="track.key" :value="track.key!">
                    {{ track.name }}<template v-if="track.isInternal"> · {{ $t('内部') }}</template>
                  </SelectItem>
                </SelectContent>
              </Select>
              <Badge v-else variant="outline">{{ t.trackName ?? t.trackKey }}</Badge>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ t.memberIds?.length ?? 0 }}</TableCell>
            <TableCell>
              <Badge :variant="t.registrationStatus === 'Approved' ? 'default' : t.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                {{ enumLabel(TeamRegistrationStatusLabel, t.registrationStatus) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge v-if="t.isBanned" variant="destructive">{{ $t('已封禁') }}</Badge>
              <span v-else class="text-muted-foreground">-</span>
            </TableCell>
            <TableCell class="font-mono tabular-nums">{{ adminFormatDateTime(t.registeredAt) }}</TableCell>
            <TableCell v-if="canWrite || canJudge" class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <template v-if="canWrite && t.registrationStatus === 'Pending'">
                  <Button size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'approve')">{{ $t('通过') }}</Button>
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'reject')">{{ $t('拒绝') }}</Button>
                </template>
                <Button v-if="canJudge" variant="outline" size="sm" :disabled="pendingId === t.id" @click="openScoreAdjustment(t)">
                  {{ $t('调整分数') }}
                </Button>
                <template v-if="canJudge && !t.isBanned">
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="openBan(t, 'ban')">{{ $t('封禁') }}</Button>
                </template>
                <template v-else-if="canWrite">
                  <Button type="button" variant="ghost" size="sm" :disabled="pendingId === t.id" @click.stop="openBan(t, 'correct')">{{ $t('纠正封禁') }}</Button>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <Sheet :open="selectedTeam !== null" @update:open="open => { if (!open) selectedTeam = null }">
      <SheetContent class="overflow-y-auto sm:max-w-lg">
        <SheetHeader>
          <SheetTitle>{{ $t('队伍详情') }}</SheetTitle>
          <SheetDescription>{{ selectedTeam ? displayTeamName(selectedTeam) : '' }}</SheetDescription>
        </SheetHeader>
        <div v-if="selectedTeam" class="mt-6 flex flex-col gap-6">
          <dl class="grid grid-cols-[7rem_minmax(0,1fr)] gap-x-4 gap-y-3 text-sm">
            <dt class="text-muted-foreground">{{ $t('赛道') }}</dt>
            <dd>{{ selectedTeam.trackName ?? selectedTeam.trackKey }}</dd>
            <dt class="text-muted-foreground">{{ $t('注册状态') }}</dt>
            <dd>{{ enumLabel(TeamRegistrationStatusLabel, selectedTeam.registrationStatus) }}</dd>
            <dt class="text-muted-foreground">{{ $t('封禁状态') }}</dt>
            <dd>{{ selectedTeam.isBanned ? $t('已封禁') : $t('正常') }}</dd>
            <dt class="text-muted-foreground">{{ $t('注册时间') }}</dt>
            <dd class="font-mono tabular-nums">{{ adminFormatDateTime(selectedTeam.registeredAt) }}</dd>
          </dl>
          <Separator />
          <section class="flex flex-col gap-3">
            <h3 class="font-semibold">{{ $t('成员（{count}）', { count: selectedTeam.memberIds?.length ?? 0 }) }}</h3>
            <div v-if="teamDetailLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
              <Spinner class="size-4" />{{ $t('加载中') }}
            </div>
            <div v-else class="flex flex-col divide-y rounded-md border">
              <div v-for="member in teamMembers" :key="member.userId" class="flex items-center gap-3 p-3">
                <Avatar class="size-9">
                  <AvatarImage v-if="member.avatarUrl" :src="member.avatarUrl" :alt="member.userName ?? ''" />
                  <AvatarFallback>{{ member.userName?.slice(0, 2) }}</AvatarFallback>
                </Avatar>
                <div class="min-w-0 flex-1">
                  <NuxtLink :to="`/users/${member.userId}`" class="font-medium hover:underline">
                    {{ member.userName }}
                  </NuxtLink>
                </div>
                <Badge v-if="member.userId === selectedTeam.captainId" variant="secondary">{{ $t('队长') }}</Badge>
              </div>
              <p v-if="!teamMembers.length" class="p-3 text-sm text-muted-foreground">{{ $t('暂无成员') }}</p>
            </div>
          </section>
        </div>
      </SheetContent>
    </Sheet>

    <Dialog :open="scoreAdjustmentTeam !== null" @update:open="closeScoreAdjustment">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('得分修正') }}</DialogTitle>
          <DialogDescription>
            {{ $t('为队伍「{team}」记录题目得分修正。正数加分，负数扣分。', { team: scoreAdjustmentTeam ? displayTeamName(scoreAdjustmentTeam) : '-' }) }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Alert v-if="scoreAdjustmentError" variant="destructive">
            <AlertDescription>{{ scoreAdjustmentError }}</AlertDescription>
          </Alert>
          <Field>
            <FieldLabel for="score-adjustment-challenge">{{ $t('题目') }}</FieldLabel>
            <Skeleton v-if="scoreAdjustmentLoading" class="h-10 w-full" />
            <Select v-else v-model="scoreAdjustmentChallengeId">
              <SelectTrigger id="score-adjustment-challenge" class="w-full">
                <SelectValue :placeholder="$t('选择题目')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="challenge in scoreAdjustmentChallenges" :key="challenge.id" :value="challenge.id!">
                  {{ challenge.title }} · {{ directionLabel(challenge.direction) }}
                </SelectItem>
              </SelectContent>
            </Select>
            <FieldDescription v-if="!scoreAdjustmentLoading && scoreAdjustmentChallenges.length === 0">
              {{ $t('当前竞赛没有可调整的题目') }}
            </FieldDescription>
          </Field>
          <Field>
            <FieldLabel for="score-adjustment-delta">{{ $t('修正分值') }}</FieldLabel>
            <Input id="score-adjustment-delta" v-model.number="scoreAdjustmentDelta" type="number" step="1" />
            <FieldDescription>{{ $t('请输入非零整数，例如 25 或 -10。') }}</FieldDescription>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="scoreAdjustmentPending" @click="scoreAdjustmentTeam = null">{{ $t('取消') }}</Button>
          <Button :disabled="scoreAdjustmentLoading || scoreAdjustmentPending || !scoreAdjustmentValid" @click="submitScoreAdjustment">
            <Spinner v-if="scoreAdjustmentPending" data-icon="inline-start" />
            {{ $t('确认调整') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Separator />

    <div class="flex flex-col gap-4">
      <h2 class="text-lg font-semibold">{{ $t('封禁申诉') }}</h2>
      <Alert v-if="appealsError" variant="destructive">
        <AlertDescription>{{ appealsError }}</AlertDescription>
      </Alert>
      <Skeleton v-if="appealsLoading" class="h-32 w-full" />
      <Empty v-else-if="!appealsError && appeals.length === 0" class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('暂无申诉') }}</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <div v-else-if="appeals.length > 0" class="flex flex-col gap-3">
        <Card v-for="a in appeals" :id="`appeal-${a.appeal?.id ?? a.banEventId}`" :key="a.banEventId" class="scroll-mt-24">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <CardTitle class="text-base">{{ teamDisplayName(a, teamDisplayNames) }}</CardTitle>
              <div class="flex items-center gap-1">
                <Badge variant="outline">{{ enumLabel(TeamBanSourceLabel, a.source) }}</Badge>
                <Badge v-if="a.appeal" :variant="a.appeal.status === 'Submitted' ? 'secondary' : a.appeal.status === 'Accepted' ? 'default' : 'destructive'">
                  {{ $t('申诉{status}', { status: enumLabel(TeamBanAppealStatusLabel, a.appeal.status) }) }}
                </Badge>
                <Badge v-if="a.isCurrentlyBanned" variant="destructive">{{ $t('封禁中') }}</Badge>
              </div>
            </div>
            <CardDescription>{{ $t('封禁于 {time}', { time: adminFormatDateTime(a.bannedAt) }) }}</CardDescription>
          </CardHeader>
          <CardContent v-if="a.appeal" class="flex flex-col gap-2 text-sm">
            <p><span class="text-muted-foreground">{{ $t('申诉人:') }}</span>{{ a.appeal.submittedByUserName }} · {{ adminFormatDateTime(a.appeal.submittedAt) }}</p>
            <p class="whitespace-pre-wrap">{{ a.appeal.statement }}</p>
            <p v-if="a.appeal.resolutionReason" class="text-muted-foreground">
              {{ $t('裁决：{user} · {time} · {reason}', { user: a.appeal.resolvedByUserName ?? '-', time: adminFormatDateTime(a.appeal.resolvedAt), reason: a.appeal.resolutionReason }) }}
            </p>
          </CardContent>
          <CardFooter v-if="canJudge && a.appeal?.status === 'Submitted' && a.canResolve" class="gap-2">
            <Button size="sm" :disabled="appealPending" @click="openAppeal(a, 'accept')">{{ $t('接受申诉(解封)') }}</Button>
            <Button variant="outline" size="sm" :disabled="appealPending" @click="openAppeal(a, 'uphold')">{{ $t('维持封禁') }}</Button>
          </CardFooter>
        </Card>
      </div>
    </div>

    <Dialog :open="banDialog !== null" @update:open="(v) => { if (!v) banDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ banDialog?.mode === 'ban' ? $t('封禁队伍') : $t('纠正封禁') }}</DialogTitle>
          <DialogDescription>
            {{ banDialog?.mode === 'ban'
              ? $t('封禁「{team}」，该队将无法继续参赛。', { team: banDialog?.team.name ?? '-' })
              : $t('将「{team}」的封禁标记为误封并纠正。', { team: banDialog?.team.name ?? '-' }) }}
            {{ $t('必须填写原因（将记入审计）。') }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="ban-reason">{{ $t('原因') }}</FieldLabel>
            <Textarea id="ban-reason" v-model="banReason" maxlength="512" required />
            <FieldDescription v-if="banDialog?.mode === 'correct'">
              {{ $t('至少 8 个字符；当前 {length}/512。', { length: banReason.trim().length }) }}
            </FieldDescription>
            <FieldDescription v-else>
              {{ $t('当前 {length}/512。', { length: banReason.trim().length }) }}
            </FieldDescription>
          </Field>
          <Field v-if="banDialog?.mode === 'ban'" orientation="horizontal">
            <Checkbox id="ban-announce-publicly" v-model="banAnnouncePublicly" />
            <div class="grid gap-1.5 leading-none">
              <FieldLabel for="ban-announce-publicly">{{ $t('封禁后发布赛事纪律公告') }}</FieldLabel>
              <FieldDescription> {{ $t('默认关闭。开启后将向参赛者公告该队伍因违反赛事规则被封禁，不公开处置原因或证据。') }} </FieldDescription>
            </div>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="banDialog = null">{{ $t('取消') }}</Button>
          <Button type="button" :disabled="banPending || !banReasonValid" @click="submitBan">
            <Spinner v-if="banPending" data-icon="inline-start" /> {{ $t('确认') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="appealDialog !== null" @update:open="(v) => { if (!v) appealDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ appealDialog?.mode === 'accept' ? $t('接受申诉') : $t('维持封禁') }}</DialogTitle>
          <DialogDescription>
            {{ appealDialog?.mode === 'accept'
              ? $t('接受「{team}」的申诉并解除封禁。', { team: appealDialog?.banCase.teamName ?? '-' })
              : $t('驳回「{team}」的申诉，维持封禁。', { team: appealDialog?.banCase.teamName ?? '-' }) }}
            {{ $t('必须填写裁决理由。') }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="appeal-reason">{{ $t('裁决理由') }}</FieldLabel>
            <Textarea id="appeal-reason" v-model="appealReason" required />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="appealDialog = null">{{ $t('取消') }}</Button>
          <Button :disabled="appealPending || !appealReason.trim()" @click="submitAppeal">
            <Spinner v-if="appealPending" data-icon="inline-start" /> {{ $t('确认') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
