<script setup lang="ts">
import { toast } from 'vue-sonner'
import { adminChallengeBankCreateTemplate } from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
} from '~/api'
import { challengeTemplateWriteErrorMessages } from '~/lib/challenge-template-error'
import { validateChallengeTemplateDraft } from '~/lib/challenge-template-validation'
import { defaultDefinitionJson, normalizeDefinitionJson } from '~/utils/game-config'

definePageMeta({ middleware: 'auth' })

const { canOrganize } = useAuth()

const title = ref('')
const mode = ref<NoCtfapiEndpointsCompetitionsGameModeProtocol>('Ctf')
const visibility = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol>('Private')
const direction = ref('')
const description = ref('')
const definitionJson = ref(defaultDefinitionJson(mode.value))
const saveErrors = ref<string[]>([])
const saveAttempted = ref(false)
const pending = ref(false)

const titleInvalid = computed(() => saveAttempted.value
  && (!title.value.trim() || title.value.trim().length > 160))
const directionInvalid = computed(() => saveAttempted.value
  && (!direction.value.trim() || direction.value.trim().length > 96))

function changeMode(value: unknown): void {
  if (value !== 'Ctf' && value !== 'Awd' && value !== 'Awdp' && value !== 'Koh') return
  definitionJson.value = defaultDefinitionJson(value)
  mode.value = value
}

async function submit(): Promise<void> {
  saveAttempted.value = true
  saveErrors.value = []
  const normalizedDefinition = normalizeDefinitionJson(mode.value, definitionJson.value)
  if (!normalizedDefinition) {
    saveErrors.value = [translate('题目定义无法解析，请重置或修正后再保存')]
    toast.error(saveErrors.value[0] ?? translate('无法保存题目模板'))
    return
  }
  const validationErrors = validateChallengeTemplateDraft({
    mode: mode.value,
    title: title.value,
    direction: direction.value,
    definitionJson: normalizedDefinition,
  })
  if (validationErrors.length > 0) {
    saveErrors.value = validationErrors
    toast.error(validationErrors[0] ?? translate('无法保存题目模板'))
    return
  }
  pending.value = true
  const { data, error: apiError } = await adminChallengeBankCreateTemplate({
    body: {
      title: title.value.trim(),
      mode: mode.value,
      visibility: visibility.value,
      direction: directionLabel(direction.value),
      description: description.value.trim() || null,
      definitionJson: normalizedDefinition,
    },
  })
  pending.value = false
  if (apiError || !data) {
    saveErrors.value = challengeTemplateWriteErrorMessages(apiError)
    toast.error(saveErrors.value[0] ?? translate('无法保存题目模板'))
    return
  }
  toast.success(translate("模板已创建"))
  await navigateTo(`/admin/challenges/${data.id}`)
}
</script>

<template>
  <div class="mx-auto flex max-w-3xl flex-col gap-6 px-4 py-8">
    <div>
      <h1 class="text-2xl font-semibold">{{ $t('新建题目模板') }}</h1>
      <p class="text-sm text-muted-foreground">{{ $t('创建全局题库模板,之后可实例化到竞赛中') }}</p>
    </div>

    <Alert v-if="!canOrganize" variant="destructive">
      <AlertDescription>{{ $t('需要组织者或管理员权限才能创建题目模板。') }}</AlertDescription>
    </Alert>

    <Card v-else>
      <CardContent class="pt-6">
        <form novalidate @submit.prevent="submit">
          <Alert v-if="saveErrors.length" variant="destructive" class="mb-6">
            <AlertTitle>{{ $t('无法保存题目模板') }}</AlertTitle>
            <AlertDescription class="flex flex-col gap-2">
              <span>{{ $t('请修正以下问题后重试：') }}</span>
              <ul class="list-disc pl-5">
                <li v-for="message in saveErrors" :key="message">{{ message }}</li>
              </ul>
            </AlertDescription>
          </Alert>
          <Tabs default-value="basic">
            <TabsList>
              <TabsTrigger value="basic">{{ $t('基本信息') }}</TabsTrigger>
              <TabsTrigger value="definition">{{ $t('题目定义') }}</TabsTrigger>
            </TabsList>
            <TabsContent value="basic">
              <FieldGroup>
                <Field :data-invalid="titleInvalid || undefined">
                  <FieldLabel for="title">{{ $t('标题') }}</FieldLabel>
                  <Input
                    id="title"
                    v-model="title"
                    required
                    maxlength="160"
                    :aria-invalid="titleInvalid || undefined"
                    :placeholder="$t('例如:Web 入门 - SQL 注入')"
                  />
                </Field>
                <div class="grid gap-4 sm:grid-cols-2">
                  <Field>
                    <FieldLabel for="mode">{{ $t('游戏模式') }}</FieldLabel>
                    <Select :model-value="mode" @update:model-value="changeMode">
                      <SelectTrigger id="mode" class="w-full">
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
                  </Field>
                  <Field>
                    <FieldLabel for="visibility">{{ $t('可见性') }}</FieldLabel>
                    <Select v-model="visibility">
                      <SelectTrigger id="visibility" class="w-full">
                        <SelectValue :placeholder="$t('选择可见性')" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="Private">{{ $t('私有(仅负责人/管理员可用)') }}</SelectItem>
                          <SelectItem value="Shared">{{ $t('共享(所有组织者可用)') }}</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                  </Field>
                </div>
                <Field :data-invalid="directionInvalid || undefined">
                  <FieldLabel for="direction">{{ $t('方向') }}</FieldLabel>
                  <Input
                    id="direction"
                    v-model="direction"
                    @blur="direction = directionLabel(direction)"
                    required
                    maxlength="96"
                    :aria-invalid="directionInvalid || undefined"
                    :placeholder="$t('例如:Web / Pwn / Misc')"
                  />
                </Field>
                <Field>
                  <FieldLabel for="description">{{ $t('题面') }}</FieldLabel>
                  <Textarea id="description" v-model="description" rows="6" :placeholder="$t('题目描述,支持 Markdown')" />
                </Field>
                <Field orientation="horizontal">
                  <Button type="submit" :disabled="pending">
                    <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('创建模板') }} </Button>
                  <Button type="button" variant="outline" as-child>
                    <NuxtLink to="/admin/challenges">{{ $t('取消') }}</NuxtLink>
                  </Button>
                </Field>
              </FieldGroup>
            </TabsContent>
            <TabsContent value="definition">
              <FieldGroup>
                <Alert>
                  <AlertDescription>{{ $t('可选,创建后可再配置。') }}</AlertDescription>
                </Alert>
                <DefinitionEditor v-model="definitionJson" :mode="mode" />
              </FieldGroup>
            </TabsContent>
          </Tabs>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
