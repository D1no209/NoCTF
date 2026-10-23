<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeRulesEditorViewState } from '~/features/admin/useChallengeRulesEditor'

const viewProps = defineProps<{ state: ChallengeRulesEditorViewState }>()
const { storeRows, storeRulesValid, updateStoreRule, presetControlled, isAwd, changeReason, reasonRequired, invalidFields, canSaveRules, groups, DefinitionSection, presetOpen, presetChanges, applyPreset, closePreset, overridden, parseFailed, updateField, setOverride, displayedValue, dirty, save, ConfigFieldInput, readonly, loading, saving } = toRefs(viewProps.state)
const { scoreCurveSource, hasScorePreset } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-3">
    <div class="flex items-center gap-2 text-xs text-muted-foreground">
      <span v-if="dirty">{{ $t('administration.label.thereUnsavedChanges') }}</span>
    </div>
    <Skeleton v-if="loading" class="h-64 w-full" />
    <Alert v-else-if="parseFailed" variant="destructive">
      <AlertDescription>{{ $t('administration.error.typedConfigurationUnavailable') }}</AlertDescription>
    </Alert>
    <template v-else>
      <FieldDescription v-if="isAwd">{{ $t('administration.awd.nextRoundNotice') }}</FieldDescription>
      <component :is="group.key ? DefinitionSection : 'div'" v-for="group in groups" :key="group.key"
        :title="group.title" :collapsible="group.collapsible" :default-open="group.defaultOpen">
      <FieldGroup>
        <Field v-for="field in group.fields" :key="field.key" :data-invalid="invalidFields.includes(field.key) || undefined">
          <div class="flex items-center gap-2">
            <FieldLabel>{{ field.label }}</FieldLabel>
            <Switch
              size="sm"
              :model-value="overridden[field.key] ?? false"
              :disabled="readonly"
              @update:model-value="setOverride(field, $event === true)"
            />
            <span class="text-xs text-muted-foreground">{{ $t(field.key === 'scoreCurve' && hasScorePreset ? 'scorePresets.override' : 'administration.label.cover') }}</span>
            <span v-if="isAwd && presetControlled(field)" class="text-xs text-muted-foreground">{{ $t('administration.awd.presetControlled') }}</span>
            <span v-else-if="field.key !== 'scoreCurve' && !(overridden[field.key] ?? false)" class="text-xs text-muted-foreground">{{ $t('administration.label.inheritContestDefaults') }}</span>
          </div>
          <FieldDescription v-if="field.key === 'scoreCurve'">{{ scoreCurveSource }}</FieldDescription>
          <component :is="ConfigFieldInput"
            :field="field"
            :model-value="displayedValue(field)"
            :disabled="readonly || !(overridden[field.key] ?? false)"
            @update:model-value="updateField(field.key, $event)"
          />
          <FieldDescription v-if="field.description">{{ field.description }}</FieldDescription>
        </Field>
      </FieldGroup>
      </component>
      <DefinitionSection v-if="isAwd && storeRows.length" :title="$t('administration.awd.stores')" :default-open="true">
        <FieldDescription>{{ $t('administration.awd.storeOverridesHint') }}</FieldDescription>
        <div v-for="store in storeRows" :key="store.storeId" class="grid items-end gap-3 sm:grid-cols-3">
          <div class="flex flex-col gap-1"><span class="text-sm font-medium">{{ store.name }}</span><span class="text-xs text-muted-foreground">{{ $t('administration.awd.storeShare', { share: store.share, flags: store.effectiveFlags }) }}</span></div>
          <Field><FieldLabel>{{ $t('administration.awd.storeWeight') }}</FieldLabel>
            <NullableNumberInput :model-value="store.weightOverride" :min="0.000001" :max="100" step="any" :placeholder="$t('administration.label.default')" :disabled="readonly" @update:model-value="updateStoreRule(store.storeId, 'weight', $event)" />
          </Field>
          <Field><FieldLabel>{{ $t('administration.awd.field.flagsPerStore') }}</FieldLabel>
            <NullableNumberInput :model-value="store.flagsOverride" :min="1" :max="64" :placeholder="$t('administration.label.inheritContestDefaults')" :disabled="readonly" @update:model-value="updateStoreRule(store.storeId, 'flagsPerIssue', $event)" />
          </Field>
        </div>
      </DefinitionSection>
      <Alert v-if="invalidFields.length || !storeRulesValid" variant="destructive"><AlertDescription>{{ $t('administration.awd.invalidField') }}</AlertDescription></Alert>
      <Field v-if="isAwd && !readonly">
        <FieldLabel for="awd-rule-change-reason">{{ $t('administration.awd.changeReason') }}</FieldLabel>
        <Textarea id="awd-rule-change-reason" v-model="changeReason" :maxlength="512" :disabled="saving" :aria-required="reasonRequired" />
        <FieldDescription v-if="reasonRequired">{{ $t('administration.awd.reasonRequired') }}</FieldDescription>
      </Field>
      <div v-if="!readonly">
        <Button :disabled="saving || !dirty || !canSaveRules" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveConfiguration') }} </Button>
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
