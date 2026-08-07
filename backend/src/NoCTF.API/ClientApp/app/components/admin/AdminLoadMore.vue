<script setup lang="ts">
defineProps<{ loading: boolean; hasMore: boolean }>()

const emit = defineEmits<{ load: [] }>()
</script>

<template>
  <div v-if="hasMore" class="flex justify-center py-4">
    <Button variant="outline" :disabled="loading" @click="emit('load')">
      <Spinner v-if="loading" data-icon="inline-start" />
      加载更多
    </Button>
  </div>
</template>
