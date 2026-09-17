<script setup lang="ts">
import { keepNoticeInteractive } from '../sonner/notice-events'
import type { DialogContentEmits, DialogContentProps, PointerDownOutsideEvent } from 'reka-ui'

import type { HTMLAttributes } from 'vue'
import { XIcon } from '@lucide/vue'
import { reactiveOmit } from '@vueuse/core'
import {
  DialogClose,
  DialogContent as RekaDialogContent,
  DialogPortal,
  useForwardPropsEmits,
} from 'reka-ui'
import { cn } from '~/lib/utils'
import { Card } from '~/components/ui/card'
import DialogOverlay from '~/components/ui/dialog/DialogOverlay.vue'

defineOptions({
  inheritAttrs: false,
})

const props = defineProps<DialogContentProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<DialogContentEmits>()

const delegatedProps = reactiveOmit(props, 'class', 'as', 'asChild')

const forwarded = useForwardPropsEmits(delegatedProps, emits)

function preventScrollbarDismiss(event: PointerDownOutsideEvent) {
  const originalEvent = event.detail.originalEvent
  const target = originalEvent.target as HTMLElement
  if (originalEvent.offsetX > target.clientWidth || originalEvent.offsetY > target.clientHeight) {
    event.preventDefault()
  }
}
</script>

<template>
  <DialogPortal>
    <DialogOverlay v-scroll-surface data-scroll-surface data-scroll-axis="y"
      class="fixed inset-0 z-50 grid place-items-center overflow-y-auto bg-black/80  data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0"
    >
      <Card
        :as="RekaDialogContent"
        slot-name="dialog-content"
        data-modal-scroll-lock
        @interact-outside="keepNoticeInteractive"
        :class="
          cn(
            'relative z-50 grid w-full max-w-lg my-8 gap-4 p-6 duration-200 md:w-full',
            props.class,
          )
        "
        v-bind="{ ...$attrs, ...forwarded }"
        @pointer-down-outside="preventScrollbarDismiss"
      >
        <slot />

        <DialogClose
          class="absolute top-4 right-4 p-0.5 transition-colors rounded-md hover:bg-secondary"
        >
          <XIcon class="w-4 h-4" />
          <span class="sr-only">{{ $t('ui.close2') }}</span>
        </DialogClose>
      </Card>
    </DialogOverlay>
  </DialogPortal>
</template>
