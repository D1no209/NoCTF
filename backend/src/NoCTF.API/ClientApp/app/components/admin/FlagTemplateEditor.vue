<script setup lang="ts">
import type { FlagTemplateModel } from '~/utils/game-config'

withDefaults(defineProps<{
  template: FlagTemplateModel
  disabled?: boolean
}>(), {
  disabled: false,
})
</script>

<template>
  <FieldGroup>
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <FieldLabel>Flag 前缀</FieldLabel>
        <Input v-model="template.header" placeholder="flag" class="font-mono text-sm" :disabled="disabled" />
        <FieldDescription>最终 Flag 形如 前缀{正文}。</FieldDescription>
      </Field>
      <Field orientation="horizontal">
        <Switch id="flag-template-leet" v-model="template.leetLiteralText" :disabled="disabled" />
        <FieldLabel for="flag-template-leet" class="font-normal">对正文中的明文做 leet 混淆</FieldLabel>
      </Field>
    </div>
    <Field>
      <FieldLabel>正文模板</FieldLabel>
      <Input v-model="template.bodyTemplate" placeholder="[RANDOM:32]" class="font-mono text-sm" :disabled="disabled" />
      <FieldDescription>
        可用占位符:[GUID]、[TEAMID]、[CHALLENGEID]、[COMPETITIONCHALLENGEID]、[COMPETITIONID]、
        [TEAMHASH:n](8–64,默认 32)、[RANDOM:n](8–128,必填)。
      </FieldDescription>
    </Field>
  </FieldGroup>
</template>
