<script setup lang="ts">
import { Copy } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { isRuntimeUrlClickable } from '~/utils/runtime-url'

const props = defineProps<{ url: string }>()
const clickable = computed(() => isRuntimeUrlClickable(props.url))

async function copy(): Promise<void> {
  try {
    await navigator.clipboard.writeText(props.url)
    toast.success(translate("已复制到剪贴板"))
  }
  catch {
    toast.error(translate("复制失败,请手动选择复制"))
  }
}
</script>

<template>
  <div class="flex min-w-0 items-center gap-1">
    <a
      v-if="clickable"
      :href="url"
      target="_blank"
      rel="noopener"
      class="min-w-0 break-all font-mono text-sm text-primary underline"
    >
      {{ url }}
    </a>
    <template v-else>
      <span class="min-w-0 flex-1 select-text break-all font-mono text-sm">{{ url }}</span>
      <Button
        type="button"
        size="icon"
        variant="ghost"
        class="size-7 shrink-0"
        :aria-label="$t('复制')"
        @click="copy"
      >
        <Copy class="size-4" aria-hidden="true" />
      </Button>
    </template>
  </div>
</template>
