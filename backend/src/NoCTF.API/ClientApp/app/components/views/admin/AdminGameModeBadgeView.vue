<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminGameModeBadgeViewState } from '~/features/admin/useAdminGameModeBadge'

const viewProps = defineProps<{ state: AdminGameModeBadgeViewState }>()
const { label } = toRefs(viewProps.state)
</script>

<template>
  <Badge variant="secondary">{{ label }}</Badge>
</template>
