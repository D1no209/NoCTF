<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminGetCompetitionLeaderboardVisibility,
  adminUpdateCompetitionLeaderboardVisibility,
} from '~/api'
import type {
  NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse,
  NoCtfDomainCompetitionsCompetitionLeaderboardVisibility,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

const current = ref<NoCtfapiEndpointsAdministrationCompetitionsCompetitionLeaderboardVisibilityResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const visibility = ref('0')
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
    visibility.value = String(data?.configuredVisibility ?? 0)
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
        visibility: Number(visibility.value) as NoCtfDomainCompetitionsCompetitionLeaderboardVisibility,
        startsAt: localInputToIso(startsAt.value) ?? null,
        expectedRevision: current.value.revision ?? 0,
        reason: reason.value.trim() || null,
      },
    })
    if (error) throw error
    current.value = data ?? current.value
    reason.value = ''
    toast.success('记分板可见性已更新')
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
          <CardTitle>当前状态</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-col gap-2 text-sm">
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">配置的可见性:</span>
            <Badge variant="secondary">{{ enumLabel(LeaderboardVisibilityLabel, current.configuredVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">实际生效:</span>
            <Badge>{{ enumLabel(LeaderboardVisibilityLabel, current.effectiveVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">生效时间:</span>
            <span>{{ current.startsAt ? adminFormatDateTime(current.startsAt) : '立即' }}</span>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">上次应用:</span>
            <span>{{ adminFormatDateTime(current.appliedAt) }}</span>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>修改可见性</CardTitle>
          <CardDescription>冻结保留最后排名快照;遮蔽对选手完全隐藏记分板。可设置定时生效。</CardDescription>
        </CardHeader>
        <CardContent>
          <form @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="vis">可见性</FieldLabel>
                <Select id="vis" v-model="visibility" :disabled="!canWrite">
                  <SelectTrigger class="w-full max-w-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="0">正常</SelectItem>
                      <SelectItem value="1">冻结</SelectItem>
                      <SelectItem value="2">遮蔽</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </Field>
              <Field>
                <FieldLabel for="vis-start">生效时间(可选,留空立即生效)</FieldLabel>
                <Input id="vis-start" v-model="startsAt" type="datetime-local" class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="vis-reason">原因(可选,记入审计)</FieldLabel>
                <Input id="vis-reason" v-model="reason" class="max-w-sm" :readonly="!canWrite" placeholder="例如:比赛最后 30 分钟冻结" />
              </Field>
              <Field v-if="canWrite">
                <Button type="submit" :disabled="saving" class="w-fit">
                  <Spinner v-if="saving" data-icon="inline-start" />
                  保存
                </Button>
              </Field>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
