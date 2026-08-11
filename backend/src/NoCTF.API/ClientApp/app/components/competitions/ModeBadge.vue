<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsGameModeProtocol } from '~/api'
import { gameModeLabel } from '~/utils/labels'

const props = defineProps<{ mode?: NoCtfapiEndpointsCompetitionsGameModeProtocol }>()

const label = computed(() => gameModeLabel(props.mode))
</script>

<template>
  <Badge variant="outline" class="font-mono">{{ label }}</Badge>
</template>
