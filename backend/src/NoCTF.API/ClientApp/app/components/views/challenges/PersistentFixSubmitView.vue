<script setup lang="ts">
import { toRefs } from 'vue'
import type { PersistentFixSubmitViewState } from '~/features/challenges/usePersistentFixSubmit'
const props = defineProps<{ state: PersistentFixSubmitViewState }>()
const { busy, canUpload, phaseLabel, unavailable, uploadKey, pending, error, status, selectFile, submit, refresh, timedOut, gameplayFactFailureCodeLabel } = toRefs(props.state)
</script>
<template>
  <section class="flex flex-col gap-4 py-5">
    <h3 class="text-sm font-semibold">{{ $t('challenges.fix.title') }}</h3>
    <FieldDescription>{{ $t('challenges.fix.persistentDescription') }}</FieldDescription>
    <FieldDescription v-if="unavailable">{{ $message(unavailable) }}</FieldDescription>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Field><FieldLabel>{{ $t('administration.label.patchArchive') }}</FieldLabel><FileUpload :key="uploadKey" accept=".tar.gz" :disabled="busy" :pending="pending" @change="selectFile" /></Field>
    <div class="flex flex-wrap items-center gap-3">
      <Button :disabled="!canUpload" @click="submit"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('challenges.fix.submit') }}</Button>
      <Badge v-if="busy" variant="secondary"><Spinner data-icon="inline-start" />{{ $t(phaseLabel) }}</Badge>
      <Badge v-else-if="status?.state === 'Completed'" :variant="status.result === 'Applied' ? 'default' : 'destructive'">{{ $t(status.result === 'Applied' ? 'challenges.fix.applied' : 'challenges.fix.failed') }}</Badge>
      <Badge v-else-if="status?.state === 'PlatformFailed'" variant="destructive">{{ $t('challenges.fix.platformFailure') }}</Badge>
      <Button v-if="timedOut || error" variant="outline" @click="refresh">{{ $t('common.label.refresh') }}</Button>
    </div>
    <p v-if="status?.failureCode" class="text-sm text-muted-foreground">{{ gameplayFactFailureCodeLabel(status.failureCode) }}</p>
  </section>
</template>
