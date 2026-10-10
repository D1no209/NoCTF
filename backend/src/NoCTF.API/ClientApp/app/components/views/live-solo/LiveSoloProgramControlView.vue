<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloProgramControlState } from '~/features/live-solo/useLiveSoloProgramControl'
const props=defineProps<{state:LiveSoloProgramControlState}>()
const {program,historyRows,error,busy,reason,decision,actionKey,stateKey,actions,load,open,setOpen,confirm}=toRefs(props.state)
</script>
<template>
  <section class="flex flex-col gap-3" :aria-label="$t('liveSolo.programControl.title')">
    <div class="flex flex-wrap items-center gap-3"><h2 class="font-semibold">{{ $t('liveSolo.programControl.title') }}</h2><Badge v-if="program?.stalled" variant="destructive">{{ $t('liveSolo.programControl.stalled') }}</Badge><Badge v-else-if="program" variant="secondary">{{ $t(stateKey) }}</Badge><Button variant="ghost" size="sm" :disabled="busy || decision !== null" @click="load">{{ $t('common.label.refresh') }}</Button></div>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <p v-if="program" class="text-sm text-muted-foreground">{{ $t('liveSolo.programControl.lastVideo',{time:formatDateTime(program.lastFragmentImportedAt)}) }}</p>
    <div class="flex flex-wrap gap-2"><Button v-for="item in actions" :key="item.action" size="sm" variant="outline" :disabled="busy || decision !== null" @click="open(item.action)">{{ $t(item.key) }}</Button></div>
    <DefinitionSection v-if="historyRows.length" :title="$t('liveSolo.programControl.history')"><div v-for="row in historyRows" :key="row.entry.id" class="flex flex-col gap-1 py-2"><p class="text-sm text-muted-foreground">{{ formatDateTime(row.entry.occurredAt) }} · {{ $t(row.key) }}</p><p class="whitespace-pre-wrap break-words text-sm">{{ row.entry.reason }}</p></div></DefinitionSection>
    <AlertDialog :open="decision !== null" @update:open="setOpen"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t(actionKey) }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.programControl.confirm') }}</AlertDialogDescription></AlertDialogHeader><Field><FieldLabel for="programme-recovery-reason">{{ $t('liveSolo.judge.reason') }}</FieldLabel><Textarea id="programme-recovery-reason" v-model="reason" :maxlength="4000" :disabled="busy" /></Field><AlertDialogFooter><AlertDialogCancel :disabled="busy">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button :disabled="busy || !reason.trim()" @click="confirm">{{ $t(actionKey) }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </section>
</template>
