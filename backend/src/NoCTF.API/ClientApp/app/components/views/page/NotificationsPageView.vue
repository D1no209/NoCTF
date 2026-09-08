<script setup lang="ts">
import { toRefs } from 'vue'
import type { NotificationsPageViewState } from '~/features/routes/useNotificationsPage'

const viewProps = defineProps<{ state: NotificationsPageViewState }>()
const { NotificationCenter } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto w-full max-w-6xl px-4 py-8">
    <component :is="NotificationCenter" />
  </div>
</template>
