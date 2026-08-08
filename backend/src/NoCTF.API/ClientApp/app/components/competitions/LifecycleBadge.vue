<script setup lang="ts">
import { CompetitionStatus, competitionStatusLabel } from '~/utils/labels'

const props = defineProps<{ status?: string }>()

const label = computed(() => competitionStatusLabel(props.status))

const variant = computed(() => {
  switch (props.status) {
    case CompetitionStatus.Running:
      return 'default' as const
    case CompetitionStatus.Published:
    case CompetitionStatus.Visible:
      return 'secondary' as const
    case CompetitionStatus.Paused:
    case CompetitionStatus.Finished:
      return 'outline' as const
    default:
      return 'secondary' as const
  }
})
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
