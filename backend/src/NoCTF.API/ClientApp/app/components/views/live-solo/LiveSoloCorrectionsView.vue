<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloCorrectionsState } from '~/features/live-solo/useLiveSoloCorrections'
const props = defineProps<{ state: LiveSoloCorrectionsState }>()
const { match, correction, preview, history, impacts, pending, winnerOptions, winner, leftWins, rightWins, reason, resolutionReason,
  selected, loading, busy, writable, error, decision, validProposal, canApply, dirty, load, editWinner, tally, consent, assess, open, setOpen,
  confirm, show, newProposal, enterMatch, back, matchStateKey, leaveOpen, setLeave, leave } = toRefs(props.state)
</script>
<template>
  <div data-contained-workspace-page class="flex h-full min-h-0 flex-col gap-4 px-4 pb-6 pt-3 md:px-8">
    <header class="flex flex-wrap items-center gap-3"><Button variant="ghost" @click="back">{{ $t('liveSolo.back') }}</Button><h1 class="text-display text-2xl">{{ $t('liveSolo.correction.title') }}</h1><Badge v-if="!writable" variant="secondary">{{ $t('liveSolo.settings.readOnly') }}</Badge><Button class="ml-auto" variant="ghost" :disabled="busy || dirty" @click="load">{{ $t('common.label.refresh') }}</Button></header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Card class="min-h-0 flex-1"><ScrollSurface axis="y" class="h-full" :aria-label="$t('liveSolo.correction.title')"><CardContent class="flex flex-col gap-6 py-5">
      <Skeleton v-if="loading && !match" class="h-64" />
      <template v-else-if="match">
        <header class="flex flex-wrap items-center gap-3"><h2 class="text-display text-xl">{{ match.leftTeamName }} / {{ match.rightTeamName }}</h2><span class="font-mono text-xl tabular-nums">{{ match.leftWins }} : {{ match.rightWins }}</span><Badge>{{ $t(matchStateKey(match.state)) }}</Badge></header>
        <template v-if="!correction">
          <UiForm v-if="writable && match.state === 'Completed'" validation="feature" class="flex max-w-2xl flex-col gap-4" @submit="assess">
            <Field><FieldLabel for="correction-winner">{{ $t('liveSolo.correction.winner') }}</FieldLabel><Select :model-value="winner" :disabled="busy || decision !== null" @update:model-value="editWinner"><SelectTrigger id="correction-winner"><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="option in winnerOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem></SelectContent></Select></Field>
            <div class="grid grid-cols-2 gap-4"><Field><FieldLabel for="correction-left">{{ match.leftTeamName }} · {{ $t('liveSolo.correction.wins') }}</FieldLabel><NumberInput id="correction-left" :min="0" :max="match.requiredWins" :model-value="leftWins" :disabled="busy || decision !== null" @update:model-value="tally('Left', $event)" /></Field><Field><FieldLabel for="correction-right">{{ match.rightTeamName }} · {{ $t('liveSolo.correction.wins') }}</FieldLabel><NumberInput id="correction-right" :min="0" :max="match.requiredWins" :model-value="rightWins" :disabled="busy || decision !== null" @update:model-value="tally('Right', $event)" /></Field></div>
            <Field><FieldLabel for="correction-reason">{{ $t('liveSolo.correction.reason') }}</FieldLabel><Textarea id="correction-reason" v-model="reason" :disabled="busy || decision !== null" :maxlength="4000" /></Field>
            <Button type="submit" class="self-start" :disabled="busy || !validProposal"><Spinner v-if="busy" data-icon="inline-start" />{{ $t('liveSolo.correction.preview') }}</Button>
          </UiForm>
          <p v-else class="text-sm text-muted-foreground">{{ $t('liveSolo.correction.completedOnly') }}</p>
          <template v-if="preview"><h3 class="text-display">{{ $t('liveSolo.correction.impact') }}</h3><p class="font-mono tabular-nums">{{ preview.previousLeftWins }} : {{ preview.previousRightWins }} → {{ leftWins }} : {{ rightWins }}</p><p class="text-sm text-muted-foreground">{{ $t('liveSolo.correction.freezeHelp') }}</p></template>
        </template>
        <template v-else>
          <div class="flex flex-wrap items-center gap-3"><Badge>{{ $t(correction.state === 'Pending' ? 'liveSolo.correction.pending' : correction.state === 'Applied' ? 'liveSolo.correction.applied' : 'liveSolo.correction.canceled') }}</Badge><span class="font-mono tabular-nums">{{ correction.previousLeftWins }} : {{ correction.previousRightWins }} → {{ correction.leftWins }} : {{ correction.rightWins }}</span><span class="text-sm text-muted-foreground">{{ correction.actorName }} · {{ formatDateTime(correction.createdAt) }}</span></div>
          <p class="whitespace-pre-wrap break-words">{{ correction.reason }}</p>
          <p v-if="pending" class="text-sm text-muted-foreground">{{ $t('liveSolo.correction.pendingHelp') }}</p>
          <template v-else><p class="whitespace-pre-wrap break-words">{{ correction.resolutionReason }}</p><p class="text-sm text-muted-foreground">{{ correction.resolvedByName }} · {{ formatDateTime(correction.resolvedAt) }}</p><Button v-if="writable" variant="outline" class="self-start" @click="newProposal">{{ $t('liveSolo.correction.new') }}</Button></template>
        </template>
        <section v-if="preview || correction" class="flex flex-col gap-3" :aria-label="$t('liveSolo.correction.impact')">
          <p v-if="!impacts.length" class="text-sm text-muted-foreground">{{ $t('liveSolo.correction.noDownstream') }}</p>
          <div v-for="(row, index) in impacts" :key="row.matchId" class="flex flex-wrap items-center gap-3 border-b py-3">
            <span class="font-mono text-sm tabular-nums">{{ index + 1 }}</span><Button variant="link" class="min-w-0 justify-start whitespace-normal break-words p-0" @click="enterMatch(row.matchId!)">{{ row.leftTeamName ?? '—' }} / {{ row.rightTeamName ?? '—' }}</Button><Badge variant="secondary">{{ $t(matchStateKey(row.state)) }}</Badge>
            <Badge v-if="row.requiresReplay" variant="destructive">{{ $t(correction && correction.state !== 'Pending' ? 'liveSolo.correction.wasStarted' : 'liveSolo.correction.started') }}</Badge><span v-else class="text-sm text-muted-foreground">{{ $t('liveSolo.correction.unstarted') }}</span>
            <Field v-if="pending && row.requiresReplay && writable" orientation="horizontal" class="basis-full"><Checkbox :id="'replay-'+row.matchId" :model-value="selected.has(row.matchId!)" :disabled="busy || decision !== null" @update:model-value="consent(row.matchId!, $event)" /><FieldLabel :for="'replay-'+row.matchId">{{ $t('liveSolo.correction.replayConsent') }}</FieldLabel></Field>
            <Button v-if="row.replacementMatchId" variant="outline" size="sm" @click="enterMatch(row.replacementMatchId)">{{ $t('liveSolo.correction.replacement') }}</Button>
          </div>
        </section>
        <Button v-if="preview && !correction && writable" class="self-start" :disabled="busy || !validProposal" @click="open('Begin')">{{ $t('liveSolo.correction.begin') }}</Button>
        <template v-if="pending && writable"><Field class="max-w-2xl"><FieldLabel for="correction-resolution">{{ $t('liveSolo.correction.resolutionReason') }}</FieldLabel><Textarea id="correction-resolution" v-model="resolutionReason" :maxlength="4000" :disabled="busy || decision !== null" /></Field><div class="flex flex-wrap gap-3"><Button variant="outline" :disabled="busy || !resolutionReason.trim()" @click="open('Cancel')">{{ $t('liveSolo.correction.cancel') }}</Button><Button :disabled="busy || !canApply" @click="open('Apply')">{{ $t('liveSolo.correction.apply') }}</Button></div></template>
        <section v-if="history.length" class="flex flex-col gap-2" :aria-label="$t('liveSolo.correction.history')"><h3 class="text-display">{{ $t('liveSolo.correction.history') }}</h3><Button v-for="item in history" :key="item.id" variant="ghost" class="h-auto justify-start whitespace-normal text-left" @click="show(item.id!)">{{ formatDateTime(item.createdAt) }} · {{ item.actorName }} · {{ item.leftWins }} : {{ item.rightWins }}</Button></section>
      </template>
    </CardContent></ScrollSurface></Card>
    <AlertDialog :open="decision !== null" @update:open="setOpen"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t(decision === 'Begin' ? 'liveSolo.correction.begin' : decision === 'Apply' ? 'liveSolo.correction.apply' : 'liveSolo.correction.cancel') }}</AlertDialogTitle><AlertDialogDescription>{{ $t(decision === 'Cancel' ? 'liveSolo.correction.cancelHelp' : decision === 'Begin' ? 'liveSolo.correction.freezeHelp' : 'liveSolo.correction.replayHelp') }}</AlertDialogDescription></AlertDialogHeader><p class="whitespace-pre-wrap break-words text-sm">{{ decision === 'Begin' ? reason : resolutionReason }}</p><AlertDialogFooter><AlertDialogCancel :disabled="busy">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button :disabled="busy" @click="confirm"><Spinner v-if="busy" data-icon="inline-start" />{{ $t('common.action.confirm') }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
    <AlertDialog :open="leaveOpen" @update:open="setLeave"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('liveSolo.correction.unsavedTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.correction.unsavedHelp') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel><Button variant="destructive" @click="leave">{{ $t('liveSolo.correction.discard') }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </div>
</template>
