<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdTieBreakEditorViewState } from '~/features/admin/awd/useAwdTieBreakEditor'
const props = defineProps<{ state: AwdTieBreakEditorViewState }>()
const { rows, valid, canAdd, disabled, updateField, updateDirection, move, remove, add, reset, ArrowUp, ArrowDown, Plus, Trash2, RotateCcw } = toRefs(props.state)
</script>

<template>
  <DefinitionSection :title="$t('administration.awd.tieBreak.title')">
    <FieldDescription>{{ $t('administration.awd.tieBreak.notice') }}</FieldDescription>
    <div v-for="row in rows" :key="row.field" class="flex flex-wrap items-end gap-2">
      <span class="pb-2 font-mono tabular-nums text-muted-foreground">{{ row.index + 1 }}</span>
      <Field class="min-w-44 flex-1"><FieldLabel>{{ $t('administration.awd.tieBreak.fieldLabel') }}</FieldLabel><Select :model-value="row.field" :disabled="disabled" @update:model-value="updateField(row.index, $event)"><SelectTrigger :aria-label="$t('administration.awd.tieBreak.fieldAt', { position: row.index + 1 })"><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="option in row.choices" :key="option.value" :value="option.value">{{ option.label }}</SelectItem></SelectContent></Select></Field>
      <Field class="w-36"><FieldLabel>{{ $t('administration.awd.tieBreak.direction') }}</FieldLabel><Select :model-value="row.direction" :disabled="disabled" @update:model-value="updateDirection(row.index, $event)"><SelectTrigger :aria-label="$t('administration.awd.tieBreak.directionAt', { position: row.index + 1 })"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Descending">{{ $t('administration.awd.tieBreak.Descending') }}</SelectItem><SelectItem value="Ascending">{{ $t('administration.awd.tieBreak.Ascending') }}</SelectItem></SelectContent></Select></Field>
      <Button size="icon" variant="ghost" :disabled="disabled || row.index === 0" :aria-label="$t('administration.awd.tieBreak.moveUp', { position: row.index + 1 })" @click="move(row.index, -1)"><ArrowUp /></Button>
      <Button size="icon" variant="ghost" :disabled="disabled || row.index === rows.length - 1" :aria-label="$t('administration.awd.tieBreak.moveDown', { position: row.index + 1 })" @click="move(row.index, 1)"><ArrowDown /></Button>
      <Button size="icon" variant="ghost" :disabled="disabled || rows.length <= 1" :aria-label="$t('administration.awd.tieBreak.remove', { position: row.index + 1 })" @click="remove(row.index)"><Trash2 /></Button>
    </div>
    <FieldDescription v-if="!valid" class="text-destructive">{{ $t('administration.awd.tieBreak.invalid') }}</FieldDescription>
    <div class="flex flex-wrap gap-2"><Button variant="outline" :disabled="!canAdd" @click="add"><Plus data-icon="inline-start" />{{ $t('administration.awd.tieBreak.add') }}</Button><Button variant="ghost" :disabled="disabled" @click="reset"><RotateCcw data-icon="inline-start" />{{ $t('administration.awd.tieBreak.reset') }}</Button></div>
  </DefinitionSection>
</template>
