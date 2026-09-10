<script setup lang="ts">
import { toRefs } from 'vue'
import type { NotificationsPageViewState } from '~/features/routes/useNotificationsPage'

const viewProps = defineProps<{ state: NotificationsPageViewState }>()
const { NotificationCenter } = toRefs(viewProps.state)
</script>

<template>
  <div class="h-full min-h-0">
    <component :is="NotificationCenter" />
  </div>
</template>
