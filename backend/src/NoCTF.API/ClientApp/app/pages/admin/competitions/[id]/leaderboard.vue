<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminGetCompetitionLeaderboardVisibility,
  adminUpdateCompetitionLeaderboardVisibility,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse,
  NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

const current = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const visibility = ref<NoCtfapiEndpointsCompetitionsLeaderboardVisibilityProtocol>('Normal')
const startsAt = ref('')
const reason = ref('')
const saving = ref(false)

async function load() {
  loading.value = true
  error.value = null
  const { data, error: e } = await adminGetCompetitionLeaderboardVisibility({ path: { competitionId } })
  if (e) error.value = parseApiError(e).message
  else {
    current.value = data ?? null
    visibility.value = data?.configuredVisibility ?? 'Normal'
    startsAt.value = isoToLocalInput(data?.startsAt)
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
        visibility: visibility.value,
        startsAt: localInputToIso(startsAt.value) ?? null,
        expectedRevision: current.value.revision ?? 0,
        reason: reason.value.trim() || null,
      },
    })
    if (error) throw error
    current.value = data ?? current.value
    reason.value = ''
    toast.success(translate("记分板可见性已更新"))
  }
  catch (e) {
    toastWriteError(e, load)
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
            <span class="text-muted-foreground">{{ $t('配置的可见性:') }}</span>
            <Badge variant="secondary">{{ enumLabel(LeaderboardVisibilityLabel, current.configuredVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('实际生效:') }}</span>
            <Badge>{{ enumLabel(LeaderboardVisibilityLabel, current.effectiveVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('生效时间:') }}</span>
            <span class="font-mono tabular-nums">{{ current.startsAt ? adminFormatDateTime(current.startsAt) : $t('立即') }}</span>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('上次应用:') }}</span>
            <span class="font-mono tabular-nums">{{ adminFormatDateTime(current.appliedAt) }}</span>
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
                <FieldLabel for="vis">{{ $t('可见性') }}</FieldLabel>
                <Select id="vis" v-model="visibility" :disabled="!canWrite">
                  <SelectTrigger class="w-full max-w-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="Normal">{{ $t('正常') }}</SelectItem>
                      <SelectItem value="Frozen">{{ $t('冻结') }}</SelectItem>
                      <SelectItem value="Blackout">{{ $t('遮蔽') }}</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="vis-start">{{ $t('生效时间(可选,留空立即生效)') }}</FieldLabel>
                <Input id="vis-start" v-model="startsAt" type="datetime-local" class="max-w-sm" :readonly="!canWrite" />
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
