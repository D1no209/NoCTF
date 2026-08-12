<script setup lang="ts">
import { toast } from 'vue-sonner'
import { adminCreateCompetition } from '~/api'
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '~/api'

definePageMeta({ middleware: 'auth' })

const { canOrganize } = useAuth()

const title = ref('')
const description = ref('')
const mode = ref<NoCtfapiEndpointsCompetitionsGameModeProtocol>('Ctf')
const startTime = ref('')
const endTime = ref('')
const teamRegistrationAutoApprove = ref(false)
const allowTeamRegistrationWhileRunning = ref(false)
const maxTeamMembers = ref(5)
const maxConcurrentRuntimeInstancesPerTeam = ref(1)
const maxActiveQuestionsPerTeam = ref(5)
const maxParticipantMessagesBeforeHandlerReply = ref(3)
const allowChallengeOwnersToHandleQuestions = ref(true)
const practiceModeEnabled = ref(false)
const error = ref<string | null>(null)
const pending = ref(false)

async function submit() {
  error.value = null
  if (!title.value.trim()) {
    error.value = translate('请输入竞赛标题')
    return
  }
  const start = localInputToIso(startTime.value)
  const end = localInputToIso(endTime.value)
  if (!start || !end) {
    error.value = translate('请选择开始和结束时间')
    return
  }
  if (new Date(start) >= new Date(end)) {
    error.value = translate('开始时间必须早于结束时间')
    return
  }
  pending.value = true
  try {
    const { data, error: e } = await adminCreateCompetition({
      body: {
        title: title.value.trim(),
        description: description.value.trim() || null,
        mode: mode.value,
        startTime: start,
        endTime: end,
        teamRegistrationAutoApprove: teamRegistrationAutoApprove.value,
        allowTeamRegistrationWhileRunning: allowTeamRegistrationWhileRunning.value,
        maxTeamMembers: maxTeamMembers.value,
        maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
        maxActiveQuestionsPerTeam: maxActiveQuestionsPerTeam.value,
        maxParticipantMessagesBeforeHandlerReply: maxParticipantMessagesBeforeHandlerReply.value,
        allowChallengeOwnersToHandleQuestions: allowChallengeOwnersToHandleQuestions.value,
        practiceModeEnabled: mode.value === 'Ctf' && practiceModeEnabled.value,
      },
    })
    if (e || !data) throw e
    toast.success(translate("竞赛已创建"))
    await navigateTo(`/admin/competitions/${data.id}`)
  }
  catch (e) {
    error.value = parseApiError(e).message
  }
  finally {
    pending.value = false
  }
}
</script>

<template>
  <div class="mx-auto flex max-w-2xl flex-col px-4 py-8">
    <Alert v-if="!canOrganize" variant="destructive" class="mb-4">
      <AlertDescription>{{ $t('当前账户没有创建竞赛的权限') }}</AlertDescription>
    </Alert>
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('新建竞赛') }}</CardTitle>
        <CardDescription>{{ $t('创建后游戏模式不可修改,其余信息可稍后调整') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="title">{{ $t('标题') }}</FieldLabel>
              <Input id="title" v-model="title" required maxlength="200" />
            </Field>
            <Field>
              <FieldLabel for="description">{{ $t('描述') }}</FieldLabel>
              <Textarea id="description" v-model="description" :placeholder="$t('可选')" />
            </Field>
            <Field>
              <FieldLabel for="mode">{{ $t('游戏模式') }}</FieldLabel>
              <Select id="mode" v-model="mode">
                <SelectTrigger class="w-full">
                  <SelectValue :placeholder="$t('选择模式')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="Ctf">CTF</SelectItem>
                    <SelectItem value="Awd">AWD</SelectItem>
                    <SelectItem value="Awdp">AWDP</SelectItem>
                    <SelectItem value="Koh">KoH</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
              <FieldDescription>{{ $t('创建后不可修改') }}</FieldDescription>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="start">{{ $t('开始时间') }}</FieldLabel>
                <Input id="start" v-model="startTime" type="datetime-local" required />
              </Field>
              <Field>
                <FieldLabel for="end">{{ $t('结束时间') }}</FieldLabel>
                <Input id="end" v-model="endTime" type="datetime-local" required />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="max-members">{{ $t('每队最大人数') }}</FieldLabel>
                <Input id="max-members" v-model.number="maxTeamMembers" type="number" min="1" required />
              </Field>
              <Field>
                <FieldLabel for="max-runtime">{{ $t('每队并发运行时上限') }}</FieldLabel>
                <Input id="max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" type="number" min="1" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="auto-approve" v-model="teamRegistrationAutoApprove" />
              <FieldLabel for="auto-approve" class="font-normal">{{ $t('队伍注册自动通过(无需审批)') }}</FieldLabel>
            </Field>
            <Field orientation="horizontal">
              <Checkbox id="allow-running-registration" v-model="allowTeamRegistrationWhileRunning" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="allow-running-registration" class="font-normal">{{ $t('比赛进行中仍允许创建队伍') }}</FieldLabel>
                <FieldDescription>{{ $t('关闭时，比赛进入 Running 后停止接收新队伍与重新报名。') }}</FieldDescription>
              </div>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="max-active-questions">{{ $t('每队活跃咨询上限') }}</FieldLabel>
                <Input id="max-active-questions" v-model.number="maxActiveQuestionsPerTeam" type="number" min="1" required />
                <FieldDescription>{{ $t('待处理与已回复的咨询计入上限。') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="max-participant-messages">{{ $t('连续补充消息上限') }}</FieldLabel>
                <Input id="max-participant-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply" type="number" min="1" required />
                <FieldDescription>{{ $t('初始提问计入额度；工作人员回复后重置。') }}</FieldDescription>
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="allow-challenge-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="allow-challenge-owner-questions" class="font-normal">{{ $t('允许题目所有者处理关联咨询') }}</FieldLabel>
                <FieldDescription>{{ $t('仅限其拥有或协作的题目；不会授予 Flag、Token 或平台密钥权限。') }}</FieldDescription>
              </div>
            </Field>
            <Field v-if="mode === 'Ctf'" orientation="horizontal">
              <Checkbox id="practice-mode" v-model="practiceModeEnabled" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="practice-mode" class="font-normal">{{ $t('比赛结束后开放练习模式') }}</FieldLabel>
                <FieldDescription>{{ $t('已审核且未封禁的原参赛队伍可启动容器题环境并验证 Flag；不产生分数、血榜或排行榜变化。') }}</FieldDescription>
              </div>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending || !canOrganize" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('创建竞赛') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
