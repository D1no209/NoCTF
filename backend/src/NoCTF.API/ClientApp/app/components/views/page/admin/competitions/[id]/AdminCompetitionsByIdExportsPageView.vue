<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdExportsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdExportsPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdExportsPageViewState }>()
const { canWrite, eventsFrom, eventsTo, exportingEvents, exportEvents, includeProtectedFlags, exportReason, exportingArchive, exportArchive } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Card class="gap-0">
      <section id="competition-event-export" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('administration.label.eventExportJsonl') }}</CardTitle>
        <CardDescription>{{ $t('administration.competitionsBy.description.exportCompetitionEventStream') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-wrap items-end gap-3">
        <Field>
          <FieldLabel for="ev-from">{{ $t('administration.label.startTime') }}</FieldLabel>
          <DateTimePicker id="ev-from" v-model="eventsFrom"  />
        </Field>
        <Field>
          <FieldLabel for="ev-to">{{ $t('common.label.endTime') }}</FieldLabel>
          <DateTimePicker id="ev-to" v-model="eventsTo"  />
        </Field>
        <Button :disabled="exportingEvents" @click="exportEvents">
          <Spinner v-if="exportingEvents" data-icon="inline-start" /> {{ $t('administration.label.exportEvents') }} </Button>
      </CardContent>
      </section>

      <Separator />
      <section id="competition-archive-export" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
      <CardHeader>
        <CardTitle>{{ $t('administration.label.competitionArchive') }}</CardTitle>
        <CardDescription>{{ $t('administration.competitionsBy.description.generateCompetitionSData') }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-4">
        <div v-if="canWrite" class="flex flex-wrap items-end gap-3">
          <Field>
            <FieldLabel for="ex-reason">{{ includeProtectedFlags ? $t('administration.label.exportReason') : $t('administration.label.exportReasonOptional') }}</FieldLabel>
            <Input id="ex-reason" v-model="exportReason" class="w-72" :placeholder="$t('administration.label.creditedAudit')" />
            <FieldDescription v-if="includeProtectedFlags">{{ $t('administration.competitionsBy.validation.characterReasonRequired') }}</FieldDescription>
          </Field>
          <Field orientation="horizontal">
            <Checkbox id="ex-flags" v-model="includeProtectedFlags" />
            <FieldLabel for="ex-flags" class="font-normal">{{ $t('administration.label.protectedFlag') }}</FieldLabel>
          </Field>
          <Button :disabled="exportingArchive" @click="exportArchive">
            <Spinner v-if="exportingArchive" data-icon="inline-start" /> {{ $t('administration.label.downloadCompetitionArchive') }} </Button>
        </div>
      </CardContent>
      </section>
    </Card>
  </div>
</template>
