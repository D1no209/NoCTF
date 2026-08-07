<script setup lang="ts">
import type { BadgeVariants } from '~/components/ui/badge'

const props = defineProps<{ status?: number | null }>()

const label = computed(() => enumLabel(CompetitionStatusLabel, props.status))
const variant = computed<BadgeVariants['variant']>(() => {
  switch (props.status) {
    case 3: return 'default'
    case 4: return 'secondary'
    case 5: return 'outline'
    case 2: return 'secondary'
    default: return 'outline'
  }
})
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
