<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminDateTimeViewState } from '~/features/admin/useAdminDateTime'

const viewProps = defineProps<{ state: AdminDateTimeViewState }>()
const { formatted, value } = toRefs(viewProps.state)
</script>

<template>
  <Hint :content="value ?? undefined" ><time :datetime="value ?? undefined"  class="font-mono whitespace-nowrap tabular-nums">{{ formatted }}</time></Hint>
</template>
