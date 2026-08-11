<script setup lang="ts">
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '~/api'
import type { BadgeVariants } from '~/components/ui/badge'

const props = defineProps<{ status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol | null }>()

const label = computed(() => enumLabel(CompetitionStatusLabel, props.status))
const variant = computed<BadgeVariants['variant']>(() => {
  switch (props.status) {
    case 'Running': return 'default'
    case 'Paused': return 'secondary'
    case 'Finished': return 'outline'
    case 'Published': return 'secondary'
    default: return 'outline'
  }
})
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
