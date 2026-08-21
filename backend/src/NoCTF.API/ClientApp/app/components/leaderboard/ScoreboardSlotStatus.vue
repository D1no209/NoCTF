<script setup lang="ts">
import { Flag, ShieldCheck } from '@lucide/vue'
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
const flagLabel = computed(() => {
  if (props.mode === 'Awd' || props.mode === 'Awdp')
    return signals.value.flagSucceeded ? translate('攻击成功') : translate('攻击未成功')
  if (props.mode === 'Koh')
    return signals.value.flagSucceeded ? translate('已取得控制权') : translate('尚未取得控制权')
  return signals.value.flagSucceeded ? translate('已解出') : translate('尚未解出')
})
const shieldLabel = computed(() =>
  signals.value.shieldSucceeded ? translate('防御成功') : translate('防御未成功'))
const accessibleLabel = computed(() => [
  signals.value.showFlag ? flagLabel.value : null,
  signals.value.showShield ? shieldLabel.value : null,
].filter(Boolean).join('，'))

function iconClass(succeeded: boolean, attempted: boolean): string {
  if (succeeded) return 'border-emerald-500/30 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400'
  if (attempted) return 'border-destructive/30 bg-destructive/10 text-destructive'
  return 'border-border bg-muted/50 text-muted-foreground/45'
}
</script>

<template>
  <span class="inline-flex items-center justify-center gap-1.5" :title="accessibleLabel">
    <span v-if="signals.showFlag" class="inline-flex size-8 items-center justify-center rounded-md border" :class="iconClass(signals.flagSucceeded, signals.flagAttempted)">
      <Flag class="size-4" aria-hidden="true" />
    </span>
    <span v-if="signals.showShield" class="inline-flex size-8 items-center justify-center rounded-md border" :class="iconClass(signals.shieldSucceeded, signals.shieldAttempted)">
      <ShieldCheck class="size-4" aria-hidden="true" />
    </span>
    <span class="sr-only">{{ accessibleLabel }}</span>
  </span>
</template>
