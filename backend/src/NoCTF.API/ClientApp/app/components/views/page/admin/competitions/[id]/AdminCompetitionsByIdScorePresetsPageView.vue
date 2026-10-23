<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdScorePresetsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdScorePresetsPage'
const props = defineProps<{ state: AdminCompetitionsByIdScorePresetsPageViewState }>()
const { Plus, Pencil, Trash2, ConfigFieldInput, items, loading, saving, deleting, error, editing, editId, name, hasScoreCurve, curve, field, canSave, canWrite, create, editTag, close, setEditing, updateCurve, modeLabel, save, remove } = toRefs(props.state)
</script>
<template>
  <div class="flex min-w-0 flex-col gap-6">
    <div class="flex items-center justify-between gap-3"><h2 class="text-display text-xl">{{ $t('scorePresets.title') }}</h2>
      <Button v-if="canWrite" :disabled="saving || loading" @click="create"><Plus data-icon="inline-start" />{{ $t('scorePresets.create') }}</Button>
    </div>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
        <Skeleton v-if="loading" class="h-48 w-full" />
        <Table v-else-if="items.length" class="[&_th]:px-4 [&_th]:py-3 [&_td]:px-4 [&_td]:py-4"><TableHeader class="bg-muted/30"><TableRow>
          <TableHead>{{ $t('scorePresets.name') }}</TableHead><TableHead>{{ $t('common.label.type') }}</TableHead><TableHead>{{ $t('administration.label.decayMode') }}</TableHead>
          <TableHead>{{ $t('administration.label.initialScore') }}</TableHead><TableHead>{{ $t('administration.label.lowestScore') }}</TableHead>
          <TableHead>{{ $t('scorePresets.minimumTeamCount') }}</TableHead><TableHead>{{ $t('scorePresets.challengeCount') }}</TableHead><TableHead class="text-right">{{ $t('common.label.actions') }}</TableHead>
        </TableRow></TableHeader><TableBody><TableRow v-for="item in items" :key="item.name">
          <TableCell>{{ item.name }}</TableCell>
          <TableCell><Badge :variant="item.preset?.scoreCurve ? 'default' : 'secondary'">{{ $t(item.preset?.scoreCurve ? 'challengeTags.scoringTag' : 'challengeTags.ordinaryTag') }}</Badge></TableCell>
          <TableCell>{{ item.preset?.scoreCurve ? $t(modeLabel(item.preset)) : '—' }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.preset?.scoreCurve?.initialPoints ?? '—' }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.preset?.scoreCurve?.minimumPoints ?? '—' }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.preset?.scoreCurve?.decayTeamCount ?? '—' }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.challengeCount }}</TableCell>
          <TableCell><div v-if="canWrite" class="flex justify-end gap-1">
            <Button variant="ghost" size="icon" :disabled="saving || !!deleting" :aria-label="$t('scorePresets.edit')" @click="editTag(item)"><Pencil /></Button>
            <Button v-if="item.preset" variant="ghost" size="icon" :disabled="saving || !!deleting" :aria-label="$t('scorePresets.delete')" @click="remove(item.preset)"><Spinner v-if="deleting === item.preset.id" /><Trash2 v-else /></Button>
          </div></TableCell>
        </TableRow></TableBody></Table>
        <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('scorePresets.empty') }}</EmptyTitle></EmptyHeader></Empty>
    <Dialog :open="editing" @update:open="setEditing"><DialogScrollContent class="sm:max-w-2xl">
        <DialogHeader><DialogTitle>{{ $t(editId ? 'scorePresets.edit' : 'scorePresets.create') }}</DialogTitle><DialogDescription>{{ $t('scorePresets.saveHelp') }}</DialogDescription></DialogHeader>
        <UiForm validation="feature" @submit.prevent="save"><FieldGroup>
          <Field><FieldLabel for="score-preset-name">{{ $t('scorePresets.name') }}</FieldLabel><Input id="score-preset-name" v-model="name" :maxlength="40" :disabled="saving || !canWrite" />
            <FieldDescription>{{ $t('scorePresets.nameHelp') }}</FieldDescription></Field>
          <Field orientation="horizontal"><FieldContent><FieldLabel for="tag-score-curve">{{ $t('challengeTags.enableScoring') }}</FieldLabel>
            <FieldDescription>{{ $t('challengeTags.optionalScoringHelp') }}</FieldDescription></FieldContent>
            <Switch id="tag-score-curve" v-model="hasScoreCurve" :disabled="saving || !canWrite" /></Field>
          <component :is="ConfigFieldInput" v-if="hasScoreCurve" :field="field" :model-value="curve" :disabled="saving || !canWrite" @update:model-value="updateCurve" />
          <div class="flex justify-end gap-2"><Button type="button" variant="ghost" :disabled="saving" @click="close">{{ $t('common.action.cancel') }}</Button>
            <Button v-if="canWrite" type="submit" :disabled="!canSave"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('common.action.save') }}</Button></div>
        </FieldGroup></UiForm>
    </DialogScrollContent></Dialog>
  </div>
</template>
