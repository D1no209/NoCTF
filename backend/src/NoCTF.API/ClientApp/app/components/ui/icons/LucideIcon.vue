<script setup lang="ts">
import { computed } from 'vue'
import { lucideIcon } from '../../../lib/lucide-icons'
const props = defineProps<{ name: string }>()
const icon = computed(() => lucideIcon(props.name))
</script>

<template>
  <component :is="icon" data-slot="lucide-icon" :data-icon="name" aria-hidden="true" class="size-5" />
</template>
