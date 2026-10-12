<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionModeConfigEditorViewState } from '~/features/admin/useCompetitionModeConfigEditor'

const viewProps = defineProps<{ state: CompetitionModeConfigEditorViewState }>()
const { isAwd, changeReason, reasonRequired, invalidFields, canSaveRules, groups, DefinitionSection, presetOpen, presetChanges, applyPreset, closePreset, RotateCcw, values, parseFailed, updateField, resetToCurrentDefaults, dirty, save, ConfigFieldInput, readonly, loading, saving } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex items-center gap-2 text-xs text-muted-foreground">
      <span v-if="dirty">{{ $t('administration.label.thereUnsavedChanges') }}</span>
    </div>
    <Skeleton v-if="loading" class="h-64 w-full" />
    <Alert v-else-if="parseFailed" variant="destructive">
      <AlertDescription class="flex flex-col items-start gap-3">
        <span>{{ $t('administration.error.typedConfigurationUnavailable') }}</span>
        <Button v-if="!readonly" type="button" variant="outline" size="sm" @click="resetToCurrentDefaults">
          <RotateCcw data-icon="inline-start" /> {{ $t('administration.competitionMode.label.resetModeDefaults') }}
        </Button>
      </AlertDescription>
    </Alert>
    <template v-else>
      <FieldDescription v-if="isAwd">{{ $t('administration.awd.nextRoundNotice') }}</FieldDescription>
      <component :is="group.key ? DefinitionSection : 'div'" v-for="group in groups" :key="group.key"
        :title="group.title" :collapsible="group.collapsible" :default-open="group.defaultOpen">
      <FieldGroup>
        <Field v-for="field in group.fields" :key="field.key" :data-invalid="invalidFields.includes(field.key) || undefined">
          <FieldLabel>{{ field.label }}</FieldLabel>
          <component :is="ConfigFieldInput"
            :field="field"
            :model-value="values[field.key]"
            :disabled="readonly"
            @update:model-value="updateField(field.key, $event)"
          />
          <FieldDescription v-if="field.description">{{ field.description }}</FieldDescription>
        </Field>
      </FieldGroup>
      </component>
      <Alert v-if="invalidFields.length" variant="destructive"><AlertDescription>{{ $t('administration.awd.invalidField') }}</AlertDescription></Alert>
      <Field v-if="isAwd && !readonly">
        <FieldLabel for="awd-rule-change-reason">{{ $t('administration.awd.changeReason') }}</FieldLabel>
        <Textarea id="awd-rule-change-reason" v-model="changeReason" :maxlength="512" :disabled="saving" :aria-required="reasonRequired" />
        <FieldDescription v-if="reasonRequired">{{ $t('administration.awd.reasonRequired') }}</FieldDescription>
      </Field>
      <div v-if="!readonly" class="flex flex-wrap items-center gap-2">
        <Button type="button" variant="outline" :disabled="saving" @click="resetToCurrentDefaults">
          <RotateCcw data-icon="inline-start" /> {{ $t('administration.competitionMode.label.resetModeDefaults') }}
        </Button>
        <Button :disabled="saving || !dirty || !canSaveRules" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveConfiguration') }} </Button>
        <span v-if="!isAwd" class="text-xs text-muted-foreground">{{ $t('administration.competitionMode.description.saveConfigurationResettingApply') }}</span>
      </div>
    </template>
    <Dialog v-model:open="presetOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader><DialogTitle>{{ $t('administration.awd.presetTitle') }}</DialogTitle>
          <DialogDescription>{{ $t('administration.awd.presetNotice') }}</DialogDescription></DialogHeader>
        <ScrollSurface class="max-h-[50dvh]">
          <Table class="table-fixed"><TableHeader><TableRow><TableHead />
            <TableHead>{{ $t('administration.awd.before') }}</TableHead><TableHead>{{ $t('administration.awd.after') }}</TableHead>
          </TableRow></TableHeader><TableBody><TableRow v-for="change in presetChanges" :key="change.key">
            <TableCell class="whitespace-normal break-words">{{ change.label }}</TableCell><TableCell class="whitespace-normal break-words">{{ change.before }}</TableCell><TableCell class="whitespace-normal break-words">{{ change.after }}</TableCell>
          </TableRow></TableBody></Table>
        </ScrollSurface>
        <DialogFooter><Button variant="ghost" @click="closePreset">{{ $t('common.action.cancel') }}</Button>
          <Button @click="applyPreset">{{ $t('administration.awd.applyPreset') }}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
