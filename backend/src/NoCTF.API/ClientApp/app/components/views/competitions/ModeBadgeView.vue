<script setup lang="ts">
import { toRefs } from 'vue'
import type { ModeBadgeViewState } from '~/features/competitions/useModeBadge'

const viewProps = defineProps<{ state: ModeBadgeViewState }>()
const { label } = toRefs(viewProps.state)
</script>

<template>
  <Badge variant="outline" class="font-mono">{{ label }}</Badge>
</template>
