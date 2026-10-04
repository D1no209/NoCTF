<script setup lang="ts">
import { toRefs } from 'vue'
import type { FlagTemplateEditorViewState } from '~/features/admin/useFlagTemplateEditor'

const viewProps = defineProps<{ state: FlagTemplateEditorViewState }>()
const { template, disabled } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <FieldLabel>{{ $t('administration.label.flagPrefix') }}</FieldLabel>
        <Input v-model="template.header" :placeholder="$t('administration.label.flag.templateEditorView')" class="font-mono text-sm" :disabled="disabled" />
        <FieldDescription>{{ $t('administration.flagTemplate.description.finalFlagFormPrefix') }}</FieldDescription>
      </Field>
      <Field orientation="horizontal">
        <Switch id="flag-template-leet" v-model="template.leetLiteralText" :disabled="disabled" />
        <FieldLabel for="flag-template-leet" class="font-normal">{{ $t('administration.flagTemplate.description.leetObfuscatesPlainText') }}</FieldLabel>
      </Field>
    </div>
    <Field>
      <FieldLabel>{{ $t('administration.label.textTemplate') }}</FieldLabel>
      <Input v-model="template.bodyTemplate" :placeholder="$t('administration.label.guid')" class="font-mono text-sm" :disabled="disabled" />
      <FieldDescription> {{ $t('administration.flagTemplate.description.availablePlaceholdersGuidTeamid') }} </FieldDescription>
    </Field>
  </FieldGroup>
</template>
