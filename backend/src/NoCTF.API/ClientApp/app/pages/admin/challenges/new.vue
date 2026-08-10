<script setup lang="ts">
import { toast } from 'vue-sonner'
import { adminChallengeBankCreateTemplate } from '~/api'
import type {
  NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol,
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
} from '~/api'

definePageMeta({ middleware: 'auth' })

const { canOrganize } = useAuth()

const title = ref('')
const mode = ref<NoCtfapiEndpointsCompetitionsGameModeProtocol>('Ctf')
const visibility = ref<NoCtfapiEndpointsAdministrationChallengeBankChallengeVisibilityProtocol>('Private')
const direction = ref('')
const description = ref('')
const definitionJson = ref('{}')
const error = ref<string | null>(null)
const pending = ref(false)

async function submit(): Promise<void> {
  error.value = null
  if (!title.value.trim() || !direction.value.trim()) {
    error.value = translate('请填写标题和方向')
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
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ error }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="title">{{ $t('标题') }}</FieldLabel>
              <Input id="title" v-model="title" required maxlength="200" :placeholder="$t('例如:Web 入门 - SQL 注入')" />
            </Field>
            <div class="grid gap-4 sm:grid-cols-2">
              <Field>
                <FieldLabel for="mode">{{ $t('游戏模式') }}</FieldLabel>
                <Select v-model="mode">
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
            <Field>
              <FieldLabel for="direction">{{ $t('方向') }}</FieldLabel>
              <Input id="direction" v-model="direction" required maxlength="100" :placeholder="$t('例如:Web / Pwn / Misc')" />
            </Field>
            <Field>
              <FieldLabel for="description">{{ $t('题面') }}</FieldLabel>
              <Textarea id="description" v-model="description" rows="6" :placeholder="$t('题目描述,支持 Markdown')" />
            </Field>
            <Field>
              <FieldLabel>{{ $t('题目定义') }}</FieldLabel>
              <DefinitionEditor v-model="definitionJson" :mode="mode" />
            </Field>
            <Field orientation="horizontal">
              <Button type="submit" :disabled="pending">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('创建模板') }} </Button>
              <Button type="button" variant="outline" as-child>
                <NuxtLink to="/admin/challenges">{{ $t('取消') }}</NuxtLink>
              </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
