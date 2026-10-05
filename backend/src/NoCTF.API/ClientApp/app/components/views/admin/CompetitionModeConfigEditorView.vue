<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionModeConfigEditorViewState } from '~/features/admin/useCompetitionModeConfigEditor'

const viewProps = defineProps<{ state: CompetitionModeConfigEditorViewState }>()
const { RotateCcw, fields, values, parseFailed, updateField, resetToCurrentDefaults, dirty, save, ConfigFieldInput, readonly, loading, saving } = toRefs(viewProps.state)
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
      <FieldGroup>
        <Field v-for="field in fields" :key="field.key">
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
      <div v-if="!readonly" class="flex flex-wrap items-center gap-2">
        <Button type="button" variant="outline" :disabled="saving" @click="resetToCurrentDefaults">
          <RotateCcw data-icon="inline-start" /> {{ $t('administration.competitionMode.label.resetModeDefaults') }}
        </Button>
        <Button :disabled="saving || !dirty" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveConfiguration') }} </Button>
        <span class="text-xs text-muted-foreground">{{ $t('administration.competitionMode.description.saveConfigurationResettingApply') }}</span>
      </div>
    </template>
  </div>
</template>
