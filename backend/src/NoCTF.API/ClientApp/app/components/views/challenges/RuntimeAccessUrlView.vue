<script setup lang="ts">
import { toRefs } from 'vue'
import type { RuntimeAccessUrlViewState } from '~/features/challenges/useRuntimeAccessUrl'

const viewProps = defineProps<{ state: RuntimeAccessUrlViewState }>()
const { Copy, entries, copy } = toRefs(viewProps.state)
</script>

<template>
  <div class="grid min-w-0 gap-1.5">
    <div v-for="entry in entries" :key="`${entry.kind}:${entry.address}`" class="flex min-w-0 items-center gap-2">
      <Badge variant="outline" class="shrink-0 font-mono text-[10px] uppercase">
        {{ entry.kind === 'direct' ? $t('runtime.label.directAddress') : $t('runtime.label.wsrxAddress') }}
      </Badge>
      <ExternalLink
        v-if="entry.clickable"
        :href="entry.address"
        target="_blank"
        rel="noopener"
        class="min-w-0 flex-1 break-all font-mono text-sm text-primary underline"
      >
        {{ entry.address }}
      </ExternalLink>
      <span v-else class="min-w-0 flex-1 select-text break-all font-mono text-sm">{{ entry.address }}</span>
      <Button
        type="button"
        size="icon"
        variant="ghost"
        class="size-7 shrink-0"
        :aria-label="$t('common.action.copy')"
        @click="copy(entry.address)"
      >
        <Copy class="size-4" aria-hidden="true" />
      </Button>
    </div>
  </div>
</template>
