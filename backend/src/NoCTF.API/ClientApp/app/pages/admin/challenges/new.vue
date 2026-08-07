<script setup lang="ts">
import { toast } from 'vue-sonner'
import { adminChallengeBankCreateTemplate } from '~/api'
import type {
  NoCtfDomainChallengesChallengeVisibility2,
  NoCtfDomainCompetitionsGameMode2,
} from '~/api'

definePageMeta({ middleware: 'auth' })

const { canOrganize } = useAuth()

const title = ref('')
const mode = ref<NoCtfDomainCompetitionsGameMode2>('Ctf')
const visibility = ref<NoCtfDomainChallengesChallengeVisibility2>('Private')
const direction = ref('')
const description = ref('')
const definitionJson = ref('{}')
const error = ref<string | null>(null)
const pending = ref(false)

const definitionJsonError = computed(() => {
  try {
    JSON.parse(definitionJson.value)
    return null
  }
  catch {
    return '定义 JSON 格式错误,请检查后再提交'
  }
})

async function submit(): Promise<void> {
  error.value = null
  if (!title.value.trim() || !direction.value.trim()) {
    error.value = '请填写标题和方向'
    return
  }
  if (definitionJsonError.value) {
    error.value = definitionJsonError.value
    return
  }
  pending.value = true
  const { data, error: apiError } = await adminChallengeBankCreateTemplate({
    body: {
      title: title.value.trim(),
      mode: mode.value,
      visibility: visibility.value,
      direction: direction.value.trim(),
      description: description.value.trim() || null,
      definitionJson: definitionJson.value,
    },
  })
  pending.value = false
  if (apiError || !data) {
    error.value = parseApiError(apiError).message
    return
  }
  toast.success('模板已创建')
  await navigateTo(`/admin/challenges/${data.id}`)
}
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-8">
    <div>
      <h1 class="text-2xl font-semibold">新建题目模板</h1>
      <p class="text-sm text-muted-foreground">创建全局题库模板,之后可实例化到竞赛中</p>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>需要组织者或管理员权限才能创建题目模板。</AlertDescription>
    </Alert>

    <Card v-else>
      <CardContent class="pt-6">
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="title">标题</FieldLabel>
              <Input id="title" v-model="title" required maxlength="200" placeholder="例如:Web 入门 - SQL 注入" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="mode">游戏模式</FieldLabel>
                <Select v-model="mode">
                  <SelectTrigger id="mode" class="w-full">
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
              </Field>
              <Field>
                <FieldLabel for="visibility">可见性</FieldLabel>
                <Select v-model="visibility">
                  <SelectTrigger id="visibility" class="w-full">
                    <SelectValue placeholder="选择可见性" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="Private">私有(仅负责人/管理员可用)</SelectItem>
                      <SelectItem value="Shared">共享(所有组织者可用)</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </Field>
            </div>
            <Field>
              <FieldLabel for="direction">方向</FieldLabel>
              <Input id="direction" v-model="direction" required maxlength="100" placeholder="例如:Web / Pwn / Misc" />
            </Field>
            <Field>
              <FieldLabel for="description">题面</FieldLabel>
              <Textarea id="description" v-model="description" rows="6" placeholder="题目描述,支持 Markdown" />
            </Field>
            <Field :data-invalid="!!definitionJsonError || undefined">
              <FieldLabel for="definition-json">定义 JSON</FieldLabel>
              <Textarea
                id="definition-json"
                v-model="definitionJson"
                rows="8"
                class="font-mono text-sm"
                :aria-invalid="!!definitionJsonError || undefined"
                placeholder="Runtime / Checker / Flag 注入定义"
              />
              <FieldDescription>Provider 中立的 Runtime/Checker/Flag 注入定义,必须是合法 JSON。</FieldDescription>
              <FieldError v-if="definitionJsonError">{{ definitionJsonError }}</FieldError>
            </Field>
            <Field orientation="horizontal">
              <Button type="submit" :disabled="pending">
                <Spinner v-if="pending" data-icon="inline-start" />
                创建模板
              </Button>
              <Button type="button" variant="outline" as-child>
                <NuxtLink to="/admin/challenges">取消</NuxtLink>
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
