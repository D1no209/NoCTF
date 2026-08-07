<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  createTeamEndpoint,
  getMyTeamEndpoint,
  joinTeamByInvitationEndpoint,
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
    toast.error(parseApiError(error, '创建队伍失败').message)
    return
  }
  toast.success('队伍创建成功')
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
    toast.error(parseApiError(error, '加入队伍失败,请检查邀请码').message)
    return
  }
  toast.success('已加入队伍')
  joinOpen.value = false
  joinToken.value = ''
  await loadMyTeam()
}

const isCaptain = computed(
  () => myTeam.value && user.value && myTeam.value.captainId === user.value.userId,
)
</script>

<template>
  <div v-if="competition" class="grid gap-6 lg:grid-cols-3">
    <Card class="lg:col-span-2">
      <CardHeader>
        <CardTitle>竞赛介绍</CardTitle>
      </CardHeader>
      <CardContent>
        <p v-if="competition.description" class="whitespace-pre-line text-sm leading-6">
          {{ competition.description }}
        </p>
        <p v-else class="text-sm text-muted-foreground">主办方还没有填写竞赛介绍。</p>
        <Separator class="my-4" />
        <dl class="grid gap-3 text-sm sm:grid-cols-2">
          <div>
            <dt class="text-muted-foreground">开始时间</dt>
            <dd>{{ formatDateTime(competition.startTime) }}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">结束时间</dt>
            <dd>{{ formatDateTime(competition.endTime) }}</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">队伍人数上限</dt>
            <dd>{{ competition.maxTeamMembers ?? '—' }} 人</dd>
          </div>
          <div>
            <dt class="text-muted-foreground">报名审核</dt>
            <dd>{{ competition.teamRegistrationAutoApprove ? '自动通过' : '需要主办方审核' }}</dd>
          </div>
        </dl>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>我的参赛状态</CardTitle>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <Skeleton v-if="!teamLoaded" class="h-20 w-full" />

        <template v-else-if="!isLoggedIn">
          <p class="text-sm text-muted-foreground">登录后即可创建或加入队伍参赛。</p>
          <Button as-child class="w-full">
            <NuxtLink :to="`/auth/login?redirect=/competitions/${competitionId}`">登录 / 注册</NuxtLink>
          </Button>
        </template>

        <template v-else-if="!myTeam">
          <p class="text-sm text-muted-foreground">你还没有加入本竞赛的队伍。</p>
          <div class="flex flex-col gap-2">
            <Dialog v-model:open="createOpen">
              <DialogTrigger as-child>
                <Button class="w-full">创建队伍</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>创建队伍</DialogTitle>
                  <DialogDescription>队伍创建后你就是队长,可邀请成员加入</DialogDescription>
                </DialogHeader>
                <form @submit.prevent="submitCreate">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="team-name">队伍名称</FieldLabel>
                      <Input id="team-name" v-model="createName" required maxlength="64" />
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="createPending">
                        <Spinner v-if="createPending" data-icon="inline-start" />
                        创建
                      </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </DialogContent>
            </Dialog>

            <Dialog v-model:open="joinOpen">
              <DialogTrigger as-child>
                <Button variant="outline" class="w-full">凭邀请码加入</Button>
              </DialogTrigger>
              <DialogContent>
                <DialogHeader>
                  <DialogTitle>加入队伍</DialogTitle>
                  <DialogDescription>输入队长分享给你的 32 位邀请码</DialogDescription>
                </DialogHeader>
                <form @submit.prevent="submitJoin">
                  <FieldGroup>
                    <Field>
                      <FieldLabel for="invitation-token">邀请码</FieldLabel>
                      <Input id="invitation-token" v-model="joinToken" required />
                    </Field>
                    <Field>
                      <Button type="submit" class="w-full" :disabled="joinPending">
                        <Spinner v-if="joinPending" data-icon="inline-start" />
                        加入
                      </Button>
                    </Field>
                  </FieldGroup>
                </form>
              </DialogContent>
            </Dialog>
          </div>
        </template>

        <template v-else>
          <div class="flex items-center gap-2">
            <span class="font-medium">{{ myTeam.name }}</span>
            <Badge
              :variant="myTeam.registrationStatus === TeamRegistrationStatus.Approved ? 'default' : myTeam.registrationStatus === TeamRegistrationStatus.Rejected ? 'destructive' : 'secondary'"
            >
              {{ teamRegistrationStatusLabel(myTeam.registrationStatus) }}
            </Badge>
            <Badge v-if="myTeam.isBanned" variant="destructive">已封禁</Badge>
          </div>
          <Alert v-if="myTeam.registrationStatus === TeamRegistrationStatus.Pending">
            <AlertDescription>报名已提交,等待主办方审核通过后即可参赛。</AlertDescription>
          </Alert>
          <Alert v-else-if="myTeam.registrationStatus === TeamRegistrationStatus.Rejected" variant="destructive">
            <AlertDescription>
              报名被拒绝{{ isCaptain ? ',可在「我的队伍」页修改信息后重新提交' : '' }}。
            </AlertDescription>
          </Alert>
          <Alert v-else>
            <AlertDescription>报名已通过,去题目区开始解题吧。</AlertDescription>
          </Alert>
          <Button variant="outline" as-child class="w-full">
            <NuxtLink :to="`/competitions/${competitionId}/my/team`">进入我的队伍</NuxtLink>
          </Button>
        </template>
      </CardContent>
    </Card>
  </div>
</template>
