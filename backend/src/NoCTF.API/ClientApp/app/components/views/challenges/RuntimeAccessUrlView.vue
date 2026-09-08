<script setup lang="ts">
import { toRefs } from 'vue'
import type { RuntimeAccessUrlViewState } from '~/features/challenges/useRuntimeAccessUrl'

const viewProps = defineProps<{ state: RuntimeAccessUrlViewState }>()
const { Copy, clickable, copy, url } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex min-w-0 items-center gap-1">
    <ExternalLink
      v-if="clickable"
      :href="url"
      target="_blank"
      rel="noopener"
      class="min-w-0 break-all font-mono text-sm text-primary underline"
    >
      {{ url }}
    </ExternalLink>
    <template v-else>
      <span class="min-w-0 flex-1 select-text break-all font-mono text-sm">{{ url }}</span>
      <Button
        type="button"
        size="icon"
        variant="ghost"
        class="size-7 shrink-0"
        :aria-label="$t('ui.copy')"
        @click="copy"
      >
        <Copy class="size-4" aria-hidden="true" />
      </Button>
    </template>
  </div>
</template>
