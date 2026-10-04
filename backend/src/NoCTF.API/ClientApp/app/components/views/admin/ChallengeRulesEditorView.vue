<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeRulesEditorViewState } from '~/features/admin/useChallengeRulesEditor'

const viewProps = defineProps<{ state: ChallengeRulesEditorViewState }>()
const { fields, overridden, parseFailed, updateField, setOverride, displayedValue, dirty, save, ConfigFieldInput, readonly, loading, saving } = toRefs(viewProps.state)
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
      <FieldGroup>
        <Field v-for="field in fields" :key="field.key">
          <div class="flex items-center gap-2">
            <FieldLabel>{{ field.label }}</FieldLabel>
            <Switch
              size="sm"
              :model-value="overridden[field.key] ?? false"
              :disabled="readonly"
              @update:model-value="setOverride(field, $event === true)"
            />
            <span class="text-xs text-muted-foreground">{{ $t('administration.label.cover') }}</span>
            <span v-if="!(overridden[field.key] ?? false)" class="text-xs text-muted-foreground">{{ $t('administration.label.inheritContestDefaults') }}</span>
          </div>
          <component :is="ConfigFieldInput"
            :field="field"
            :model-value="displayedValue(field)"
            :disabled="readonly || !(overridden[field.key] ?? false)"
            @update:model-value="updateField(field.key, $event)"
          />
          <FieldDescription v-if="field.description">{{ field.description }}</FieldDescription>
        </Field>
      </FieldGroup>
      <div v-if="!readonly">
        <Button :disabled="saving || !dirty" @click="save">
          <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveConfiguration') }} </Button>
      </div>
    </template>
  </div>
</template>
