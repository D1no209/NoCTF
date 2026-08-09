<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminAcceptTeamBanAppeal,
  adminApproveTeam,
  adminBanTeam,
  adminCorrectTeamBan,
  adminListTeamBanAppeals,
  adminListTeams,
  adminRejectTeam,
  adminUnbanTeam,
  adminUpholdTeamBanAppeal,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)
const pendingId = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  const { data, error: e } = await adminListTeams({ path: { competitionId } })
  if (e) error.value = parseApiError(e).message
  else teams.value = data?.items ?? []
  loading.value = false
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
    toast.success('操作成功')
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
    toast.error('队伍标识缺失，请刷新后重试')
    return
  }
  pendingId.value = team.id
  try {
    const { error } = await adminUnbanTeam({
      path: { competitionId, teamId: team.id },
    })
    if (error) throw error
    toast.success(`已解除「${team.name ?? '该队伍'}」的封禁`)
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
const banPending = ref(false)
const banReasonValid = computed(() => {
  const length = banReason.value.trim().length
  return length <= 512 && (banDialog.value?.mode === 'correct' ? length >= 8 : length > 0)
})

function openBan(team: NoCtfapiEndpointsTeamsTeamResponse, mode: 'ban' | 'correct') {
  banDialog.value = { team, mode }
  banReason.value = ''
}

async function submitBan() {
  const ctx = banDialog.value
  if (!ctx?.team.id || !banReasonValid.value) return
  banPending.value = true
  try {
    const path = { competitionId, teamId: ctx.team.id }
    const { error } = ctx.mode === 'ban'
      ? await adminBanTeam({ path, body: { reason: banReason.value.trim() } })
      : await adminCorrectTeamBan({ path, body: { reason: banReason.value.trim() } })
    if (error) throw error
    toast.success(ctx.mode === 'ban' ? '队伍已封禁' : '封禁已纠正')
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
const appealDialog = ref<{ banCase: NoCtfapiEndpointsAdministrationTeamsAdminTeamBanCaseResponse; mode: 'accept' | 'uphold' } | null>(null)
const appealReason = ref('')
const appealPending = ref(false)

async function loadAppeals() {
  appealsLoading.value = true
  const { data, error } = await adminListTeamBanAppeals({ path: { competitionId } })
  if (!error) appeals.value = data?.items ?? []
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
    toast.success(ctx.mode === 'accept' ? '申诉已接受,队伍解封' : '申诉已驳回,维持封禁')
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
      <h2 class="text-lg font-semibold">团队管理</h2>
      <Alert v-if="error" variant="destructive">
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>
      <Skeleton v-if="loading" class="h-48 w-full" />
      <Empty v-else-if="teams.length === 0">
        <EmptyHeader>
          <EmptyTitle>暂无注册队伍</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <Table v-else>
        <TableHeader>
          <TableRow>
            <TableHead>队名</TableHead>
            <TableHead class="w-24">人数</TableHead>
            <TableHead class="w-28">注册状态</TableHead>
            <TableHead class="w-28">封禁状态</TableHead>
            <TableHead class="w-44">注册时间</TableHead>
            <TableHead v-if="canWrite" class="w-64 text-right">操作</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="t in teams" :key="t.id">
            <TableCell class="font-medium">{{ t.name }}</TableCell>
            <TableCell>{{ t.memberIds?.length ?? 0 }}</TableCell>
            <TableCell>
              <Badge :variant="t.registrationStatus === 'Approved' ? 'default' : t.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                {{ enumLabel(TeamRegistrationStatusLabel, t.registrationStatus) }}
              </Badge>
            </TableCell>
            <TableCell>
              <Badge v-if="t.isBanned" variant="destructive">已封禁</Badge>
              <span v-else class="text-muted-foreground">—</span>
            </TableCell>
            <TableCell>{{ adminFormatDateTime(t.registeredAt) }}</TableCell>
            <TableCell v-if="canWrite" class="text-right">
              <div class="flex flex-wrap justify-end gap-1">
                <template v-if="t.registrationStatus === 'Pending'">
                  <Button size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'approve')">通过</Button>
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="simpleAction(t, 'reject')">拒绝</Button>
                </template>
                <template v-if="!t.isBanned">
                  <Button variant="outline" size="sm" :disabled="pendingId === t.id" @click="openBan(t, 'ban')">封禁</Button>
                </template>
                <template v-else>
                  <Button type="button" variant="outline" size="sm" :disabled="pendingId === t.id" @click.stop="openUnban(t)">解封</Button>
                  <Button type="button" variant="ghost" size="sm" :disabled="pendingId === t.id" @click.stop="openBan(t, 'correct')">纠正封禁</Button>
                </template>
              </div>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <Separator />

    <div class="flex flex-col gap-4">
      <h2 class="text-lg font-semibold">封禁申诉</h2>
      <Skeleton v-if="appealsLoading" class="h-32 w-full" />
      <Empty v-else-if="appeals.length === 0">
        <EmptyHeader>
          <EmptyTitle>暂无申诉</EmptyTitle>
        </EmptyHeader>
      </Empty>
      <div v-else class="flex flex-col gap-3">
        <Card v-for="a in appeals" :key="a.banEventId">
          <CardHeader>
            <div class="flex items-center justify-between gap-2">
              <CardTitle class="text-base">{{ a.teamName }}</CardTitle>
              <div class="flex items-center gap-1">
                <Badge variant="outline">{{ enumLabel(TeamBanSourceLabel, a.source) }}</Badge>
                <Badge v-if="a.appeal" :variant="a.appeal.status === 'Submitted' ? 'secondary' : a.appeal.status === 'Accepted' ? 'default' : 'destructive'">
                  申诉{{ enumLabel(TeamBanAppealStatusLabel, a.appeal.status) }}
                </Badge>
                <Badge v-if="a.isCurrentlyBanned" variant="destructive">封禁中</Badge>
              </div>
            </div>
            <CardDescription>封禁于 {{ adminFormatDateTime(a.bannedAt) }}</CardDescription>
          </CardHeader>
          <CardContent v-if="a.appeal" class="flex flex-col gap-2 text-sm">
            <p><span class="text-muted-foreground">申诉人:</span>{{ a.appeal.submittedByUserName }} · {{ adminFormatDateTime(a.appeal.submittedAt) }}</p>
            <p class="whitespace-pre-wrap">{{ a.appeal.statement }}</p>
            <p v-if="a.appeal.resolutionReason" class="text-muted-foreground">
              裁决:{{ a.appeal.resolvedByUserName }} · {{ adminFormatDateTime(a.appeal.resolvedAt) }} — {{ a.appeal.resolutionReason }}
            </p>
          </CardContent>
          <CardFooter v-if="canWrite && a.appeal?.status === 'Submitted' && a.canResolve" class="gap-2">
            <Button size="sm" :disabled="appealPending" @click="openAppeal(a, 'accept')">接受申诉(解封)</Button>
            <Button variant="outline" size="sm" :disabled="appealPending" @click="openAppeal(a, 'uphold')">维持封禁</Button>
          </CardFooter>
        </Card>
      </div>
    </div>

    <Dialog :open="unbanDialog !== null" @update:open="(v) => { if (!v && pendingId === null) unbanDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>解除队伍封禁？</DialogTitle>
          <DialogDescription>
            将立即恢复「{{ unbanDialog?.name }}」的参赛资格、历史计分资格和正常运行时生命周期。
          </DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button type="button" variant="outline" :disabled="pendingId !== null" @click="unbanDialog = null">取消</Button>
          <Button
            type="button"
            :disabled="pendingId !== null"
            @click="submitUnban"
          >
            <Spinner v-if="pendingId === unbanDialog?.id" data-icon="inline-start" />
            确认解封
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="banDialog !== null" @update:open="(v) => { if (!v) banDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ banDialog?.mode === 'ban' ? '封禁队伍' : '纠正封禁' }}</DialogTitle>
          <DialogDescription>
            {{ banDialog?.mode === 'ban'
              ? `封禁「${banDialog?.team.name}」,该队将无法继续参赛。`
              : `将「${banDialog?.team.name}」的封禁标记为误封并纠正。` }}
            必须填写原因(将记入审计)。
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="ban-reason">原因</FieldLabel>
            <Textarea id="ban-reason" v-model="banReason" maxlength="512" required />
            <FieldDescription v-if="banDialog?.mode === 'correct'">
              至少 8 个字符；当前 {{ banReason.trim().length }}/512。
            </FieldDescription>
            <FieldDescription v-else>
              当前 {{ banReason.trim().length }}/512。
            </FieldDescription>
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="banDialog = null">取消</Button>
          <Button type="button" :disabled="banPending || !banReasonValid" @click="submitBan">
            <Spinner v-if="banPending" data-icon="inline-start" />
            确认
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="appealDialog !== null" @update:open="(v) => { if (!v) appealDialog = null }">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ appealDialog?.mode === 'accept' ? '接受申诉' : '维持封禁' }}</DialogTitle>
          <DialogDescription>
            {{ appealDialog?.mode === 'accept'
              ? `接受「${appealDialog?.banCase.teamName}」的申诉并解除封禁。`
              : `驳回「${appealDialog?.banCase.teamName}」的申诉,维持封禁。` }}
            必须填写裁决理由。
          </DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="appeal-reason">裁决理由</FieldLabel>
            <Textarea id="appeal-reason" v-model="appealReason" required />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="appealDialog = null">取消</Button>
          <Button :disabled="appealPending || !appealReason.trim()" @click="submitAppeal">
            <Spinner v-if="appealPending" data-icon="inline-start" />
            确认
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
