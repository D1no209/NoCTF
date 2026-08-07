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
const maxTeamMembers = ref(5)
const maxConcurrentRuntimeInstancesPerTeam = ref(1)
const error = ref<string | null>(null)
const pending = ref(false)

async function submit() {
  error.value = null
  if (!title.value.trim()) {
    error.value = '请输入竞赛标题'
    return
  }
  const start = localInputToIso(startTime.value)
  const end = localInputToIso(endTime.value)
  if (!start || !end) {
    error.value = '请选择开始和结束时间'
    return
  }
  if (new Date(start) >= new Date(end)) {
    error.value = '开始时间必须早于结束时间'
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
        maxTeamMembers: maxTeamMembers.value,
        maxConcurrentRuntimeInstancesPerTeam: maxConcurrentRuntimeInstancesPerTeam.value,
      },
    })
    if (e || !data) throw e
    toast.success('竞赛已创建')
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
      <AlertDescription>当前账户没有创建竞赛的权限</AlertDescription>
    </Alert>
    <Card>
      <CardHeader>
        <CardTitle>新建竞赛</CardTitle>
        <CardDescription>创建后游戏模式不可修改,其余信息可稍后调整</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="title">标题</FieldLabel>
              <Input id="title" v-model="title" required maxlength="200" />
            </Field>
            <Field>
              <FieldLabel for="description">描述</FieldLabel>
              <Textarea id="description" v-model="description" placeholder="可选" />
            </Field>
            <Field>
              <FieldLabel for="mode">游戏模式</FieldLabel>
              <Select id="mode" v-model="mode">
                <SelectTrigger class="w-full">
                  <SelectValue placeholder="选择模式" />
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
              <FieldDescription>创建后不可修改</FieldDescription>
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="start">开始时间</FieldLabel>
                <Input id="start" v-model="startTime" type="datetime-local" required />
              </Field>
              <Field>
                <FieldLabel for="end">结束时间</FieldLabel>
                <Input id="end" v-model="endTime" type="datetime-local" required />
              </Field>
            </div>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="max-members">每队最大人数</FieldLabel>
                <Input id="max-members" v-model.number="maxTeamMembers" type="number" min="1" required />
              </Field>
              <Field>
                <FieldLabel for="max-runtime">每队并发运行时上限</FieldLabel>
                <Input id="max-runtime" v-model.number="maxConcurrentRuntimeInstancesPerTeam" type="number" min="1" required />
              </Field>
            </div>
            <Field orientation="horizontal">
              <Checkbox id="auto-approve" v-model="teamRegistrationAutoApprove" />
              <FieldLabel for="auto-approve" class="font-normal">队伍注册自动通过(无需审批)</FieldLabel>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending || !canOrganize" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" />
                创建竞赛
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
