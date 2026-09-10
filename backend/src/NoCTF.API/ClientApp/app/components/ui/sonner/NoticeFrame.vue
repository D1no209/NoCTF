<script setup lang="ts">
import { X } from '@lucide/vue'
import NoticeIcon from './NoticeIcon.vue'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'
import ActionButton from '../action-button/ActionButton.vue'
defineProps<{ destructive?: boolean }>()
const emit = defineEmits<{ dismiss: [] }>()
</script>

<template>
  <div class="notice-frame" :data-tone="destructive ? 'error' : 'info'" :role="destructive ? 'alert' : 'status'">
    <NoticeIcon :tone="destructive ? 'error' : 'info'" />
    <ScrollSurface axis="y" class="notice-message"><slot /></ScrollSurface>
    <ActionButton class="notice-dismiss" :aria-label="$t('ui.close')" @click="emit('dismiss')"><X class="size-4" /></ActionButton>
  </div>
</template>
