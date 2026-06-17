<script setup lang="ts">
import type { Component } from 'vue'
import { Card, CardContent } from '@/components/ui/card'

withDefaults(defineProps<{
  label: string
  value: string | number
  description?: string
  icon?: Component
  tone?: 'default' | 'success' | 'warning' | 'danger'
}>(), {
  tone: 'default',
})
</script>

<template>
  <Card class="py-4">
    <CardContent class="flex items-center justify-between gap-3 px-4">
      <div class="min-w-0">
        <p class="text-xs font-medium uppercase tracking-wide text-muted-foreground">{{ label }}</p>
        <p class="mt-1 truncate text-2xl font-semibold tabular-nums">{{ value }}</p>
        <p v-if="description" class="mt-1 truncate text-xs text-muted-foreground">{{ description }}</p>
      </div>
      <component
        :is="icon"
        v-if="icon"
        class="size-5 shrink-0"
        :class="{
          'text-muted-foreground': tone === 'default',
          'text-green-600': tone === 'success',
          'text-yellow-600': tone === 'warning',
          'text-destructive': tone === 'danger',
        }"
      />
    </CardContent>
  </Card>
</template>
