<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminCompetitionConfigurationGet,
  adminCompetitionConfigurationUpdate,
  adminUpdateCompetition,
} from '~/api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionConfigurationResponse } from '~/api'
import type { GameModeValue } from '~/utils/game-config'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, competition, canWrite, refresh } = useCompetitionAdmin()

// ---- Basic metadata form ----
const title = ref('')
const description = ref('')
const startTime = ref('')
const endTime = ref('')
const teamRegistrationAutoApprove = ref(false)
const allowTeamRegistrationWhileRunning = ref(false)
const maxTeamMembers = ref(1)
const maxConcurrentRuntimeInstancesPerTeam = ref(1)
const maxActiveQuestionsPerTeam = ref(5)
const maxParticipantMessagesBeforeHandlerReply = ref(3)
const allowChallengeOwnersToHandleQuestions = ref(true)
const practiceModeEnabled = ref(false)
const savingMeta = ref(false)
const metaError = ref<string | null>(null)

watch(competition, (c) => {
  if (!c) return
  title.value = c.title ?? ''
  description.value = c.description ?? ''
  startTime.value = isoToLocalInput(c.startTime)
  endTime.value = isoToLocalInput(c.endTime)
  teamRegistrationAutoApprove.value = c.teamRegistrationAutoApprove ?? false
  allowTeamRegistrationWhileRunning.value = c.allowTeamRegistrationWhileRunning ?? false
  maxTeamMembers.value = c.maxTeamMembers ?? 1
  maxConcurrentRuntimeInstancesPerTeam.value = c.maxConcurrentRuntimeInstancesPerTeam ?? 1
  maxActiveQuestionsPerTeam.value = c.maxActiveQuestionsPerTeam ?? 5
  maxParticipantMessagesBeforeHandlerReply.value = c.maxParticipantMessagesBeforeHandlerReply ?? 3
  allowChallengeOwnersToHandleQuestions.value = c.allowChallengeOwnersToHandleQuestions ?? true
  practiceModeEnabled.value = c.practiceModeEnabled ?? false
}, { immediate: true })

async function saveMeta() {
  metaError.value = null
  const start = localInputToIso(startTime.value)
  const end = localInputToIso(endTime.value)
  if (!title.value.trim() || !start || !end) {
    metaError.value = translate('请完整填写标题与时间')
    return
  }
  savingMeta.value = true
  try {
    const { error } = await adminUpdateCompetition({
      path: { competitionId },
      body: {
        title: title.value.trim(),
        description: description.value.trim() || null,
        startTime: start,
        endTime: end,
        teamRegistrationAutoApprove: teamRegistrationAutoApprove.value,
        allowTeamRegistrationWhileRunning: allowTeamRegistrationWhileRunning.value,
        maxTeamMembers: maxTeamMembers.value,
        maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
        maxActiveQuestionsPerTeam: maxActiveQuestionsPerTeam.value,
        maxParticipantMessagesBeforeHandlerReply: maxParticipantMessagesBeforeHandlerReply.value,
        allowChallengeOwnersToHandleQuestions: allowChallengeOwnersToHandleQuestions.value,
        practiceModeEnabled: practiceModeEnabled.value,
      },
    })
    if (error) throw error
    toast.success(translate("基础信息已保存"))
    await refresh()
  }
  catch (e) {
    toastWriteError(e, refresh)
  }
  finally {
    savingMeta.value = false
  }
}

// ---- Mode-specific configuration JSON ----
const config = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionConfigurationResponse | null>(null)
const configLoading = ref(true)
const savingConfig = ref(false)

async function loadConfig() {
  configLoading.value = true
  const { data, error } = await adminCompetitionConfigurationGet({ path: { competitionId } })
  if (!error && data) config.value = data
  configLoading.value = false
}

async function saveConfig(json: string) {
  if (!config.value) return
  savingConfig.value = true
  try {
    const { data, error } = await adminCompetitionConfigurationUpdate({
      path: { competitionId },
      body: { json, expectedRevision: config.value.revision ?? 0 },
    })
    if (error) throw error
    config.value = data ?? config.value
    toast.success(translate("模式配置已保存"))
  }
  catch (e) {
    toastWriteError(e, loadConfig)
  }
  finally {
    savingConfig.value = false
  }
}

onMounted(loadConfig)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('基础信息') }}</CardTitle>
        <CardDescription>{{ $t('标题、时间与队伍限制;游戏模式创建后不可修改') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="saveMeta">
          <FieldGroup>
            <Alert v-if="metaError" variant="destructive">
              <AlertDescription>{{ metaError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="c-title">{{ $t('标题') }}</FieldLabel>
              <Input id="c-title" v-model="title" :readonly="!canWrite" required />
            </Field>
            <Field>
              <FieldLabel for="c-desc">{{ $t('描述') }}</FieldLabel>
              <Textarea id="c-desc" v-model="description" :readonly="!canWrite" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-start">{{ $t('开始时间') }}</FieldLabel>
                <Input id="c-start" v-model="startTime" type="datetime-local" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-end">{{ $t('结束时间') }}</FieldLabel>
                <Input id="c-end" v-model="endTime" type="datetime-local" :readonly="!canWrite" required />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-members">{{ $t('每队最大人数') }}</FieldLabel>
                <Input id="c-max-members" v-model.number="maxTeamMembers" type="number" min="1" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-max-runtime">{{ $t('每队并发运行时上限') }}</FieldLabel>
                <Input id="c-max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" type="number" min="1" :readonly="!canWrite" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-auto-approve" v-model="teamRegistrationAutoApprove" :disabled="!canWrite" />
              <FieldLabel for="c-auto-approve" class="font-normal">{{ $t('队伍注册自动通过') }}</FieldLabel>
            </Field>
            <Field v-if="competition?.mode === 'Ctf'" orientation="horizontal">
              <Checkbox id="c-practice-mode" v-model="practiceModeEnabled" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-practice-mode" class="font-normal">{{ $t('赛后练习模式') }}</FieldLabel>
                <FieldDescription>{{ $t('比赛结束后，已通过审核且未封禁的原参赛队伍可以启动 Container 或 Compose 靶机并验证 Flag；练习不计分，也不产生血榜。') }}</FieldDescription>
              </div>
            </Field>
            <Field orientation="horizontal">
              <Checkbox id="c-allow-running-registration" v-model="allowTeamRegistrationWhileRunning" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-allow-running-registration" class="font-normal">{{ $t('比赛进行中仍允许创建队伍') }}</FieldLabel>
                <FieldDescription>{{ $t('关闭时，比赛进入 Running 后停止接收新队伍与重新报名。') }}</FieldDescription>
              </div>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-active-questions">{{ $t('每队活跃咨询上限') }}</FieldLabel>
                <Input id="c-max-active-questions" v-model.number="maxActiveQuestionsPerTeam" type="number" min="1" :readonly="!canWrite" required />
                <FieldDescription>{{ $t('待处理与已回复的咨询计入上限。') }}</FieldDescription>
              </Field>
              <Field>
                <FieldLabel for="c-max-participant-messages">{{ $t('连续补充消息上限') }}</FieldLabel>
                <Input id="c-max-participant-messages" v-model.number="maxParticipantMessagesBeforeHandlerReply" type="number" min="1" :readonly="!canWrite" required />
                <FieldDescription>{{ $t('初始提问计入额度；工作人员回复后重置。') }}</FieldDescription>
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-allow-challenge-owner-questions" v-model="allowChallengeOwnersToHandleQuestions" :disabled="!canWrite" />
              <div class="grid gap-1.5 leading-none">
                <FieldLabel for="c-allow-challenge-owner-questions" class="font-normal">{{ $t('允许题目所有者处理关联咨询') }}</FieldLabel>
                <FieldDescription>{{ $t('仅限其拥有或协作的题目；不会授予 Flag、Token 或平台密钥权限。') }}</FieldDescription>
              </div>
            </Field>
            <Field v-if="canWrite">
              <Button type="submit" :disabled="savingMeta">
                <Spinner v-if="savingMeta" data-icon="inline-start" /> {{ $t('保存基础信息') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ $t('模式配置') }}</CardTitle>
        <CardDescription>
          {{ $t('{mode} 模式的专属配置；保存时携带修订版本进行乐观并发校验', { mode: enumLabel(GameModeLabel, config?.mode) }) }}
        </CardDescription>
      </CardHeader>
      <CardContent>
        <CompetitionModeConfigEditor
          :mode="(config?.mode ?? competition?.mode ?? 'Ctf') as GameModeValue"
          :json="config?.json"
          :revision="config?.revision"
          :readonly="!canWrite"
          :loading="configLoading"
          :saving="savingConfig"
          @save="saveConfig"
        />
      </CardContent>
    </Card>
  </div>
</template>
