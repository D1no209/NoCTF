<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminCompetitionConfigurationGet,
  adminCompetitionConfigurationUpdate,
  adminUpdateCompetition,
} from '~/api'
import type { NoCtfapiEndpointsAdministrationCompetitionsCompetitionConfigurationResponse } from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, competition, canWrite, refresh } = useCompetitionAdmin()

// ---- Basic metadata form ----
const title = ref('')
const description = ref('')
const startTime = ref('')
const endTime = ref('')
const teamRegistrationAutoApprove = ref(false)
const maxTeamMembers = ref(1)
const maxConcurrentRuntimeInstancesPerTeam = ref(1)
const savingMeta = ref(false)
const metaError = ref<string | null>(null)

watch(competition, (c) => {
  if (!c) return
  title.value = c.title ?? ''
  description.value = c.description ?? ''
  startTime.value = isoToLocalInput(c.startTime)
  endTime.value = isoToLocalInput(c.endTime)
  teamRegistrationAutoApprove.value = c.teamRegistrationAutoApprove ?? false
  maxTeamMembers.value = c.maxTeamMembers ?? 1
  maxConcurrentRuntimeInstancesPerTeam.value = c.maxConcurrentRuntimeInstancesPerTeam ?? 1
}, { immediate: true })

async function saveMeta() {
  metaError.value = null
  const start = localInputToIso(startTime.value)
  const end = localInputToIso(endTime.value)
  if (!title.value.trim() || !start || !end) {
    metaError.value = '请完整填写标题与时间'
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
        maxTeamMembers: maxTeamMembers.value,
        maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
      },
    })
    if (error) throw error
    toast.success('基础信息已保存')
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
    toast.success('模式配置已保存')
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
        <CardTitle>基础信息</CardTitle>
        <CardDescription>标题、时间与队伍限制;游戏模式创建后不可修改</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="saveMeta">
          <FieldGroup>
            <Alert v-if="metaError" variant="destructive">
              <AlertDescription>{{ metaError }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="c-title">标题</FieldLabel>
              <Input id="c-title" v-model="title" :readonly="!canWrite" required />
            </Field>
            <Field>
              <FieldLabel for="c-desc">描述</FieldLabel>
              <Textarea id="c-desc" v-model="description" :readonly="!canWrite" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-start">开始时间</FieldLabel>
                <Input id="c-start" v-model="startTime" type="datetime-local" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-end">结束时间</FieldLabel>
                <Input id="c-end" v-model="endTime" type="datetime-local" :readonly="!canWrite" required />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="c-max-members">每队最大人数</FieldLabel>
                <Input id="c-max-members" v-model.number="maxTeamMembers" type="number" min="1" :readonly="!canWrite" required />
              </Field>
              <Field>
                <FieldLabel for="c-max-runtime">每队并发运行时上限</FieldLabel>
                <Input id="c-max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" type="number" min="1" :readonly="!canWrite" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="c-auto-approve" v-model:checked="teamRegistrationAutoApprove" :disabled="!canWrite" />
              <FieldLabel for="c-auto-approve" class="font-normal">队伍注册自动通过</FieldLabel>
            </Field>
            <Field v-if="canWrite">
              <Button type="submit" :disabled="savingMeta">
                <Spinner v-if="savingMeta" data-icon="inline-start" />
                保存基础信息
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>模式配置(JSON)</CardTitle>
        <CardDescription>
          {{ enumLabel(GameModeLabel, config?.mode) }} 模式的专属配置;保存时携带修订版本进行乐观并发校验
        </CardDescription>
      </CardHeader>
      <CardContent>
        <JsonConfigEditor
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
