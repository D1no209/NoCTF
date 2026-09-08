<script setup lang="ts">
import { toRefs } from 'vue'
import type { GameModeBadgeViewState } from '~/features/admin/useGameModeBadge'

const viewProps = defineProps<{ state: GameModeBadgeViewState }>()
const { label } = toRefs(viewProps.state)
</script>

<template>
  <Badge variant="secondary">{{ label }}</Badge>
</template>
