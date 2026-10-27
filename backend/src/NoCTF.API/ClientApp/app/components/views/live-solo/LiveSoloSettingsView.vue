<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloSettingsState } from '~/features/live-solo/useLiveSoloSettings'
const props = defineProps<{ state: LiveSoloSettingsState }>()
const { draft, loading, busy, dirty, writable, hasMatches, error, saved, formatOptions, laneOptions, sections, load, number, format,
  addStage, removeStage, stageNumber, stageLane, enabled, evidence, streaming, recording, opponents, save, leaveOpen, setLeave, leave,
  tournamentFields, swissFields, addTournamentStage, moveTournamentStage, removeTournamentStage, tournamentNumber, tournamentFormat, formatKey } = toRefs(props.state)
</script>
<template>
  <div class="flex w-full min-w-0 flex-col gap-6">
    <header class="flex flex-wrap items-center gap-4"><h1 class="text-display text-2xl">{{ $t('liveSolo.navigation.modeSettings') }}</h1><Badge v-if="!writable" variant="secondary">{{ $t('liveSolo.settings.readOnly') }}</Badge><Button class="ml-auto" variant="ghost" :disabled="busy || dirty" @click="load">{{ $t('common.label.refresh') }}</Button></header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Alert v-if="saved"><AlertDescription>{{ $t('liveSolo.settings.saved') }}</AlertDescription></Alert>
    <Card class="min-w-0"><CardContent class="min-w-0">
      <Skeleton v-if="loading && !draft" class="h-64" />
      <UiForm v-else-if="draft" validation="feature" class="flex flex-col gap-5" @submit="save">
        <Field orientation="horizontal"><Switch id="live-solo-enabled" :model-value="draft.enabled" :disabled="!writable || busy" @update:model-value="enabled" /><FieldLabel for="live-solo-enabled">{{ $t('liveSolo.settings.enabled') }}</FieldLabel></Field>
        <p class="text-sm text-muted-foreground">{{ $t('liveSolo.settings.snapshot') }}</p>
        <DefinitionSection :title="$t('liveSolo.tournament.stageFlow')" :collapsible="false">
          <div class="flex flex-col gap-6">
            <p v-if="!draft.tournamentStages.length" class="text-sm text-muted-foreground">{{ $t('liveSolo.tournament.legacyStage') }}</p>
            <section v-for="(stage,index) in draft.tournamentStages" :key="index" class="flex flex-col gap-4 border-b pb-6 last:border-0">
              <header class="flex flex-wrap items-center gap-2"><Badge variant="secondary">{{ $t('liveSolo.tournament.stageNumber',{number:index+1}) }}</Badge>
                <span class="text-sm text-muted-foreground">{{ $t(formatKey(stage.format)) }}</span>
                <div v-if="writable && !hasMatches" class="ml-auto flex gap-1">
                  <Button type="button" variant="ghost" size="sm" :disabled="busy || index === 0" @click="moveTournamentStage(index,-1)">{{ $t('liveSolo.bracket.upShort') }}</Button>
                  <Button type="button" variant="ghost" size="sm" :disabled="busy || index === draft.tournamentStages.length-1" @click="moveTournamentStage(index,1)">{{ $t('liveSolo.bracket.downShort') }}</Button>
                  <Button type="button" variant="ghost" size="sm" :disabled="busy" @click="removeTournamentStage(index)">{{ $t('liveSolo.settings.remove') }}</Button>
                </div>
              </header>
              <FieldGroup class="grid min-w-0 gap-4 md:grid-cols-2 xl:grid-cols-4">
                <Field><FieldLabel :for="'tournament-name-'+index">{{ $t('liveSolo.tournament.stageName') }}</FieldLabel><Input :id="'tournament-name-'+index" v-model="stage.name" :maxlength="120" :disabled="!writable || busy || hasMatches" /></Field>
                <Field><FieldLabel :for="'tournament-format-'+index">{{ $t('liveSolo.settings.format') }}</FieldLabel><Select :model-value="stage.format" :disabled="!writable || busy || hasMatches" @update:model-value="tournamentFormat(index,$event)"><SelectTrigger :id="'tournament-format-'+index"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="option in formatOptions" :key="option.value" :value="option.value">{{ $t(option.key) }}</SelectItem></SelectGroup></SelectContent></Select></Field>
                <Field v-for="field in tournamentFields" :key="field.key"><FieldLabel :for="'tournament-'+field.key+index">{{ $t(field.label) }}</FieldLabel><NumberInput :id="'tournament-'+field.key+index" :model-value="stage[field.key]" :min="field.min" :max="field.max" :disabled="!writable || busy || hasMatches" @update:model-value="tournamentNumber(index,field.key,$event)" /></Field>
                <template v-if="stage.format === 'Swiss'"><Field v-for="field in swissFields" :key="field.key"><FieldLabel :for="'swiss-'+field.key+index">{{ $t(field.label) }}</FieldLabel><NumberInput :id="'swiss-'+field.key+index" :model-value="stage[field.key]" :min="field.min" :max="field.max" :disabled="!writable || busy || hasMatches" @update:model-value="tournamentNumber(index,field.key,$event)" /></Field></template>
              </FieldGroup>
              <p class="text-sm text-muted-foreground">{{ $t(stage.format === 'Swiss' ? 'liveSolo.tournament.swissRules' : 'liveSolo.tournament.eliminationRules') }}</p>
            </section>
            <div><Button v-if="writable" type="button" variant="outline" :disabled="busy || hasMatches" @click="addTournamentStage">{{ $t('liveSolo.tournament.addStage') }}</Button></div>
            <p v-if="hasMatches" class="text-sm text-muted-foreground">{{ $t('liveSolo.settings.formatLocked') }}</p>
          </div>
        </DefinitionSection>
        <DefinitionSection v-for="section in sections" :key="section.key" :title="$t(section.title)" :collapsible="false"><FieldGroup class="grid min-w-0 gap-5 md:grid-cols-2 xl:grid-cols-3">
          <Field v-if="section.key === 'match' && !draft.tournamentStages.length"><FieldLabel for="live-solo-format">{{ $t('liveSolo.settings.format') }}</FieldLabel><Select :model-value="draft.bracketFormat" :disabled="!writable || busy || hasMatches" @update:model-value="format"><SelectTrigger id="live-solo-format"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="item in formatOptions" :key="item.value" :value="item.value">{{ $t(item.key) }}</SelectItem></SelectGroup></SelectContent></Select><FieldDescription v-if="hasMatches">{{ $t('liveSolo.settings.formatLocked') }}</FieldDescription></Field>
          <Field v-if="section.key === 'match'" class="md:col-span-2 xl:col-span-3"><div class="flex items-center gap-3"><Switch id="live-solo-evidence" :model-value="draft.staticFlagEvidenceRequired" :disabled="!writable || busy" @update:model-value="evidence" /><FieldLabel for="live-solo-evidence">{{ $t('liveSolo.settings.evidence') }}</FieldLabel></div><FieldDescription>{{ $t('liveSolo.settings.evidenceHelp') }}</FieldDescription></Field>
          <Field v-if="section.key === 'media'" class="md:col-span-2 xl:col-span-3"><div class="flex items-center gap-3"><Switch id="live-solo-streaming" :model-value="draft.platformStreamingEnabled" :disabled="!writable || busy" @update:model-value="streaming" /><FieldLabel for="live-solo-streaming">{{ $t('liveSolo.settings.streaming') }}</FieldLabel></div><FieldDescription>{{ $t('liveSolo.settings.streamingHelp') }}</FieldDescription></Field>
          <Field v-for="field in section.fields" :key="field.key"><FieldLabel :for="field.key">{{ $t(field.label) }}</FieldLabel><NumberInput :id="field.key" :model-value="draft[field.key]" :min="field.min" :max="field.max" :disabled="!writable || busy" @update:model-value="number(field.key, $event)" /></Field>
          <template v-if="section.key === 'media' && draft.platformStreamingEnabled"><Field orientation="horizontal"><Switch id="live-solo-opponents" :model-value="draft.participantsMayViewOpponents" :disabled="!writable || busy" @update:model-value="opponents" /><FieldLabel for="live-solo-opponents">{{ $t('liveSolo.settings.opponents') }}</FieldLabel></Field><Field orientation="horizontal"><Switch id="live-solo-recording" :model-value="draft.recordingEnabled" :disabled="!writable || busy" @update:model-value="recording" /><FieldLabel for="live-solo-recording">{{ $t('liveSolo.settings.recording') }}</FieldLabel></Field></template>
        </FieldGroup></DefinitionSection>
        <Separator />
        <DefinitionSection :title="$t('liveSolo.settings.stages')" :collapsible="false">
          <div class="flex flex-col gap-4">
            <FieldGroup v-for="(row,index) in draft.stageRules" :key="index" class="grid min-w-0 items-end gap-4 md:grid-cols-2 xl:grid-cols-4">
              <Field><FieldLabel :for="'rule-tournament-'+index">{{ $t('liveSolo.tournament.selectStage') }}</FieldLabel><NumberInput :id="'rule-tournament-'+index" :model-value="row.tournamentStage ?? 1" :min="1" :disabled="!writable || busy" @update:model-value="stageNumber(index,'tournamentStage',$event)" /></Field>
              <Field><FieldLabel :for="'stage-lane-' + index">{{ $t('liveSolo.settings.lane') }}</FieldLabel><Select :model-value="row.lane" :disabled="!writable || busy" @update:model-value="stageLane(index,$event)"><SelectTrigger :id="'stage-lane-' + index"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="item in laneOptions" :key="item.value" :value="item.value">{{ $t(item.key) }}</SelectItem></SelectGroup></SelectContent></Select></Field>
              <Field><FieldLabel :for="'stage-number-' + index">{{ $t('liveSolo.settings.stage') }}</FieldLabel><NumberInput :id="'stage-number-' + index" :model-value="row.stage" :min="1" :disabled="!writable || busy" @update:model-value="stageNumber(index,'stage',$event)" /></Field>
              <Field><FieldLabel :for="'stage-wins-' + index">{{ $t('liveSolo.settings.stageWins') }}</FieldLabel><NumberInput :id="'stage-wins-' + index" :model-value="row.requiredWins" :min="1" :max="1024" :disabled="!writable || busy" @update:model-value="stageNumber(index,'requiredWins',$event)" /></Field>
              <Button v-if="writable" type="button" variant="ghost" class="justify-self-start" :disabled="busy" @click="removeStage(index)">{{ $t('liveSolo.settings.remove') }}</Button>
            </FieldGroup>
            <div><Button v-if="writable" type="button" variant="outline" :disabled="busy" @click="addStage">{{ $t('liveSolo.settings.addStage') }}</Button></div>
          </div>
        </DefinitionSection>
        <Separator /><div v-if="writable" class="flex flex-wrap items-center justify-end gap-3"><Badge v-if="dirty" variant="secondary">{{ $t('liveSolo.settings.unsaved') }}</Badge><Button type="submit" :disabled="busy || !dirty"><Spinner v-if="busy" data-icon="inline-start" />{{ $t('liveSolo.settings.save') }}</Button></div>
      </UiForm>
    </CardContent></Card>
    <AlertDialog :open="leaveOpen" @update:open="setLeave"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('liveSolo.settings.unsavedTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.settings.unsavedDescription') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel>{{ $t('common.action.cancel') }}</AlertDialogCancel><Button @click="leave">{{ $t('liveSolo.settings.discard') }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </div>
</template>
