<script setup lang="ts">
import { AlertCircle } from 'lucide-vue-next'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'

defineProps<{
  title: string
  message?: string
  retryLabel?: string
}>()

const emit = defineEmits<{
  retry: []
}>()
</script>

<template>
  <Card class="min-h-40 flex flex-col items-center justify-center gap-0 border-dashed border-destructive/30 bg-destructive/5 px-4 py-8 text-center">
    <AlertCircle class="size-8 text-destructive" />
    <h3 class="mt-3 text-sm font-medium text-destructive">{{ title }}</h3>
    <p v-if="message" class="mt-1 max-w-sm text-sm text-muted-foreground">{{ message }}</p>
    <Button v-if="retryLabel" variant="outline" size="sm" class="mt-4" @click="emit('retry')">
      {{ retryLabel }}
    </Button>
  </Card>
</template>
