<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdExportsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdExportsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdExportsPageViewState }>()
const { canWrite, eventsFrom, eventsTo, exportingEvents, exportEvents, includeProtectedFlags, exportReason, exportingArchive, exportArchive } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.eventExportJsonl') }}</CardTitle>
        <CardDescription>{{ $t('ui.exportTheCompetitionEventStreamByTimeRangeOneJson') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap items-end gap-3">
        <Field>
          <FieldLabel for="ev-from">{{ $t('ui.startTime2') }}</FieldLabel>
          <DateTimePicker id="ev-from" v-model="eventsFrom"  />
        </Field>
        <Field>
          <FieldLabel for="ev-to">{{ $t('ui.endTime') }}</FieldLabel>
          <DateTimePicker id="ev-to" v-model="eventsTo"  />
        </Field>
        <Button :disabled="exportingEvents" @click="exportEvents">
          <Spinner v-if="exportingEvents" data-icon="inline-start" /> {{ $t('ui.exportEvents') }} </Button>
      </CardContent>
    </Card>

    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.competitionArchive') }}</CardTitle>
        <CardDescription>{{ $t('ui.generateThisCompetitionSDataArchiveSynchronouslyAndDownloadIt') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <div v-if="canWrite" class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="ex-reason">{{ includeProtectedFlags ? $t('ui.exportReason') : $t('ui.exportReasonOptional') }}</FieldLabel>
            <Input id="ex-reason" v-model="exportReason" class="w-72" :placeholder="$t('ui.creditedToAudit')" />
            <FieldDescription v-if="includeProtectedFlags">{{ $t('ui.an8512CharacterReasonIsRequiredWhenProtectedFlags') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="ex-flags" v-model="includeProtectedFlags" />
            <FieldLabel for="ex-flags" class="font-normal">{{ $t('ui.containsProtectedFlag') }}</FieldLabel>
          </Field>
          <Button :disabled="exportingArchive" @click="exportArchive">
            <Spinner v-if="exportingArchive" data-icon="inline-start" /> {{ $t('ui.downloadCompetitionArchive') }} </Button>
        </div>
      </CardContent>
    </Card>
  </div>
</template>
