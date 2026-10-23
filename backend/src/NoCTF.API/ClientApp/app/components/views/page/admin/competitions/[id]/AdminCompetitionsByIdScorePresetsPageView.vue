<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdScorePresetsPageViewState } from '~/features/routes/admin/competitions/[id]/useAdminCompetitionsByIdScorePresetsPage'
const props = defineProps<{ state: AdminCompetitionsByIdScorePresetsPageViewState }>()
const { Plus, Pencil, Trash2, ConfigFieldInput, items, loading, saving, deleting, error, editing, editId, name, curve, field, canSave, canWrite, create, start, close, updateCurve, modeLabel, save, remove } = toRefs(props.state)
</script>
<template>
  <div class="flex h-full min-h-0 flex-col gap-4">
    <div class="flex items-center justify-between gap-3"><h1 class="text-display text-2xl">{{ $t('scorePresets.title') }}</h1>
      <Button v-if="canWrite" :disabled="saving || loading" @click="create"><Plus data-icon="inline-start" />{{ $t('scorePresets.create') }}</Button>
    </div>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <ScrollSurface class="min-h-0 flex-1">
      <Card><CardContent class="flex flex-col gap-5 pt-6">
        <Skeleton v-if="loading" class="h-48 w-full" />
        <Table v-else-if="items.length"><TableHeader><TableRow>
          <TableHead>{{ $t('scorePresets.name') }}</TableHead><TableHead>{{ $t('administration.label.decayMode') }}</TableHead>
          <TableHead>{{ $t('administration.label.initialScore') }}</TableHead><TableHead>{{ $t('administration.label.lowestScore') }}</TableHead>
          <TableHead>{{ $t('scorePresets.minimumTeamCount') }}</TableHead><TableHead>{{ $t('scorePresets.challengeCount') }}</TableHead><TableHead />
        </TableRow></TableHeader><TableBody><TableRow v-for="item in items" :key="item.id">
          <TableCell>{{ item.name }}</TableCell><TableCell>{{ $t(modeLabel(item)) }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.scoreCurve?.initialPoints }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.scoreCurve?.minimumPoints }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.scoreCurve?.decayTeamCount }}</TableCell>
          <TableCell class="font-mono tabular-nums">{{ item.challengeCount }}</TableCell>
          <TableCell><div class="flex justify-end gap-1">
            <Button variant="ghost" size="icon" :disabled="saving || !!deleting" :aria-label="$t('scorePresets.edit')" @click="start(item)"><Pencil /></Button>
            <Button v-if="canWrite" variant="ghost" size="icon" :disabled="saving || !!deleting" :aria-label="$t('scorePresets.delete')" @click="remove(item)"><Spinner v-if="deleting === item.id" /><Trash2 v-else /></Button>
          </div></TableCell>
        </TableRow></TableBody></Table>
        <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('scorePresets.empty') }}</EmptyTitle></EmptyHeader></Empty>
        <template v-if="editing"><Separator /><UiForm validation="feature" @submit.prevent="save"><FieldGroup>
          <h2 class="text-display text-lg">{{ $t(editId ? 'scorePresets.edit' : 'scorePresets.create') }}</h2>
          <Field><FieldLabel for="score-preset-name">{{ $t('scorePresets.name') }}</FieldLabel><Input id="score-preset-name" v-model="name" :maxlength="40" :disabled="saving || !canWrite" />
            <FieldDescription>{{ $t('scorePresets.nameHelp') }}</FieldDescription></Field>
          <component :is="ConfigFieldInput" :field="field" :model-value="curve" :disabled="saving || !canWrite" @update:model-value="updateCurve" />
          <FieldDescription>{{ $t('scorePresets.saveHelp') }}</FieldDescription>
          <div class="flex justify-end gap-2"><Button type="button" variant="ghost" :disabled="saving" @click="close">{{ $t('common.action.cancel') }}</Button>
            <Button v-if="canWrite" type="submit" :disabled="!canSave"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('administration.label.saveSettings') }}</Button></div>
        </FieldGroup></UiForm></template>
      </CardContent></Card>
    </ScrollSurface>
  </div>
</template>
