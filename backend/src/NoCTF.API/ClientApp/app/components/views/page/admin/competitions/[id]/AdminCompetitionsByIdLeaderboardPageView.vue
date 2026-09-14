<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdLeaderboardPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdLeaderboardPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdLeaderboardPageViewState }>()
const { canWrite, current, loading, error, frozenStartAt, hiddenStartAt, reason, saving, save } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-48 w-full" />
    <template v-else-if="current">
      <Card class="gap-0">
        <section id="competition-leaderboard-status" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
        <CardHeader>
          <CardTitle>{{ $t('ui.currentStatus') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-col gap-2 text-sm">
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('ui.actualEffective') }}</span>
            <Badge>{{ enumLabel(LeaderboardVisibilityLabel, current.effectiveVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('ui.freezeStartsAt') }}</span>
            <span class="font-mono tabular-nums">{{ current.frozenStartAt ? adminFormatDateTime(current.frozenStartAt) : $t('ui.notSet') }}</span>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('ui.blackoutStartsAt') }}</span>
            <span class="font-mono tabular-nums">{{ current.hiddenStartAt ? adminFormatDateTime(current.hiddenStartAt) : $t('ui.notSet') }}</span>
          </div>
        </CardContent>
        </section>

        <Separator />
        <section id="competition-leaderboard-visibility" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
        <CardHeader>
          <CardTitle>{{ $t('ui.modifyVisibility') }}</CardTitle>
          <CardDescription>{{ $t('ui.freezeRetainsASnapshotOfTheLastStandingsMaskCompletely') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <UiForm @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="frozen-start">{{ $t('ui.freezeStartTimeOptional') }}</FieldLabel>
                <DateTimePicker id="frozen-start" v-model="frozenStartAt"  class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="hidden-start">{{ $t('ui.blackoutStartTimeOptional') }}</FieldLabel>
                <DateTimePicker id="hidden-start" v-model="hiddenStartAt"  class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="vis-reason">{{ $t('ui.reasonOptionalRecordedInAudit') }}</FieldLabel>
                <Input id="vis-reason" v-model="reason" class="max-w-sm" :readonly="!canWrite" :placeholder="$t('ui.exampleFreezeTheLast30MinutesOfTheGame')" />
              </Field>
              <Field v-if="canWrite">
                <Button type="submit" :disabled="saving" class="w-fit">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.save') }} </Button>
              </Field>
            </FieldGroup>
          </UiForm>
        </CardContent>
        </section>
      </Card>
    </template>
  </div>
</template>
