<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeTimingSettingsState } from '~/features/challenges/timing/useChallengeTimingSettings'
const props=defineProps<{state:ChallengeTimingSettingsState}>()
const {loading,pending,error,supported,opening,scoringEnd,submissionEnd,current,zone,valid,dirty,preview,confirmation,
  canWrite,save,apply,setConfirmation,clearOpening,clearScoring,clearSubmission}=toRefs(props.state)
</script>
<template>
  <section v-if="supported" class="flex min-w-0 flex-col gap-4">
    <h3 class="text-base font-medium">{{ $t('challengeTiming.title') }}</h3>
    <Skeleton v-if="loading" class="h-40 w-full" />
    <UiForm v-else validation="feature" class="flex flex-col gap-4" @submit.prevent="save">
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <FieldDescription>{{ $t('challengeTiming.zone',{zone}) }}</FieldDescription>
      <FieldGroup class="grid gap-4 xl:grid-cols-3">
        <Field><FieldLabel for="challenge-auto-open">{{ $t('challengeTiming.opening') }}</FieldLabel><DateTimePicker id="challenge-auto-open" v-model="opening" :disabled="pending||!canWrite" /><Button v-if="opening&&canWrite" type="button" variant="ghost" size="sm" :disabled="pending" @click="clearOpening">{{ $t('common.label.clear') }}</Button><FieldDescription>{{ $t('challengeTiming.openHelp') }}</FieldDescription></Field>
        <Field><FieldLabel for="challenge-scoring-end">{{ $t('challengeTiming.scoringEnd') }}</FieldLabel><DateTimePicker id="challenge-scoring-end" v-model="scoringEnd" :disabled="pending||!canWrite" /><Button v-if="scoringEnd&&canWrite" type="button" variant="ghost" size="sm" :disabled="pending" @click="clearScoring">{{ $t('common.label.clear') }}</Button><FieldDescription>{{ $t('challengeTiming.scoreHelp') }}</FieldDescription></Field>
        <Field><FieldLabel for="challenge-submission-end">{{ $t('challengeTiming.submissionEnd') }}</FieldLabel><DateTimePicker id="challenge-submission-end" v-model="submissionEnd" :disabled="pending||!canWrite" /><Button v-if="submissionEnd&&canWrite" type="button" variant="ghost" size="sm" :disabled="pending" @click="clearSubmission">{{ $t('common.label.clear') }}</Button><FieldDescription>{{ $t('challengeTiming.closeHelp') }}</FieldDescription></Field>
      </FieldGroup>
      <p v-if="!valid" class="text-sm text-destructive">{{ $t('challengeTiming.orderInvalid') }}</p>
      <ol class="flex flex-wrap gap-3 text-sm text-muted-foreground"><li>{{ $t('challengeTiming.stageScoring') }}</li><li aria-hidden="true">→</li><li>{{ $t('challengeTiming.stageJudgement') }}</li><li aria-hidden="true">→</li><li>{{ $t('challengeTiming.stageClosed') }}</li></ol>
      <p v-if="current?.recalculationPending" class="text-sm text-muted-foreground" role="status">{{ $t('challengeTiming.recalculating') }}</p>
      <div v-if="canWrite"><Button type="submit" :disabled="pending||!valid||!dirty"><Spinner v-if="pending" />{{ $t('challengeTiming.previewAndSave') }}</Button></div>
    </UiForm>
    <AlertDialog :open="confirmation" @update:open="setConfirmation"><AlertDialogContent class="w-[calc(100vw-2rem)] data-[size=default]:max-w-3xl data-[size=default]:sm:max-w-3xl"><AlertDialogHeader><AlertDialogTitle>{{ $t('challengeTiming.confirmTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('challengeTiming.confirmHelp',{count:preview?.affectedAttempts??0}) }}</AlertDialogDescription></AlertDialogHeader>
      <ScrollSurface class="max-h-[50dvh]"><Table><TableHeader><TableRow><TableHead>{{ $t('common.label.team') }}</TableHead><TableHead>{{ $t('challengeTiming.scoreChange') }}</TableHead><TableHead>{{ $t('challengeTiming.otherChanges') }}</TableHead></TableRow></TableHeader><TableBody><TableRow v-for="team in preview?.teams" :key="team.teamId"><TableCell>{{ team.teamName }}</TableCell><TableCell class="tabular-nums">{{ team.scoreBefore }} → {{ team.scoreAfter }}</TableCell><TableCell class="min-w-40 whitespace-normal"><span v-if="team.bloodChanged">{{ $t('challengeTiming.bloodChange') }} · </span>{{ $t('challengeTiming.completionChange',{before:team.completionBefore??0,after:team.completionAfter??0,nodes:team.progressionNodesChanged??0}) }}</TableCell></TableRow></TableBody></Table></ScrollSurface>
      <AlertDialogFooter><AlertDialogCancel :disabled="pending">{{ $t('challengeTiming.cancel') }}</AlertDialogCancel><AlertDialogAction :disabled="pending" @click.prevent="apply"><Spinner v-if="pending" />{{ $t('challengeTiming.confirmSave') }}</AlertDialogAction></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </section>
</template>
