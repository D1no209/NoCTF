<script setup lang="ts">
import { toast } from 'vue-sonner'
import { Copy, RefreshCw } from '@lucide/vue'
import {
  deleteTeamEndpoint,
  getMyTeamBanCase,
  getMyTeamEndpoint,
  leaveTeamEndpoint,
  resubmitTeamRegistrationEndpoint,
  rotateTeamInvitationEndpoint,
  submitTeamBanAppeal,
  transferTeamCaptainEndpoint,
  updateTeamEndpoint,
} from '~/api'
import type {
  NoCtfapiEndpointsTeamsMyTeamBanCaseResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import {
  maximumAppealStatementLength,
  minimumAppealStatementLength,
  validateAppealStatement,
} from '~/lib/participant-form-validation'

definePageMeta({ middleware: 'auth' })

const route = useRoute()
const competitionId = route.params.id as string
const { user } = useAuth()

const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)

async function load() {
  const { data, error } = await getMyTeamEndpoint({ path: { competitionId } })
  loading.value = false
  if (error || !data) {
    team.value = null
    return
  }
  team.value = data
}

onMounted(load)

const isCaptain = computed(
  () => !!team.value && !!user.value && team.value.captainId === user.value.userId,
)

// 邀请 token(仅轮换后可展示新值)
const invitationToken = ref<string | null>(null)
const rotating = ref(false)

async function rotate() {
  if (!team.value) return
  rotating.value = true
  const { data, error } = await rotateTeamInvitationEndpoint({
    path: { competitionId, teamId: team.value.id! },
  })
  rotating.value = false
  if (error || !data?.invitationToken) {
    toast.error(parseApiError(error, '轮换邀请码失败').message)
    return
  }
  invitationToken.value = data.invitationToken
  toast.success('邀请码已轮换,旧邀请码立即失效')
}

async function copyToken() {
  if (!invitationToken.value) return
  try {
    await navigator.clipboard.writeText(invitationToken.value)
    toast.success('邀请码已复制')
  }
  catch {
    toast.error('复制失败,请手动选择复制')
  }
}

// 改名
const renameOpen = ref(false)
const renameValue = ref('')
const renamePending = ref(false)

function openRename() {
  renameValue.value = team.value?.name ?? ''
  renameOpen.value = true
}

async function submitRename() {
  if (!team.value || !renameValue.value.trim()) return
  renamePending.value = true
  const { data, error } = await updateTeamEndpoint({
    path: { competitionId, teamId: team.value.id! },
    body: { name: renameValue.value.trim() },
  })
  renamePending.value = false
  if (error || !data) {
    toast.error(parseApiError(error, '修改队名失败').message)
    return
  }
  team.value = data
  renameOpen.value = false
  toast.success('队名已更新')
}

// 转让队长
const transferOpen = ref(false)
const transferTarget = ref('')
const transferPending = ref(false)

const transferableMembers = computed(() =>
  (team.value?.memberIds ?? []).filter((id) => id !== team.value?.captainId),
)

async function submitTransfer() {
  if (!team.value || !transferTarget.value) return
  transferPending.value = true
  const { error } = await transferTeamCaptainEndpoint({
    path: { competitionId, teamId: team.value.id! },
    body: { newCaptainId: transferTarget.value },
  })
  transferPending.value = false
  if (error) {
    toast.error(parseApiError(error, '转让队长失败').message)
    return
  }
  transferOpen.value = false
  toast.success('队长已转让')
  await load()
}

// 解散 / 退队 / 重交报名
const acting = ref(false)

async function disband() {
  if (!team.value) return
  acting.value = true
  const { error } = await deleteTeamEndpoint({
    path: { competitionId, teamId: team.value.id! },
  })
  acting.value = false
  if (error) {
    toast.error(parseApiError(error, '解散队伍失败').message)
    return
  }
  toast.success('队伍已解散')
  team.value = null
}

async function leave() {
  acting.value = true
  const { error } = await leaveTeamEndpoint({ path: { competitionId } })
  acting.value = false
  if (error) {
    toast.error(parseApiError(error, '退出队伍失败').message)
    return
  }
  toast.success('已退出队伍')
  team.value = null
}

async function resubmit() {
  if (!team.value) return
  acting.value = true
  const { error } = await resubmitTeamRegistrationEndpoint({
    path: { competitionId, teamId: team.value.id! },
  })
  acting.value = false
  if (error) {
    toast.error(parseApiError(error, '重新提交报名失败').message)
    return
  }
  toast.success('报名已重新提交,等待审核')
  await load()
}

// 封禁与申诉
const banCase = ref<NoCtfapiEndpointsTeamsMyTeamBanCaseResponse | null>(null)
const appealOpen = ref(false)
const appealStatement = ref('')
const appealPending = ref(false)
const appealError = ref<string | null>(null)

async function loadBanCase() {
  const { data, error } = await getMyTeamBanCase({ path: { competitionId } })
  if (error || !data) return
  banCase.value = data
}

watch(
  () => team.value?.isBanned,
  (banned) => {
    if (banned) void loadBanCase()
  },
)

const appealStatusLabel = (status?: string) =>
  ({ Submitted: '申诉中', Upheld: '已驳回', Accepted: '已通过' } as Record<string, string>)[String(status)] ?? '未知'

async function submitAppeal() {
  if (appealPending.value) return
  appealError.value = validateAppealStatement(appealStatement.value)
  if (appealError.value) return

  appealPending.value = true
  try {
    const { error } = await submitTeamBanAppeal({
      path: { competitionId },
      body: { statement: appealStatement.value.trim() },
    })
    if (error) {
      appealError.value = parseApiError(error, '提交申诉失败').message
      toast.error(appealError.value)
      return
    }
    toast.success('申诉已提交')
    appealOpen.value = false
    appealStatement.value = ''
    appealError.value = null
    await loadBanCase()
  }
  catch (error) {
    appealError.value = parseApiError(error, '提交申诉失败').message
    toast.error(appealError.value)
  }
  finally {
    appealPending.value = false
  }
}

function setAppealOpen(open: boolean) {
  if (appealPending.value) return
  appealOpen.value = open
  if (!open) appealError.value = null
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <Skeleton v-else-if="loading" class="h-64 w-full" />

    <Empty v-else-if="!team" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>你还没有加入本竞赛的队伍</EmptyTitle>
        <EmptyDescription>回到概览页创建队伍,或凭邀请码加入队友的队伍</EmptyDescription>
      </EmptyHeader>
      <EmptyContent>
        <Button as-child>
          <NuxtLink :to="`/competitions/${competitionId}`">前往概览</NuxtLink>
        </Button>
      </EmptyContent>
    </Empty>

    <template v-else>
      <Card>
        <CardHeader>
          <div class="flex flex-wrap items-center gap-3">
            <Avatar class="size-12">
              <AvatarImage v-if="team.avatarUrl" :src="team.avatarUrl" :alt="team.name ?? ''" />
              <AvatarFallback>{{ team.name?.slice(0, 2) ?? '?' }}</AvatarFallback>
            </Avatar>
            <CardTitle class="text-xl">{{ team.name }}</CardTitle>
            <Badge
              :variant="team.registrationStatus === TeamRegistrationStatus.Approved ? 'default' : team.registrationStatus === TeamRegistrationStatus.Rejected ? 'destructive' : 'secondary'"
            >
              {{ teamRegistrationStatusLabel(team.registrationStatus) }}
            </Badge>
            <Badge v-if="team.isLocked" variant="outline">已锁定</Badge>
            <Badge v-if="team.isBanned" variant="destructive">已封禁</Badge>
          </div>
          <CardDescription>报名时间:{{ formatDateTime(team.registeredAt) }}</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Alert v-if="team.registrationStatus === TeamRegistrationStatus.Rejected">
            <AlertDescription class="flex flex-wrap items-center gap-2">
              报名被拒绝。{{ isCaptain ? '修改信息后可重新提交:' : '请联系队长重新提交报名。' }}
              <Button v-if="isCaptain" size="sm" :disabled="acting" @click="resubmit">
                <Spinner v-if="acting" data-icon="inline-start" />
                重新提交报名
              </Button>
            </AlertDescription>
          </Alert>
        </CardContent>
      </Card>

      <Card v-if="team.isBanned" id="ban-appeal" class="scroll-mt-24">
        <CardHeader>
          <CardTitle class="text-base">封禁处理</CardTitle>
          <CardDescription>你的队伍当前处于封禁状态,可提交申诉说明情况</CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <template v-if="banCase">
            <p class="text-sm">
              封禁时间:{{ formatDateTime(banCase.bannedAt) }} · 来源:{{ banCase.source === 'CheatIncident' ? '作弊检测' : '人工处理' }}
            </p>
            <Alert v-if="banCase.appeal">
              <AlertDescription>
                申诉状态:{{ appealStatusLabel(banCase.appeal.status) }}
                <template v-if="banCase.appeal.resolutionReason">
                  · 处理意见:{{ banCase.appeal.resolutionReason }}
                </template>
              </AlertDescription>
            </Alert>
          </template>
          <div v-if="banCase?.canAppeal !== false">
            <Dialog :open="appealOpen" @update:open="setAppealOpen">
              <DialogTrigger as-child>
                <Button variant="outline">提交封禁申诉</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>封禁申诉</DialogTitle>
                  <DialogDescription>向主办方陈述申诉理由,请客观描述事实</DialogDescription>
                </DialogHeader>
                <form @submit.prevent>
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="appeal-statement">申诉陈述</FieldLabel>
                      <Textarea
                        id="appeal-statement"
                        v-model="appealStatement"
                        rows="8"
                        :minlength="minimumAppealStatementLength"
                        :maxlength="maximumAppealStatementLength"
                        aria-describedby="appeal-requirement appeal-error"
                        required
                        @input="appealError = null"
                      />
                      <p id="appeal-requirement" class="text-xs text-muted-foreground">
                        需要 {{ minimumAppealStatementLength }}–{{ maximumAppealStatementLength }} 个字符。
                      </p>
                      <p v-if="appealError" id="appeal-error" role="alert" class="text-sm text-destructive">
                        {{ appealError }}
                      </p>
                    </Field>
                    <Field>
                      <Button type="button" class="w-full" :disabled="appealPending" @click="submitAppeal">
                        <Spinner v-if="appealPending" data-icon="inline-start" />
                        提交申诉
                      </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </DialogContent>
            </Dialog>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle class="text-base">成员({{ team.memberIds?.length ?? 0 }})</CardTitle>
          <CardDescription v-if="!isCaptain">只有队长可以移除成员</CardDescription>
        </CardHeader>
        <CardContent>
          <TeamMembers
            :competition-id="competitionId"
            :team="team"
            :can-manage="isCaptain"
            @changed="load"
          />
        </CardContent>
      </Card>

      <Card v-if="isCaptain">
        <CardHeader>
          <CardTitle class="text-base">邀请成员</CardTitle>
          <CardDescription>
            邀请码在创建队伍时生成;出于安全考虑无法回显当前值,轮换后会显示新的邀请码
          </CardDescription>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <div v-if="invitationToken" class="flex flex-wrap items-center gap-2">
            <code class="rounded bg-muted px-2 py-1 font-mono text-sm">{{ invitationToken }}</code>
            <Button variant="outline" size="sm" @click="copyToken">
              <Copy data-icon="inline-start" />
              复制
            </Button>
          </div>
          <div>
            <Button variant="outline" :disabled="rotating" @click="rotate">
              <Spinner v-if="rotating" data-icon="inline-start" />
              <RefreshCw v-else data-icon="inline-start" />
              轮换邀请码
            </Button>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle class="text-base">队伍管理</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-wrap items-center gap-2">
          <template v-if="isCaptain">
            <Dialog v-model:open="renameOpen">
              <DialogTrigger as-child>
                <Button variant="outline" @click="openRename">修改队名</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>修改队名</DialogTitle>
                </DialogHeader>
                <form @submit.prevent="submitRename">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="rename-input">新队名</FieldLabel>
                      <Input id="rename-input" v-model="renameValue" required maxlength="64" />
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="renamePending">
                        <Spinner v-if="renamePending" data-icon="inline-start" />
                        保存
                      </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </DialogContent>
            </Dialog>

            <Dialog v-model:open="transferOpen">
              <DialogTrigger as-child>
                <Button variant="outline" :disabled="!transferableMembers.length">转让队长</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>转让队长</DialogTitle>
                  <DialogDescription>转让后你将成为普通成员</DialogDescription>
                </DialogHeader>
                <form @submit.prevent="submitTransfer">
                  <FieldGroup>
                    <Field>
                      <FieldLabel>新队长</FieldLabel>
                      <Select v-model="transferTarget">
                        <SelectTrigger><SelectValue placeholder="选择成员" /></SelectTrigger>
                        <SelectContent>
                          <SelectGroup>
                            <SelectItem v-for="id in transferableMembers" :key="id" :value="id">
                              {{ id }}
                            </SelectItem>
                          </SelectGroup>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="transferPending || !transferTarget">
                        <Spinner v-if="transferPending" data-icon="inline-start" />
                        确认转让
                      </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </DialogContent>
            </Dialog>

            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button variant="destructive">解散队伍</Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>确认解散队伍?</AlertDialogTitle>
                  <AlertDialogDescription>
                    解散后所有成员将被移出,队伍的提交与成绩可能受到影响,该操作不可撤销。
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>取消</AlertDialogCancel>
                  <AlertDialogAction :disabled="acting" @click="disband">确认解散</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </template>

          <AlertDialog v-else>
            <AlertDialogTrigger as-child>
              <Button variant="destructive">退出队伍</Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>确认退出队伍?</AlertDialogTitle>
                <AlertDialogDescription>退出后可凭邀请码重新加入,或创建自己的队伍。</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>取消</AlertDialogCancel>
                <AlertDialogAction :disabled="acting" @click="leave">确认退出</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
