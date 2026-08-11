<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '~/api'

const props = defineProps<{ mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol | null }>()

const label = computed(() => enumLabel(GameModeLabel, props.mode))
</script>

<template>
  <Badge variant="secondary">{{ label }}</Badge>
</template>
