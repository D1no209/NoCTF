<script setup lang="ts">
import NotificationNoticeContentView from '~/components/views/layout/NotificationNoticeContent.vue'

defineProps<{
  title: string
  body?: string | null
  href: string
  actionLabel: string
}>()
</script>

<template>
  <NotificationNoticeContentView :title="title" :body="body" :href="href" :action-label="actionLabel" />
</template>
