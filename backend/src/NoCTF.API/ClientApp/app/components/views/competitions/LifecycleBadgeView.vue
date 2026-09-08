<script setup lang="ts">
import { toRefs } from 'vue'
import type { LifecycleBadgeViewState } from '~/features/competitions/useLifecycleBadge'

const viewProps = defineProps<{ state: LifecycleBadgeViewState }>()
const { label, variant } = toRefs(viewProps.state)
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
