<script setup lang="ts">
import { canEnterCompetition, isCtfPracticeOpen } from '~/lib/competition-participation'
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
  listCompetitionTracks,
} from '~/api'
import type {
  NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse,
  NoCtfapiEndpointsTeamsTeamResponse,
} from '~/api'
import { teamMembershipErrorMessage, teamRegistrationErrorMessage } from '~/lib/competition-track'

const route = useRoute()
const competitionId = route.params.id as string
const ctx = inject(competitionContextKey)!
const { user, isLoggedIn } = useAuth()

const competition = computed(() => ctx.competition.value)

// 我的参赛状态
const myTeam = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const teamLoaded = ref(false)
const teamLoadError = ref<string | null>(null)

async function loadMyTeam() {
  teamLoaded.value = false
  teamLoadError.value = null
  if (!isLoggedIn.value) {
    myTeam.value = null
    teamLoaded.value = true
    return
  }
  try {
    const { data, error, response } = await getMyTeamEndpoint({ path: { competitionId } })
    if (response?.status === 404) {
      myTeam.value = null
      return
    }
    if (error || !data) {
      myTeam.value = null
      teamLoadError.value = parseApiError(error, translate('加载报名状态失败，请重试。')).message
      return
    }
    myTeam.value = data
  } catch (error: unknown) {
    myTeam.value = null
    teamLoadError.value = parseApiError(error, translate('加载报名状态失败，请重试。')).message
  } finally {
    teamLoaded.value = true
  }
}

onMounted(loadMyTeam)
watch(isLoggedIn, loadMyTeam)

// 已报名队伍数
const approvedTeamCount = ref<number | null>(null)
const selectableTracks = ref<NoCtfapiEndpointsCompetitionsTracksCompetitionTrackResponse[]>([])
const tracksLoaded = ref(false)
const trackLoadError = ref<string | null>(null)

async function loadRegistrationOptions() {
  tracksLoaded.value = false
  trackLoadError.value = null
  try {
    const [teams, tracks] = await Promise.all([
      listCompetitionTeamsEndpoint({ path: { competitionId } }),
      listCompetitionTracks({ path: { competitionId } }),
    ])
    if (!teams.error && teams.data) approvedTeamCount.value = (teams.data.items ?? []).filter(
      (team) => team.registrationStatus === 'Approved',
    ).length
    if (tracks.error || !tracks.data) {
      selectableTracks.value = []
      trackLoadError.value = parseApiError(
        tracks.error,
        translate('加载参赛赛道失败，请重试。'),
      ).message
    } else {
      selectableTracks.value = (tracks.data.items ?? []).filter(track => track.isPublicSelectable)
    }
  } catch (error: unknown) {
    selectableTracks.value = []
    trackLoadError.value = parseApiError(
      error,
      translate('加载参赛赛道失败，请重试。'),
    ).message
  } finally {
    tracksLoaded.value = true
  }
}

onMounted(loadRegistrationOptions)

// 倒计时
const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  timer = setInterval(() => {
    now.value = Date.now()
  }, 1_000)
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

const practiceOpen = computed(() => isCtfPracticeOpen(competition.value))
const canParticipate = computed(() => canEnterCompetition(competition.value, myTeam.value))

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
const createTrackKey = ref('')
const createTrackInvitationCode = ref('')
const createPending = ref(false)
const createValidationError = ref<string | null>(null)
const selectedCreateTrack = computed(() => selectableTracks.value.find(
  track => track.key === createTrackKey.value,
))

watch(createTrackKey, () => {
  createTrackInvitationCode.value = ''
  createValidationError.value = null
})

watch(createOpen, (open) => {
  if (!open) createValidationError.value = null
})

async function submitCreate() {
  createValidationError.value = null
  if (!createName.value.trim()) {
    createValidationError.value = translate('请输入队伍名称。')
    return
  }
  if (!createTrackKey.value) {
    createValidationError.value = translate('请选择参赛赛道。')
    return
  }
  if (selectedCreateTrack.value?.requiresInvitationCode
    && !createTrackInvitationCode.value.trim()) {
    createValidationError.value = translate('请输入赛道邀请码。')
    return
  }
  createPending.value = true
  try {
    const { data, error } = await createTeamEndpoint({
      path: { competitionId },
      body: {
        name: createName.value.trim(),
        trackKey: createTrackKey.value,
        trackInvitationCode: selectedCreateTrack.value?.requiresInvitationCode
          ? createTrackInvitationCode.value.trim()
          : null,
      },
    })
    if (error || !data) {
      createValidationError.value = teamRegistrationErrorMessage(
        error,
        translate('创建队伍失败'),
      )
      return
    }
    toast.success(translate('队伍创建成功'))
    createOpen.value = false
    createName.value = ''
    createTrackKey.value = ''
    createTrackInvitationCode.value = ''
    await loadMyTeam()
  } catch (error: unknown) {
    createValidationError.value = teamRegistrationErrorMessage(
      error,
      translate('创建队伍失败'),
    )
  } finally {
    createPending.value = false
  }
}

// 凭邀请 token 加入
const joinOpen = ref(false)
const joinToken = ref('')
const joinPending = ref(false)
const joinValidationError = ref<string | null>(null)

watch(joinToken, () => { joinValidationError.value = null })
watch(joinOpen, (open) => { if (!open) joinValidationError.value = null })

async function submitJoin() {
  if (joinPending.value) return
  joinValidationError.value = null
  const invitationToken = joinToken.value.trim()
  if (invitationToken.length !== 32) {
    joinValidationError.value = translate('邀请码必须为 32 位。')
    return
  }
  joinPending.value = true
  try {
    const { error } = await joinTeamByInvitationEndpoint({
      path: { competitionId },
      body: { invitationToken },
    })
    if (error) {
      joinValidationError.value = teamMembershipErrorMessage(
        error,
        translate('加入队伍失败，请重试。'),
      )
      return
    }
    toast.success(translate('已加入队伍'))
    joinOpen.value = false
    joinToken.value = ''
    await loadMyTeam()
  } catch (error: unknown) {
    joinValidationError.value = teamMembershipErrorMessage(
      error,
      translate('加入队伍失败，请重试。'),
    )
  } finally {
    joinPending.value = false
  }
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

          <p v-if="practiceOpen" class="text-sm text-muted-foreground">
            {{ $t('赛后练习已开放，原已审核通过且未封禁的队伍可进入；练习不影响正式成绩。') }}
          </p>

          <div class="flex flex-wrap items-center gap-3 pt-1">
            <template v-if="!teamLoaded">
              <Skeleton class="h-10 w-32" />
            </template>

            <template v-else-if="!isLoggedIn">
              <Button as-child size="lg">
                <NuxtLink :to="`/auth/login?redirect=/competitions/${competitionId}`">
                  <LogIn data-icon="inline-start" /> {{ practiceOpen ? $t('登录后进入练习') : $t('登录 / 注册后报名') }} </NuxtLink>
              </Button>
            </template>

            <template v-else-if="teamLoadError">
              <Alert variant="destructive" class="w-auto">
                <AlertDescription class="flex items-center gap-3">
                  <span>{{ teamLoadError }}</span>
                  <Button type="button" size="sm" variant="outline" @click="loadMyTeam">
                    {{ $t('重试') }}
                  </Button>
                </AlertDescription>
              </Alert>
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
                  <form novalidate @submit.prevent="submitCreate">
                    <FieldGroup>
                      <Field>
                        <FieldLabel for="team-name">{{ $t('队伍名称') }}</FieldLabel>
                        <Input id="team-name" v-model="createName" required maxlength="64" />
                      </Field>
                      <Field v-if="!tracksLoaded">
                        <Skeleton class="h-10 w-full" />
                        <FieldDescription>{{ $t('正在加载可报名赛道…') }}</FieldDescription>
                      </Field>
                      <Field v-else-if="trackLoadError">
                        <Alert variant="destructive">
                          <AlertDescription class="flex items-center justify-between gap-3">
                            <span>{{ trackLoadError }}</span>
                            <Button type="button" size="sm" variant="outline" @click="loadRegistrationOptions">
                              {{ $t('重试') }}
                            </Button>
                          </AlertDescription>
                        </Alert>
                      </Field>
                      <Field v-else-if="selectableTracks.length > 0">
                        <FieldLabel for="team-track">{{ $t('参赛赛道') }}</FieldLabel>
                        <Select v-model="createTrackKey" required>
                          <SelectTrigger id="team-track"><SelectValue :placeholder="$t('选择赛道')" /></SelectTrigger>
                          <SelectContent>
                            <SelectItem v-for="track in selectableTracks" :key="track.key" :value="track.key!">
                              {{ track.name }}
                            </SelectItem>
                          </SelectContent>
                        </Select>
                        <FieldDescription>{{ $t('请选择队伍参加的赛道；创建后比赛管理员仍可调整。') }}</FieldDescription>
                      </Field>
                      <Field v-else>
                        <Alert variant="destructive">
                          <AlertDescription>{{ $t('当前没有可报名的赛道。') }}</AlertDescription>
                        </Alert>
                      </Field>
                      <Field v-if="selectedCreateTrack?.requiresInvitationCode">
                        <FieldLabel for="track-invitation-code">{{ $t('赛道邀请码') }}</FieldLabel>
                        <Input
                          id="track-invitation-code"
                          v-model="createTrackInvitationCode"
                          type="password"
                          required
                          autocomplete="off"
                          maxlength="128"
                        />
                      </Field>
                      <p v-if="createValidationError" role="alert" class="text-sm text-destructive">
                        {{ createValidationError }}
                      </p>
                      <Field>
                        <Button
                          type="submit"
                          class="w-full"
                          :disabled="createPending || !tracksLoaded || Boolean(trackLoadError) || selectableTracks.length === 0"
                        >
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
                        <Input id="invitation-token" v-model="joinToken" required minlength="32" maxlength="32" autocomplete="off" />
                      </Field>
                      <p v-if="joinValidationError" role="alert" class="text-sm text-destructive">{{ joinValidationError }}</p>
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
                <NuxtLink :to="`/competitions/${competitionId}/challenges`"> {{ practiceOpen ? $t('进入练习') : $t('进入比赛') }} <ArrowRight data-icon="inline-end" />
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
    <Alert v-if="myTeam">
      <AlertDescription>{{ $t('当前赛道：{track}', { track: myTeam.trackName ?? myTeam.trackKey ?? '-' }) }}</AlertDescription>
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
