<script setup lang="ts">
import type { LiveSoloMatchDirectionsState } from '~/features/live-solo/useLiveSoloMatchDirections'
const props = defineProps<{ state: LiveSoloMatchDirectionsState }>()
const { state, presets, groups, selectedCount, suggestedSlots, remaining, poolRows, directionCounts, drawnRows, busy, error, editorOpen, presetName, direction, questionGroupId,
  editingPreset, directionOptions, batchGroups, batchSelectedCount, groupSearch, toggleGroup, selectVisibleGroups, clearGroups,
  rescueUserId, rescueReason, decisionReasons, rescueOptions, requests, validPreset, togglePreset, editPreset, closeEditor, savePreset, removePreset,
  configure, draw, requestRescue, resolve } = toRefs(props.state)
</script>
<template>
  <section v-if="state?.pool?.length || state?.canConfigure || state?.canManagePresets || state?.canRequestRescue || requests.length || error" class="flex min-w-0 flex-col gap-4">
    <div v-if="poolRows.length || state?.canConfigure || state?.canManagePresets" class="flex flex-wrap items-center justify-between gap-2"><h2 class="font-semibold">{{ $t('liveSolo.draw.title') }}</h2><Badge v-if="poolRows.length" variant="secondary">{{ $t('liveSolo.draw.remainingCount', { count: remaining }) }}</Badge></div>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <ul v-if="directionCounts.length" class="flex flex-col gap-2" aria-live="polite" :aria-label="$t('liveSolo.draw.directionCounts')">
      <li v-for="entry in directionCounts" :key="entry.direction" class="flex items-center justify-between gap-2 text-sm"><span class="font-medium">{{ entry.direction }}</span><Badge :variant="entry.remaining ? 'default' : 'secondary'">{{ $t('liveSolo.draw.ticketsRemaining', { count: entry.remaining }) }}</Badge></li>
    </ul>
    <ol v-if="drawnRows.length" class="flex flex-col gap-2" :aria-label="$t('liveSolo.draw.history')">
      <li v-for="entry in drawnRows" :key="entry.id" class="flex flex-wrap items-center justify-between gap-2 text-sm"><span><template v-if="entry.name !== entry.direction">{{ entry.name }} · </template>{{ entry.direction }}</span><Badge variant="secondary">{{ $message(entry.label) }}</Badge></li>
    </ol>
    <Button v-if="state?.canDraw" :disabled="busy" @click="draw"><Spinner v-if="busy" data-icon="inline-start" />{{ $t('liveSolo.draw.draw') }}</Button>
    <Accordion v-if="state?.canConfigure || state?.canManagePresets" type="single" collapsible>
      <AccordionItem value="pool"><AccordionTrigger>{{ $t('liveSolo.draw.configure') }}</AccordionTrigger><AccordionContent class="flex flex-col gap-4">
        <p class="text-sm text-muted-foreground">{{ $t('liveSolo.draw.poolHelp', { count: suggestedSlots }) }}</p>
        <Empty v-if="!presets.length"><EmptyHeader><EmptyTitle>{{ $t('liveSolo.draw.noPresets') }}</EmptyTitle></EmptyHeader></Empty>
        <div v-for="preset in presets" :key="preset.id" class="flex items-start gap-2">
          <Field orientation="horizontal" class="min-w-0 flex-1"><Checkbox v-if="state?.canConfigure" :id="`draw-${preset.id}`" :model-value="preset.selected" :disabled="busy" @update:model-value="togglePreset(preset.id!, $event)" /><FieldLabel :for="`draw-${preset.id}`" class="flex min-w-0 flex-col items-start gap-1"><span>{{ preset.name }} · {{ preset.direction }}</span><span class="text-xs text-muted-foreground">{{ preset.questionGroupName }}</span></FieldLabel></Field>
          <template v-if="state?.canManagePresets"><Button variant="ghost" size="sm" :disabled="busy" @click="editPreset(preset)">{{ $t('administration.label.edit') }}</Button><Button variant="ghost" size="sm" :disabled="busy" @click="removePreset(preset.id!)">{{ $t('common.action.delete') }}</Button></template>
        </div>
        <p v-if="state?.canConfigure" class="text-sm font-mono tabular-nums">{{ $t('liveSolo.draw.selectedCount', { count: selectedCount }) }}</p>
        <div class="flex flex-wrap gap-2"><template v-if="state?.canConfigure"><Button :disabled="busy" @click="configure()">{{ $t('liveSolo.draw.savePool') }}</Button><Button v-if="poolRows.length" variant="outline" :disabled="busy" @click="configure(true)">{{ $t('liveSolo.draw.clearPool') }}</Button></template><Button v-if="state?.canManagePresets" variant="outline" :disabled="busy" @click="editPreset()">{{ $t('liveSolo.draw.newPreset') }}</Button></div>
        <UiForm v-if="editorOpen" validation="feature" class="flex flex-col gap-3" @submit="savePreset"><FieldGroup>
          <Field v-if="editingPreset"><FieldLabel>{{ $t('liveSolo.draw.presetName') }}</FieldLabel><Input v-model="presetName" :maxlength="160" :disabled="busy" /></Field>
          <Field><FieldLabel>{{ $t('liveSolo.draw.direction') }}</FieldLabel><ChoicePicker v-model="direction" :items="directionOptions" :disabled="busy" :label="$t('liveSolo.draw.direction')" :search-label="$t('liveSolo.draw.searchDirection')" :empty-label="$t('liveSolo.draw.noDirections')" /></Field>
          <Field v-if="editingPreset"><FieldLabel>{{ $t('liveSolo.draw.questionList') }}</FieldLabel><ChoicePicker v-model="questionGroupId" :items="groups" :disabled="busy" :label="$t('liveSolo.draw.questionList')" :search-label="$t('liveSolo.groups.name')" :empty-label="$t('liveSolo.selection.empty')" /></Field>
          <Field v-else><FieldLabel>{{ $t('liveSolo.draw.questionLists') }}</FieldLabel><FieldDescription>{{ $t('liveSolo.draw.batchHelp') }}</FieldDescription>
            <Input v-model="groupSearch" :disabled="busy" :placeholder="$t('liveSolo.draw.searchGroups')" :aria-label="$t('liveSolo.draw.searchGroups')" />
            <div class="flex flex-wrap items-center gap-2"><Button type="button" variant="ghost" size="sm" :disabled="busy || !batchGroups.length" @click="selectVisibleGroups">{{ $t('liveSolo.draw.selectVisible') }}</Button><Button type="button" variant="ghost" size="sm" :disabled="busy || !batchSelectedCount" @click="clearGroups">{{ $t('liveSolo.draw.clearSelection') }}</Button></div>
            <ScrollSurface class="max-h-56" :aria-label="$t('liveSolo.draw.questionLists')"><FieldGroup v-if="batchGroups.length" class="gap-3 p-1"><Field v-for="group in batchGroups" :key="group.value" orientation="horizontal"><Checkbox :id="`preset-group-${group.value}`" :model-value="group.selected" :disabled="busy" @update:model-value="toggleGroup(group.value, $event)" /><FieldLabel :for="`preset-group-${group.value}`" class="min-w-0 break-words">{{ group.label }}</FieldLabel></Field></FieldGroup><Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.selection.empty') }}</EmptyTitle></EmptyHeader></Empty></ScrollSurface>
            <FieldDescription>{{ $t('liveSolo.draw.batchSelected', { count: batchSelectedCount }) }}</FieldDescription>
            <FieldDescription v-if="state?.canConfigure">{{ $t('liveSolo.draw.batchPoolHelp') }}</FieldDescription>
          </Field>
        </FieldGroup><div class="flex flex-wrap gap-2"><Button type="submit" :disabled="busy || !validPreset"><Spinner v-if="busy" data-icon="inline-start" />{{ $t(editingPreset ? 'common.action.save' : 'liveSolo.draw.saveBatch') }}</Button><Button type="button" variant="ghost" :disabled="busy" @click="closeEditor">{{ $t('common.action.cancel') }}</Button></div></UiForm>
      </AccordionContent></AccordionItem>
    </Accordion>
    <section v-if="state?.canRequestRescue || requests.length" class="flex flex-col gap-3">
      <h3 class="font-semibold">{{ $t('liveSolo.rescue.title') }}</h3>
      <UiForm v-if="state?.canRequestRescue && rescueOptions.length" validation="feature" class="flex flex-col gap-3" @submit="requestRescue"><FieldGroup>
        <Field><FieldLabel>{{ $t('liveSolo.rescue.teammate') }}</FieldLabel><ChoicePicker v-model="rescueUserId" :items="rescueOptions" :disabled="busy" :label="$t('liveSolo.rescue.teammate')" :search-label="$t('liveSolo.rescue.teammate')" :empty-label="$t('liveSolo.rescue.noTeammates')" /></Field>
        <Field><FieldLabel>{{ $t('liveSolo.rescue.reason') }}</FieldLabel><Textarea v-model="rescueReason" :maxlength="4000" :disabled="busy" /></Field>
      </FieldGroup><Button type="submit" :disabled="busy || !rescueUserId">{{ $t('liveSolo.rescue.request') }}</Button></UiForm>
      <div v-for="item in requests" :key="item.id" class="flex flex-col gap-2 py-2">
        <div class="flex flex-wrap items-center justify-between gap-2"><span class="font-medium">{{ item.userName }}</span><Badge variant="secondary">{{ $t(item.stateKey) }}</Badge></div>
        <p class="whitespace-pre-wrap break-words text-sm">{{ item.reason }}</p><p v-if="item.decisionReason" class="whitespace-pre-wrap break-words text-sm text-muted-foreground">{{ item.decisionReason }}</p>
        <template v-if="item.mayResolve"><Field><FieldLabel>{{ $t('liveSolo.rescue.decisionReason') }}</FieldLabel><Textarea v-model="decisionReasons[item.id!]" :maxlength="4000" :disabled="busy" /></Field><div class="flex flex-wrap gap-2"><Button :disabled="busy" @click="resolve(item.id!, true)">{{ $t('liveSolo.rescue.approve') }}</Button><Button variant="outline" :disabled="busy" @click="resolve(item.id!, false)">{{ $t('liveSolo.rescue.reject') }}</Button></div></template>
      </div>
    </section>
  </section>
</template>
