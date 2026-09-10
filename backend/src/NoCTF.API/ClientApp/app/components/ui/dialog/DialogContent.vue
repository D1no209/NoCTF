<script setup lang="ts">
import { keepNoticeInteractive } from '../sonner/notice-events'
import type { DialogContentEmits, DialogContentProps } from 'reka-ui'

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
import { Button } from '~/components/ui/button'
import { Card } from '~/components/ui/card'
import DialogOverlay from '~/components/ui/dialog/DialogOverlay.vue'

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(defineProps<DialogContentProps & { class?: HTMLAttributes['class'], showCloseButton?: boolean }>(), {
  showCloseButton: true,
})
const emits = defineEmits<DialogContentEmits>()

const delegatedProps = reactiveOmit(props, 'class', 'as', 'asChild')

const forwarded = useForwardPropsEmits(delegatedProps, emits)
</script>

<template>
  <DialogPortal>
    <DialogOverlay />
    <Card
      :as="RekaDialogContent"
      slot-name="dialog-content"
      data-modal-scroll-lock
      data-scroll-surface data-scroll-axis="y"
      @interact-outside="keepNoticeInteractive"
      v-bind="{ ...$attrs, ...forwarded }"
      :class="cn('text-card-foreground data-open:animate-in data-closed:animate-out data-closed:fade-out-0 data-open:fade-in-0 data-closed:zoom-out-95 data-open:zoom-in-95 grid max-w-[calc(100%-2rem)] gap-4 p-4 duration-100 sm:max-w-sm fixed top-1/2 left-1/2 z-50 w-full -translate-x-1/2 -translate-y-1/2 outline-none', props.class)"
    >
      <slot />

      <DialogClose
        v-if="showCloseButton"
        data-slot="dialog-close"
        as-child
      >
        <Button variant="ghost" class="absolute top-2 right-2" size="icon-sm" :aria-label="$t('ui.close')">
          <XIcon aria-hidden="true" />
          <span class="sr-only">{{ $t('ui.close') }}</span>
        </Button>
      </DialogClose>
    </Card>
  </DialogPortal>
</template>
