<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { platformApi } from '@/api/noctf'
import { queryKeys } from '@/api/queryKeys'
import { cn } from '@/lib/utils'

const props = defineProps<{
  class?: HTMLAttributes['class']
}>()

const loadFailed = ref(false)
const { data: configuration } = useQuery({
  queryKey: queryKeys.platformConfiguration,
  queryFn: platformApi.configuration,
  staleTime: 5 * 60 * 1000,
})
const logoSource = computed(() =>
  !loadFailed.value && configuration.value?.logoUrl
    ? configuration.value.logoUrl
    : '/logo.png')

watch(() => configuration.value?.logoUrl, () => {
  loadFailed.value = false
})

watch(() => configuration.value?.name, (name) => {
  if (name)
    document.title = name
}, { immediate: true })
</script>

<template>
  <img
    :src="logoSource"
    :alt="configuration?.name || 'NoCTF'"
    draggable="false"
    class="block w-auto max-h-full object-contain [image-rendering:pixelated]"
    :class="cn(props.class)"
    @error="loadFailed = true"
  >
</template>
