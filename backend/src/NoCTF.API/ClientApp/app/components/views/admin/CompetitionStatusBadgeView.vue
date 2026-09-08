<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionStatusBadgeViewState } from '~/features/admin/useCompetitionStatusBadge'

const viewProps = defineProps<{ state: CompetitionStatusBadgeViewState }>()
const { label, variant } = toRefs(viewProps.state)
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
