<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminGetCompetitionLeaderboardVisibility,
  adminUpdateCompetitionLeaderboardVisibility,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

const current = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const frozenStartAt = ref('')
const hiddenStartAt = ref('')
const reason = ref('')
const saving = ref(false)

async function load() {
  loading.value = true
  error.value = null
  const { data, error: e } = await adminGetCompetitionLeaderboardVisibility({ path: { competitionId } })
  if (e) error.value = parseApiError(e).message
  else {
    current.value = data ?? null
    frozenStartAt.value = isoToLocalInput(data?.frozenStartAt)
    hiddenStartAt.value = isoToLocalInput(data?.hiddenStartAt)
  }
  loading.value = false
}

async function save() {
  if (!current.value) return
  saving.value = true
  try {
    const { data, error } = await adminUpdateCompetitionLeaderboardVisibility({
      path: { competitionId },
      body: {
        frozenStartAt: localInputToIso(frozenStartAt.value) ?? null,
        hiddenStartAt: localInputToIso(hiddenStartAt.value) ?? null,
        reason: reason.value.trim() || null,
      },
    })
    if (error) throw error
    current.value = data ?? current.value
    frozenStartAt.value = isoToLocalInput(current.value.frozenStartAt)
    hiddenStartAt.value = isoToLocalInput(current.value.hiddenStartAt)
    reason.value = ''
    toast.success(translate("记分板可见性已更新"))
  }
  catch (e) {
    toastWriteError(e)
  }
  finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-48 w-full" />
    <template v-else-if="current">
      <Card>
        <CardHeader>
          <CardTitle>{{ $t('当前状态') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-col gap-2 text-sm">
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('实际生效:') }}</span>
            <Badge>{{ enumLabel(LeaderboardVisibilityLabel, current.effectiveVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('冻结开始时间:') }}</span>
            <span class="font-mono tabular-nums">{{ current.frozenStartAt ? adminFormatDateTime(current.frozenStartAt) : $t('未设置') }}</span>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('遮蔽开始时间:') }}</span>
            <span class="font-mono tabular-nums">{{ current.hiddenStartAt ? adminFormatDateTime(current.hiddenStartAt) : $t('未设置') }}</span>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>{{ $t('修改可见性') }}</CardTitle>
          <CardDescription>{{ $t('冻结保留最后排名快照;遮蔽对选手完全隐藏记分板。可设置定时生效。') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <form @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="frozen-start">{{ $t('冻结开始时间(可选)') }}</FieldLabel>
                <Input id="frozen-start" v-model="frozenStartAt" type="datetime-local" class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="hidden-start">{{ $t('遮蔽开始时间(可选)') }}</FieldLabel>
                <Input id="hidden-start" v-model="hiddenStartAt" type="datetime-local" class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="vis-reason">{{ $t('原因(可选,记入审计)') }}</FieldLabel>
                <Input id="vis-reason" v-model="reason" class="max-w-sm" :readonly="!canWrite" :placeholder="$t('例如:比赛最后 30 分钟冻结')" />
              </Field>
              <Field v-if="canWrite">
                <Button type="submit" :disabled="saving" class="w-fit">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('保存') }} </Button>
              </Field>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
