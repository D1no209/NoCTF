<script setup lang="ts">
import { toRefs } from 'vue'
import type { SingleWriteUpSettingsState } from '~/features/writeups/useSingleWriteUpSettings'
const props = defineProps<{ state: SingleWriteUpSettingsState }>()
const { formId, loading, pending, error, enabled, inherit, percent, deadlineHours, perChallenge, retainedExample, deadlineAt, canWrite, save, load } = toRefs(props.state)
</script>
<template>
  <section class="flex min-w-0 flex-col gap-5" :aria-labelledby="`${formId}-title`">
    <h3 :id="`${formId}-title`" class="text-base font-medium leading-snug">{{ $t('challengeWriteUp.settings') }}</h3>
    <Skeleton v-if="loading" class="h-48 w-full" />
    <UiForm v-else validation="feature" class="flex flex-col gap-5" @submit.prevent="save">
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <FieldGroup>
        <Field v-if="!perChallenge" orientation="horizontal">
          <Switch :id="`${formId}-enabled`" v-model="enabled" :disabled="!canWrite || pending" />
          <FieldLabel :for="`${formId}-enabled`" class="font-normal">{{ $t('challengeWriteUp.enabled') }}</FieldLabel>
        </Field>
        <Field v-if="perChallenge" orientation="horizontal">
          <Switch :id="`${formId}-inherit`" v-model="inherit" :disabled="!canWrite || pending" />
          <FieldLabel :for="`${formId}-inherit`" class="font-normal">{{ $t('challengeWriteUp.inherit') }}</FieldLabel>
        </Field>
        <Field orientation="responsive">
          <FieldContent>
            <FieldLabel :for="`${formId}-percent`">{{ perChallenge ? $t('challengeWriteUp.overridePercent') : $t('challengeWriteUp.defaultPercent') }}</FieldLabel>
            <FieldDescription>{{ $t('challengeWriteUp.example', { points: retainedExample }) }}</FieldDescription>
          </FieldContent>
          <NumberInput :id="`${formId}-percent`" v-model="percent" class="@md/field-group:w-48 @md/field-group:shrink-0" :min="0" :max="100" :step="1" :disabled="!canWrite || pending || (perChallenge && inherit)" />
        </Field>
        <Field v-if="!perChallenge" orientation="responsive">
          <FieldContent><FieldLabel :for="`${formId}-deadline`">{{ $t('challengeWriteUp.deadlineHours') }}</FieldLabel></FieldContent>
          <NumberInput :id="`${formId}-deadline`" v-model="deadlineHours" class="@md/field-group:w-48 @md/field-group:shrink-0" :min="0" :max="8760" :step="1" :disabled="!canWrite || pending" />
        </Field>
      </FieldGroup>
      <div class="flex max-w-prose flex-col gap-2">
        <FieldDescription v-if="deadlineAt">{{ $t('challengeWriteUp.deadline', { time: formatDateTime(deadlineAt) }) }}</FieldDescription>
        <FieldDescription>{{ $t('challengeWriteUp.settingsHint') }}</FieldDescription>
      </div>
      <div v-if="canWrite || error" class="flex flex-wrap items-center justify-end gap-2">
        <Button v-if="error" type="button" variant="ghost" :disabled="pending" @click="load">{{ $t('common.label.retry') }}</Button>
        <Button v-if="canWrite" type="submit" :disabled="pending"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('challengeWriteUp.settingsSave') }}</Button>
      </div>
    </UiForm>
  </section>
</template>
