<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminAcceptTeamBanAppeal,
  adminApproveTeam,
  adminBanTeam,
  adminCorrectTeamBan,
  adminCompetitionTracksGet,
  adminListTeamBanAppeals,
  adminListTeams,
  adminRejectTeam,
  adminUnbanTeam,
  adminUpholdTeamBanAppeal,
  adminTeamTrackAssign,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse,
  NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { competitionTrackErrorMessage } from '~/lib/competition-track'

definePageMeta({ middleware: 'auth' })

const { competitionId, canJudge, canWrite } = useCompetitionAdmin()

const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const pendingId = ref<string | null>(null)
const tracks = ref<NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])
const tracksFrozen = ref(false)

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
    tracksFrozen.value = trackResult.data.isFrozen ?? false
  }
  loading.value = false
}

async function assignTrack(team: NoCtfapiEndpointsTeamsTeamResponse, trackKey: string) {
  if (!team.id || !canWrite.value || tracksFrozen.value || team.trackKey === trackKey) return
  pendingId.value = team.id
  try {
    const { error: requestError } = await adminTeamTrackAssign({
      path: { competitionId, teamId: team.id },
      body: { trackKey, expectedTeamVersion: team.concurrencyVersion ?? 0 },
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

// ---- Unban with explicit confirmation ----
const unbanDialog = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)

function openUnban(team: NoCtfapiEndpointsTeamsTeamResponse) {
  unbanDialog.value = team
}

async function submitUnban() {
  const team = unbanDialog.value
  if (!team?.id) {
    toast.error(translate("队伍标识缺失，请刷新后重试"))
    return
  }
  pendingId.value = team.id
  try {
    const { error } = await adminUnbanTeam({
      path: { competitionId, teamId: team.id },
    })
    if (error) throw error
    toast.success(translate('已解除「{team}」的封禁', { team: team.name ?? translate('该队伍') }))
    unbanDialog.value = null
    await Promise.all([load(), loadAppeals()])
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
    <div class="flex flex-col gap-4">
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
            <TableCell class="font-medium">{{ t.name }}</TableCell>
            <TableCell>
              <Select
                v-if="canWrite && !tracksFrozen"
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
                <template v-if="canJudge && !t.isBanned">
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="openBan(t, 'ban')">{{ $t('封禁') }}</Button>
                </template>
                <template v-else-if="canWrite">
                  <Button type="button" variant="outline" size="sm" :disabled="pendingId === t.id" @click.stop="openUnban(t)">{{ $t('解封') }}</Button>
                  <Button type="button" variant="ghost" size="sm" :disabled="pendingId === t.id" @click.stop="openBan(t, 'correct')">{{ $t('纠正封禁') }}</Button>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

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
        <Card v-for="a in appeals" :key="a.banEventId">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <CardTitle class="text-base">{{ a.teamName }}</CardTitle>
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

    <Dialog :open="unbanDialog !== null" @update:open="(v) => { if (!v && pendingId === null) unbanDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('解除队伍封禁？') }}</DialogTitle>
          <DialogDescription>
            {{ $t('将立即恢复「{team}」的参赛资格、历史计分资格和正常运行时生命周期。', { team: unbanDialog?.name ?? '-' }) }}
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button type="button" variant="outline" :disabled="pendingId !== null" @click="unbanDialog = null">{{ $t('取消') }}</Button>
          <Button
            type="button"
            :disabled="pendingId !== null"
            @click="submitUnban"
          >
            <Spinner v-if="pendingId === unbanDialog?.id" data-icon="inline-start" /> {{ $t('确认解封') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

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
