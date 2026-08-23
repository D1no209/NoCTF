<script setup lang="ts">
import { toast } from 'vue-sonner'
import {
  adminExportCompetitionArchive,
  adminExportCompetitionEvents,
} from '~/api'
import { useCompetitionAdmin } from '~/lib/admin-competition'
import { downloadSdkFile } from '~/utils/download'
import { userFacingErrorMessage } from '~/utils/api-error'

definePageMeta({ middleware: 'auth' })

const { competitionId, canWrite } = useCompetitionAdmin()

// ---- Events JSONL export ----
const eventsFrom = ref('')
const eventsTo = ref('')
const exportingEvents = ref(false)

async function exportEvents() {
  const from = localInputToIso(eventsFrom.value)
  const to = localInputToIso(eventsTo.value)
  if (!from || !to) {
    toast.error(translate("请选择导出时间范围"))
    return
  }
  exportingEvents.value = true
  try {
    await downloadSdkFile(
      adminExportCompetitionEvents({
        path: { competitionId },
        query: { from, to },
        parseAs: 'blob',
      }),
      `competition-${competitionId}-events.jsonl`,
    )
    toast.success(translate("事件导出已开始下载"))
  }
  catch (e) {
    toast.error(userFacingErrorMessage(e instanceof Error ? e.message : null, translate("导出失败")))
  }
  finally {
    exportingEvents.value = false
  }
}

const includeProtectedFlags = ref(false)
const exportReason = ref('')
const exportingArchive = ref(false)

async function exportArchive() {
  if (exportingArchive.value) return
  const reason = exportReason.value.trim()
  if (includeProtectedFlags.value && (reason.length < 8 || reason.length > 512)) {
    toast.error(translate('包含受保护 Flag 时，请填写 8–512 个字符的导出原因'))
    return
  }
  exportingArchive.value = true
  try {
    await downloadSdkFile(
      adminExportCompetitionArchive({
        path: { competitionId },
        body: {
          includeProtectedFlags: includeProtectedFlags.value,
          reason: reason || null,
        },
        parseAs: 'blob',
      }),
      `competition-${competitionId}-archive.zip`,
    )
    toast.success(translate('竞赛归档已开始下载'))
    exportReason.value = ''
  }
  catch (e) {
    toast.error(parseApiError(e).message)
  }
  finally {
    exportingArchive.value = false
  }
}
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('事件导出(JSONL)') }}</CardTitle>
        <CardDescription>{{ $t('按时间范围导出竞赛事件流,每行一个 JSON 事件') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap items-end gap-3">
        <Field>
          <FieldLabel for="ev-from">{{ $t('起始时间') }}</FieldLabel>
          <Input id="ev-from" v-model="eventsFrom" type="datetime-local" />
        </Field>
        <Field>
          <FieldLabel for="ev-to">{{ $t('结束时间') }}</FieldLabel>
          <Input id="ev-to" v-model="eventsTo" type="datetime-local" />
        </Field>
        <Button :disabled="exportingEvents" @click="exportEvents">
          <Spinner v-if="exportingEvents" data-icon="inline-start" /> {{ $t('导出事件') }} </Button>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ $t('竞赛归档') }}</CardTitle>
        <CardDescription>{{ $t('同步生成当前竞赛的数据归档并立即下载。') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <div v-if="canWrite" class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="ex-reason">{{ includeProtectedFlags ? $t('导出原因') : $t('导出原因(可选)') }}</FieldLabel>
            <Input id="ex-reason" v-model="exportReason" class="w-72" :placeholder="$t('记入审计')" />
            <FieldDescription v-if="includeProtectedFlags">{{ $t('包含受保护 Flag 时需填写 8–512 个字符。') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="ex-flags" v-model="includeProtectedFlags" />
            <FieldLabel for="ex-flags" class="font-normal">{{ $t('包含受保护的 Flag') }}</FieldLabel>
          </Field>
          <Button :disabled="exportingArchive" @click="exportArchive">
            <Spinner v-if="exportingArchive" data-icon="inline-start" /> {{ $t('下载竞赛归档') }} </Button>
        </div>
      </CardContent>
    </Card>
  </div>
</template>
