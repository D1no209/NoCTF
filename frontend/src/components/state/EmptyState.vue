<script setup lang="ts">
import type { Component } from 'vue'
import { Inbox } from 'lucide-vue-next'
import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'

withDefaults(defineProps<{
  title: string
  description?: string
  actionLabel?: string
  icon?: Component
}>(), {
  icon: Inbox,
})

const emit = defineEmits<{
  action: []
}>()
</script>

<template>
  <Card class="min-h-40 flex flex-col items-center justify-center gap-0 border-dashed bg-muted px-4 py-8 text-center">
    <component :is="icon" class="size-8 text-muted-foreground" />
    <h3 class="mt-3 text-sm font-medium">{{ title }}</h3>
    <p v-if="description" class="mt-1 max-w-sm text-sm text-muted-foreground">{{ description }}</p>
    <Button v-if="actionLabel" variant="outline" size="sm" class="mt-4" @click="emit('action')">
      {{ actionLabel }}
    </Button>
  </Card>
</template>
