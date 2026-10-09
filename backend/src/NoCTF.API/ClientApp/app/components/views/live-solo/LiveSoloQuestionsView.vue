<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloQuestionsState } from '~/features/live-solo/useLiveSoloQuestions'
const props = defineProps<{ state: LiveSoloQuestionsState }>()
const { loading, error, current, selected, options, select, input, inputPending, submit, mayStartRuntime, feedback, attachments, runtime,
  resourceError, downloading, download, runtimeBusy, environment, RuntimeAccess, canSubmit, statusTimedOut, retryStatus, dockSelector } = toRefs(props.state)
</script>
<template>
  <section class="flex min-w-0 flex-col gap-4">
    <h2 class="font-semibold">{{ $t('liveSolo.questions') }}</h2>
    <Alert v-if="error || resourceError" variant="destructive"><AlertDescription>{{ $message(error || resourceError) }}</AlertDescription></Alert>
    <Skeleton v-if="loading" class="h-40" />
    <Empty v-else-if="!options.length"><EmptyHeader><EmptyTitle>{{ $t('liveSolo.noQuestions') }}</EmptyTitle></EmptyHeader></Empty>
    <template v-else><ChoicePicker :items="options" :model-value="selected" :label="$t('liveSolo.questions')" :search-label="$t('liveSolo.questionSearch')" :empty-label="$t('liveSolo.noQuestions')" @update:model-value="select" />
      <template v-if="current"><h3 class="text-lg font-bold italic text-primary">{{ current.title }}</h3><div class="flex flex-wrap gap-2"><Badge v-for="tag in current.tags" :key="tag" variant="secondary">{{ tag }}</Badge></div><MarkdownContent :source="current.description ?? ''" class="max-w-[75ch]" />
        <div class="flex flex-wrap items-start gap-6"><div v-if="runtime" class="flex min-w-0 flex-1 flex-col gap-3"><Badge variant="secondary">{{ runtimeStateLabel(runtime.state) }}</Badge><component :is="RuntimeAccess" v-for="(access, index) in runtime.accesses" :key="index" :access="access" /><div v-if="canSubmit" class="flex flex-wrap gap-2"><Button v-if="mayStartRuntime" :disabled="runtimeBusy" size="sm" variant="outline" @click="environment('Start')">{{ $t('liveSolo.runtime.start') }}</Button><Button v-else :disabled="runtimeBusy" size="sm" variant="outline" @click="environment('Reset')">{{ $t('liveSolo.runtime.reset') }}</Button><Button :disabled="runtimeBusy" size="sm" variant="outline" @click="environment('Stop')">{{ $t('liveSolo.runtime.stop') }}</Button></div></div>
          <div class="flex flex-wrap gap-2"><Button v-for="file in attachments?.items" :key="file.id" :disabled="downloading" variant="outline" @click="download(file.id)">{{ file.fileName }}</Button><Button v-if="attachments?.deliveryPolicy === 'RandomOnePerTeam'" :disabled="downloading" variant="outline" @click="download()">{{ $t('liveSolo.attachment') }}</Button></div></div>
        <Teleport defer :to="dockSelector"><TerminalCommand v-model="input" :label="$t('liveSolo.flag')" :placeholder="$t('liveSolo.flag')" :disabled="!canSubmit" :pending="inputPending" @submit="submit" /><p v-if="feedback" class="px-2 text-sm" role="status">{{ $message(feedback) }}</p><Button v-if="statusTimedOut" size="sm" variant="outline" @click="retryStatus">{{ $t('common.label.retry') }}</Button></Teleport>
      </template>
    </template>
  </section>
</template>
