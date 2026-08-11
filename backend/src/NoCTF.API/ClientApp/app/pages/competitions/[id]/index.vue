<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  ArrowRight,
  Box,
  CalendarRange,
  Clock,
  FileText,
  KeyRound,
  LogIn,
  ShieldCheck,
  Trophy,
  UserPlus,
  Users,
} from '@lucide/vue'
import {
  createTeamEndpoint,
  getMyTeamEndpoint,
  joinTeamByInvitationEndpoint,
  listCompetitionTeamsEndpoint,
} from '~/api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '~/api'

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const { user, isLoggedIn } = useAuth()

const competition = computed(() => ctx.competition.value)

// 我的参赛状态
const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const teamLoaded = ref(false)

async function loadMyTeam() {
  if (!isLoggedIn.value) {
    myTeam.value = null
    teamLoaded.value = true
    return
  }
  const { data, error } = await getMyTeamEndpoint({ path: { competitionId } })
  teamLoaded.value = true
  myTeam.value = error || !data ? null : data
}

onMounted(loadMyTeam)
watch(isLoggedIn, loadMyTeam)

// 已报名队伍数
const approvedTeamCount = ref<number | null>(null)
onMounted(async () => {
  const { data, error } = await listCompetitionTeamsEndpoint({ path: { competitionId } })
  if (error || !data) return
  approvedTeamCount.value = (data.items ?? []).filter(
    (team) => team.registrationStatus === 'Approved',
  ).length
})

// 倒计时
const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  timer = setInterval(() => {
    now.value = Date.now()
  }, 30_000)
})
onUnmounted(() => clearInterval(timer))

const countdown = computed(() => {
  const c = competition.value
  if (!c) return null
  const start = new Date(c.startTime ?? '').getTime()
  const end = new Date(c.endTime ?? '').getTime()
  if (Number.isNaN(start) || Number.isNaN(end)) return null
  if (now.value < start) return { label: translate("距开始"), ms: start - now.value }
  if (now.value < end) return { label: translate("距结束"), ms: end - now.value }
  return { label: translate("已结束"), ms: 0 }
})

const canParticipate = computed(
  () =>
    myTeam.value?.registrationStatus === 'Approved'
    && !myTeam.value?.isBanned
    && competition.value?.status === 'Running',
)

const teamRegistrationOpen = computed(() => {
  const status = competition.value?.status
  return status === 'Visible'
    || status === 'Published'
    || status === 'Running'
      && competition.value?.allowTeamRegistrationWhileRunning === true
})

// 创建队伍
const createOpen = ref(false)
const createName = ref('')
const createPending = ref(false)

async function submitCreate() {
  if (!createName.value.trim()) return
  createPending.value = true
  const { data, error } = await createTeamEndpoint({
    path: { competitionId },
    body: { name: createName.value.trim() },
  })
  createPending.value = false
  if (error || !data) {
    toast.error(parseApiError(error, translate("创建队伍失败")).message)
    return
  }
  toast.success(translate("队伍创建成功"))
  createOpen.value = false
  createName.value = ''
  await loadMyTeam()
}

// 凭邀请 token 加入
const joinOpen = ref(false)
const joinToken = ref('')
const joinPending = ref(false)

async function submitJoin() {
  if (!joinToken.value.trim()) return
  joinPending.value = true
  const { error } = await joinTeamByInvitationEndpoint({
    path: { competitionId },
    body: { invitationToken: joinToken.value.trim() },
  })
  joinPending.value = false
  if (error) {
    toast.error(parseApiError(error, translate("加入队伍失败,请检查邀请码")).message)
    return
  }
  toast.success(translate("已加入队伍"))
  joinOpen.value = false
  joinToken.value = ''
  await loadMyTeam()
}

const isCaptain = computed(
  () => myTeam.value && user.value && myTeam.value.captainId === user.value.userId,
)
</script>

<template>
  <div v-if="competition" class="flex flex-col gap-6">
    <!-- Hero -->
    <Card class="overflow-hidden">
      <div class="grid gap-8 p-6 sm:p-8 lg:grid-cols-[minmax(0,1fr)_17rem] lg:gap-12">
        <div class="flex min-w-0 flex-col gap-5">
          <div class="flex flex-wrap items-center gap-2">
            <ModeBadge :mode="competition.mode" />
            <LifecycleBadge :status="competition.status" />
          </div>

          <h1 class="text-display text-3xl sm:text-4xl">{{ competition.title }}</h1>

          <p class="flex items-center gap-2 font-mono text-xs text-muted-foreground tabular-nums sm:text-sm">
            <CalendarRange class="size-4 shrink-0" />
            {{ formatDateTime(competition.startTime) }} ~ {{ formatDateTime(competition.endTime) }}
          </p>

          <div v-if="countdown" class="flex flex-wrap items-end gap-x-3 gap-y-1">
            <span v-if="countdown.ms > 0" class="flex items-center gap-1.5 pb-2 text-sm text-muted-foreground">
              <Clock class="size-4" />
              {{ countdown.label }}
            </span>
            <span class="font-mono text-3xl font-semibold text-primary tabular-nums sm:text-4xl">
              {{ countdown.ms > 0 ? formatDuration(countdown.ms) : countdown.label }}
            </span>
          </div>

          <div class="flex flex-wrap items-center gap-3 pt-1">
            <template v-if="!teamLoaded">
              <Skeleton class="h-10 w-32" />
            </template>

            <template v-else-if="!isLoggedIn">
              <Button as-child size="lg">
                <NuxtLink :to="`/auth/login?redirect=/competitions/${competitionId}`">
                  <LogIn data-icon="inline-start" /> {{ $t('登录 / 注册后报名') }} </NuxtLink>
              </Button>
            </template>

            <template v-else-if="!myTeam">
              <Dialog v-if="teamRegistrationOpen" v-model:open="createOpen">
                <DialogTrigger as-child>
                  <Button size="lg">
                    <UserPlus data-icon="inline-start" /> {{ $t('立即报名') }} </Button>
                </DialogTrigger>
                <DialogContent>
                  <DialogHeader>
                    <DialogTitle>{{ $t('创建队伍') }}</DialogTitle>
                    <DialogDescription>{{ $t('队伍创建后你就是队长,可邀请成员加入') }}</DialogDescription>
                  </DialogHeader>
                  <form @submit.prevent="submitCreate">
                    <FieldGroup>
                      <Field>
                        <FieldLabel for="team-name">{{ $t('队伍名称') }}</FieldLabel>
                        <Input id="team-name" v-model="createName" required maxlength="64" />
                      </Field>
                      <Field>
                        <Button type="submit" class="w-full" :disabled="createPending">
                          <Spinner v-if="createPending" data-icon="inline-start" /> {{ $t('创建') }} </Button>
                      </Field>
                    </FieldGroup>
                  </form>
                </DialogContent>
              </Dialog>

              <Alert v-else class="w-auto">
                <AlertDescription>{{ $t('当前比赛阶段已关闭新队伍创建。') }}</AlertDescription>
              </Alert>

              <Dialog v-model:open="joinOpen">
                <DialogTrigger as-child>
                  <Button size="lg" variant="outline">
                    <KeyRound data-icon="inline-start" /> {{ $t('凭邀请码加入') }} </Button>
                </DialogTrigger>
                <DialogContent>
                  <DialogHeader>
                    <DialogTitle>{{ $t('加入队伍') }}</DialogTitle>
                    <DialogDescription>{{ $t('输入队长分享给你的 32 位邀请码') }}</DialogDescription>
                  </DialogHeader>
                  <form @submit.prevent="submitJoin">
                    <FieldGroup>
                      <Field>
                        <FieldLabel for="invitation-token">{{ $t('邀请码') }}</FieldLabel>
                        <Input id="invitation-token" v-model="joinToken" required />
                      </Field>
                      <Field>
                        <Button type="submit" class="w-full" :disabled="joinPending">
                          <Spinner v-if="joinPending" data-icon="inline-start" /> {{ $t('加入') }} </Button>
                      </Field>
                    </FieldGroup>
                  </form>
                </DialogContent>
              </Dialog>
            </template>

            <template v-else>
              <Button v-if="canParticipate" as-child size="lg">
                <NuxtLink :to="`/competitions/${competitionId}/challenges`"> {{ $t('进入比赛') }} <ArrowRight data-icon="inline-end" />
                </NuxtLink>
              </Button>
              <Button as-child size="lg" variant="outline">
                <NuxtLink :to="`/competitions/${competitionId}/my/team`">{{ $t('我的队伍') }}</NuxtLink>
              </Button>
            </template>
          </div>
        </div>

        <dl class="flex flex-col justify-center gap-4 lg:border-l lg:pl-10">
          <div class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <Users class="size-3.5" />
              {{ $t('队伍人数上限') }}
            </dt>
            <dd class="font-mono text-sm tabular-nums">
              {{ $t('{count} 人', { count: competition.maxTeamMembers ?? '-' }) }}
            </dd>
          </div>
          <div class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <Box class="size-3.5" />
              {{ $t('同时环境上限') }}
            </dt>
            <dd class="font-mono text-sm tabular-nums">
              {{ $t('{count} 个', { count: competition.maxConcurrentRuntimeInstancesPerTeam ?? '-' }) }}
            </dd>
          </div>
          <div class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <ShieldCheck class="size-3.5" />
              {{ $t('审核方式') }}
            </dt>
            <dd class="text-sm">
              {{ competition.teamRegistrationAutoApprove ? $t('报名自动通过') : $t('报名需审核') }}
            </dd>
          </div>
          <div v-if="approvedTeamCount !== null" class="flex items-center justify-between gap-4">
            <dt class="flex items-center gap-2 text-xs text-muted-foreground">
              <Trophy class="size-3.5" />
              {{ $t('报名队伍') }}
            </dt>
            <dd class="font-mono text-sm tabular-nums">
              {{ $t('{count} 支', { count: approvedTeamCount }) }}
            </dd>
          </div>
        </dl>
      </div>
    </Card>

    <!-- 我的队伍状态提示 -->
    <Alert v-if="myTeam && myTeam.registrationStatus === 'Pending'">
      <ShieldCheck class="size-4" />
      <AlertDescription>
        {{ $t('队伍「{team}」报名已提交，等待主办方审核通过后即可参赛。', { team: myTeam.name ?? '-' }) }}
      </AlertDescription>
    </Alert>
    <Alert v-else-if="myTeam && myTeam.registrationStatus === 'Rejected'" variant="destructive">
      <AlertDescription>
        队伍「{{ myTeam.name }}」报名被拒绝{{ isCaptain ? $t(',可在「我的队伍」页修改信息后重新提交') : '' }}。
      </AlertDescription>
    </Alert>
    <Alert v-else-if="myTeam?.isBanned" variant="destructive">
      <AlertDescription>{{ $t('队伍「{team}」已被封禁，如有异议请联系主办方。', { team: myTeam.name ?? '-' }) }}</AlertDescription>
    </Alert>

    <!-- 竞赛介绍 -->
    <Card>
      <CardHeader>
        <CardTitle class="flex items-center gap-2 text-base">
          <FileText class="size-4" /> {{ $t('竞赛介绍') }} </CardTitle>
      </CardHeader>
      <CardContent>
        <p v-if="competition.description" class="whitespace-pre-line text-sm leading-7">
          {{ competition.description }}
        </p>
        <p v-else class="text-sm text-muted-foreground">{{ $t('主办方还没有填写竞赛介绍。') }}</p>
      </CardContent>
    </Card>
  </div>
</template>
