<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloFixSubmitViewState } from '~/features/live-solo/useLiveSoloFixSubmit'
const props = defineProps<{ state: LiveSoloFixSubmitViewState }>()
const { target, status, pending, error, uploadKey, ready, canPrepare, canUpload, unavailable, factPending, result,
  phaseLabel, prepare, selectFile, submit, retry, timedOut } = toRefs(props.state)
</script>
<template>
  <section class="flex min-w-0 flex-col gap-3 py-3">
    <h4 class="font-semibold">{{ $t('challenges.fix.title') }}</h4>
    <FieldDescription>{{ $t('liveSolo.fix.description') }}</FieldDescription>
    <FieldDescription v-if="unavailable">{{ $message(unavailable) }}</FieldDescription>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <div class="flex flex-wrap items-center gap-3">
      <Button :disabled="!canPrepare" variant="outline" @click="prepare"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('liveSolo.fix.prepare') }}</Button>
      <Badge v-if="target?.state" variant="secondary">{{ runtimeStateLabel(target.state) }}</Badge>
    </div>
    <template v-if="ready">
      <Field><FieldLabel>{{ $t('administration.label.patchArchive') }}</FieldLabel><FileUpload :key="uploadKey" accept=".tar.gz" :disabled="pending" :pending="pending" @change="selectFile" /></Field>
      <Button :disabled="!canUpload" @click="submit">{{ $t('challenges.fix.submit') }}</Button>
    </template>
    <p v-if="target?.admissionSequence" class="text-sm" role="status">{{ $t('liveSolo.flagAccepted', { sequence: target.admissionSequence }) }}</p>
    <Badge v-if="factPending" variant="secondary"><Spinner data-icon="inline-start" />{{ $t(phaseLabel) }}</Badge>
    <p v-else-if="status && result" class="text-sm" role="status">{{ $t('liveSolo.flagResult', { sequence: target?.admissionSequence ?? 0, result }) }}</p>
    <Button v-if="timedOut || error" size="sm" variant="outline" @click="retry">{{ $t('common.label.retry') }}</Button>
  </section>
</template>
