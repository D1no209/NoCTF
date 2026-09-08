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
        <FieldLabel>{{ $t('ui.flagPrefix') }}</FieldLabel>
        <Input v-model="template.header" :placeholder="$t('ui.flag2')" class="font-mono text-sm" :disabled="disabled" />
        <FieldDescription>{{ $t('ui.theFinalFlagIsInTheFormOfPrefix') }}</FieldDescription>
      </Field>
      <Field orientation="horizontal">
        <Switch id="flag-template-leet" v-model="template.leetLiteralText" :disabled="disabled" />
        <FieldLabel for="flag-template-leet" class="font-normal">{{ $t('ui.leetObfuscatesThePlainTextInTheText') }}</FieldLabel>
      </Field>
    </div>
    <Field>
      <FieldLabel>{{ $t('ui.textTemplate') }}</FieldLabel>
      <Input v-model="template.bodyTemplate" :placeholder="$t('ui.guid')" class="font-mono text-sm" :disabled="disabled" />
      <FieldDescription> {{ $t('ui.availablePlaceholdersGuidTeamidChallengeidCompetitionchallengeidCompetitionidTeamhashN8') }} </FieldDescription>
    </Field>
  </FieldGroup>
</template>
