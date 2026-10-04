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
          <CardTitle>{{ $t('leaderboard.label.status') }}</CardTitle>
        </CardHeader>
        <CardContent class="flex flex-col gap-2 text-sm">
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('leaderboard.label.actualEffective') }}</span>
            <Badge>{{ enumLabel(LeaderboardVisibilityLabel, current.effectiveVisibility) }}</Badge>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('leaderboard.label.freezeStarts') }}</span>
            <span class="font-mono tabular-nums">{{ current.frozenStartAt ? adminFormatDateTime(current.frozenStartAt) : $t('leaderboard.label.set') }}</span>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-muted-foreground">{{ $t('leaderboard.label.blackoutStarts') }}</span>
            <span class="font-mono tabular-nums">{{ current.hiddenStartAt ? adminFormatDateTime(current.hiddenStartAt) : $t('leaderboard.label.set') }}</span>
          </div>
        </CardContent>
        </section>

        <Separator />
        <section id="competition-leaderboard-visibility" class="flex flex-col gap-4 py-4 first:pt-0 last:pb-0">
        <CardHeader>
          <CardTitle>{{ $t('leaderboard.label.modifyVisibility') }}</CardTitle>
          <CardDescription>{{ $t('leaderboard.competitionsBy.description.freezeRetainsSnapshotLast') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <UiForm @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="frozen-start">{{ $t('leaderboard.label.freezeStartTimeOptional') }}</FieldLabel>
                <DateTimePicker id="frozen-start" v-model="frozenStartAt"  class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="hidden-start">{{ $t('leaderboard.label.blackoutStartTimeOptional') }}</FieldLabel>
                <DateTimePicker id="hidden-start" v-model="hiddenStartAt"  class="max-w-sm" :readonly="!canWrite" />
              </Field>
              <Field>
                <FieldLabel for="vis-reason">{{ $t('leaderboard.competitionsBy.label.reasonOptionalRecordedAudit') }}</FieldLabel>
                <Input id="vis-reason" v-model="reason" class="max-w-sm" :readonly="!canWrite" :placeholder="$t('leaderboard.competitionsBy.description.exampleFreezeLastMinutes')" />
              </Field>
              <Field v-if="canWrite">
                <Button type="submit" :disabled="saving" class="w-fit">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('common.action.save') }} </Button>
              </Field>
            </FieldGroup>
          </UiForm>
        </CardContent>
        </section>
      </Card>
    </template>
  </div>
</template>
