<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'
import { competitionStatusLabel } from '~/utils/labels'

const props = defineProps<{ status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol }>()

const label = computed(() => competitionStatusLabel(props.status))

const variant = computed(() => {
  switch (props.status) {
    case 'Running':
      return 'default' as const
    case 'Published':
    case 'Visible':
      return 'secondary' as const
    case 'Paused':
    case 'Finished':
      return 'outline' as const
    default:
      return 'secondary' as const
  }
})
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
