<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminDateTimeViewState } from '~/features/admin/useAdminDateTime'

const viewProps = defineProps<{ state: AdminDateTimeViewState }>()
const { formatted, value } = toRefs(viewProps.state)
</script>

<template>
  <time :datetime="value ?? undefined" :title="value ?? undefined" class="font-mono whitespace-nowrap tabular-nums">{{ formatted }}</time>
</template>
