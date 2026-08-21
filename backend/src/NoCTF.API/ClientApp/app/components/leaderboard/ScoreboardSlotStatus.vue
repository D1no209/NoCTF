<script setup lang="ts">
import { Flag, Shield, ShieldCheck, ShieldX } from '@lucide/vue'
import type {
  NoCtfapiEndpointsCompetitionsGameModeProtocol,
  NoCtfapiEndpointsCompetitionsScoreboardSlotResponse,
} from '~/api'
import { scoreboardSlotSignals } from '~/utils/scoreboard'

const props = defineProps<{
  mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null
  slot: NoCtfapiEndpointsCompetitionsScoreboardSlotResponse
}>()

const signals = computed(() => scoreboardSlotSignals(props.slot, props.mode))
const combinedSuccess = computed(() =>
  signals.value.flagState === 'Succeeded' && signals.value.shieldState === 'Succeeded')
const combinedFailure = computed(() =>
  signals.value.flagState === 'Failed' && signals.value.shieldState === 'Failed')
const noOperation = computed(() =>
  signals.value.flagState === 'None'
  && (!signals.value.showShield || signals.value.shieldState === 'None'))
const flagLabel = computed(() => {
  if (signals.value.flagState === 'None') {
    if (props.mode === 'Awd' || props.mode === 'Awdp') return translate('本轮无攻击操作')
    if (props.mode === 'Koh') return translate('本轮无控制操作')
    return translate('本轮无解题操作')
  }
  if (props.mode === 'Awd' || props.mode === 'Awdp')
    return signals.value.flagSucceeded ? translate('攻击成功') : translate('攻击失败')
  if (props.mode === 'Koh')
    return signals.value.flagSucceeded ? translate('已取得控制权') : translate('尚未取得控制权')
  return signals.value.flagSucceeded ? translate('已解出') : translate('尚未解出')
})
const shieldLabel = computed(() => {
  if (signals.value.shieldState === 'None') return translate('本轮无防御操作')
  return signals.value.shieldSucceeded ? translate('防御成功') : translate('防御异常')
})
const accessibleLabel = computed(() => [
  combinedSuccess.value ? translate('攻击与防御均成功') : null,
  combinedFailure.value ? translate('攻击失败且防御异常') : null,
  !combinedSuccess.value && !combinedFailure.value && signals.value.showFlag ? flagLabel.value : null,
  !combinedSuccess.value && !combinedFailure.value && signals.value.showShield ? shieldLabel.value : null,
].filter(Boolean).join('，'))
</script>

<template>
  <span class="inline-flex min-w-12 items-center justify-center gap-2" :title="accessibleLabel">
    <ShieldCheck v-if="combinedSuccess" class="size-5 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
    <ShieldX v-else-if="combinedFailure" class="size-5 text-destructive" aria-hidden="true" />
    <span v-else-if="noOperation" class="font-mono text-sm text-muted-foreground/60" aria-hidden="true">-</span>
    <template v-else>
      <template v-if="signals.showFlag">
        <Flag v-if="signals.flagState === 'Succeeded'" class="size-4 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
        <Flag v-else-if="signals.flagState === 'Failed'" class="size-4 text-destructive" aria-hidden="true" />
      </template>
      <template v-if="signals.showShield">
        <Shield v-if="signals.shieldState === 'Succeeded'" class="size-4 text-primary" aria-hidden="true" />
        <ShieldX v-else-if="signals.shieldState === 'Failed'" class="size-4 text-destructive" aria-hidden="true" />
      </template>
    </template>
    <span class="sr-only">{{ accessibleLabel }}</span>
  </span>
</template>
