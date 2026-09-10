<script setup lang="ts">
import { keepNoticeInteractive } from '../sonner/notice-events'
import type { AlertDialogContentEmits, AlertDialogContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import {
  AlertDialogContent as RekaAlertDialogContent,
  AlertDialogOverlay,
  AlertDialogPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { cn } from '~/lib/utils'
import { Card } from '~/components/ui/card'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<AlertDialogContentProps & {
    class?: HTMLAttributes['class']
    size?: 'default' | 'sm'
  }>(),
  {
    size: 'default',
  },
)
const emits = defineEmits<AlertDialogContentEmits>()

const delegatedProps = reactiveOmit(props, 'class', 'size', 'as', 'asChild')

const forwarded = useForwardPropsEmits(delegatedProps, emits)
</script>

<template>
  <AlertDialogPortal>
    <AlertDialogOverlay
      data-slot="alert-dialog-overlay"
      class="data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0 bg-black/10 duration-100 supports-backdrop-filter:backdrop-blur-xs fixed inset-0 z-50"
    />
    <Card
      :as="RekaAlertDialogContent"
      slot-name="alert-dialog-content"
      data-modal-scroll-lock
      @interact-outside="keepNoticeInteractive"
      :size="size"
      v-bind="{ ...$attrs, ...forwarded }"
      :class="
        cn(
          'data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0 data-closed:zoom-out-95 data-open:zoom-in-95 text-card-foreground gap-4 p-4 duration-100 data-[size=default]:max-w-xs data-[size=sm]:max-w-xs data-[size=default]:sm:max-w-sm group/alert-dialog-content fixed top-1/2 left-1/2 z-50 grid w-full -translate-x-1/2 -translate-y-1/2 outline-none',
          props.class,
        )
      "
    >
      <slot />
    </Card>
  </AlertDialogPortal>
</template>
