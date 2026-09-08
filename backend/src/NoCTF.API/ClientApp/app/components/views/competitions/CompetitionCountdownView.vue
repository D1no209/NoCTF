<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionCountdownViewState } from '~/features/competitions/useCompetitionCountdown'

const viewProps = defineProps<{ state: CompetitionCountdownViewState }>()
const { text } = toRefs(viewProps.state)
</script>

<template>
  <span v-if="text" class="tabular-nums">{{ text }}</span>
</template>
