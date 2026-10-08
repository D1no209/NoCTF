<script setup lang="ts">
import { toRefs } from 'vue'
import type { SingleWriteUpSettingsState } from '~/features/writeups/useSingleWriteUpSettings'
const props = defineProps<{ state: SingleWriteUpSettingsState }>()
const { loading, pending, error, enabled, inherit, percent, deadlineHours, perChallenge, retainedExample, deadlineAt, canWrite, save, load } = toRefs(props.state)
</script>
<template>
  <section class="min-w-0 space-y-4">
    <h3 class="text-base font-semibold">{{ $t('challengeWriteUp.settings') }}</h3>
    <Skeleton v-if="loading" class="h-48 w-full" />
    <UiForm v-else validation="feature" class="space-y-4" @submit.prevent="save">
      <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
      <Field v-if="!perChallenge" orientation="horizontal"><Switch v-model="enabled" :disabled="!canWrite || pending" :aria-label="$t('challengeWriteUp.enabled')" /><FieldLabel>{{ $t('challengeWriteUp.enabled') }}</FieldLabel></Field>
      <Field v-if="perChallenge" orientation="horizontal"><Switch v-model="inherit" :disabled="!canWrite || pending" :aria-label="$t('challengeWriteUp.inherit')" /><FieldLabel>{{ $t('challengeWriteUp.inherit') }}</FieldLabel></Field>
      <Field><FieldLabel>{{ perChallenge ? $t('challengeWriteUp.overridePercent') : $t('challengeWriteUp.defaultPercent') }}</FieldLabel>
        <NumberInput v-model="percent" :min="0" :max="100" :step="1" :disabled="!canWrite || pending || (perChallenge && inherit)" />
        <FieldDescription>{{ $t('challengeWriteUp.example', { points: retainedExample }) }}</FieldDescription>
      </Field>
      <Field v-if="!perChallenge"><FieldLabel>{{ $t('challengeWriteUp.deadlineHours') }}</FieldLabel><NumberInput v-model="deadlineHours" :min="0" :max="8760" :step="1" :disabled="!canWrite || pending" /></Field>
      <p v-if="deadlineAt" class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.deadline', { time: formatDateTime(deadlineAt) }) }}</p>
      <FieldDescription>{{ $t('challengeWriteUp.settingsHint') }}</FieldDescription>
      <Button v-if="canWrite" type="submit" :disabled="pending"><Spinner v-if="pending" />{{ $t('challengeWriteUp.settingsSave') }}</Button>
      <Button v-if="error" type="button" variant="ghost" @click="load">{{ $t('common.label.retry') }}</Button>
    </UiForm>
  </section>
</template>
