<script lang="ts" setup>
import type { CalendarNextProps } from 'reka-ui'

import type { HTMLAttributes } from 'vue'
import { ChevronRightIcon } from '@lucide/vue'
import { reactiveOmit } from '@vueuse/core'
import { CalendarNext, useForwardProps } from 'reka-ui'
import { cn } from '@/lib/utils'
import { buttonVariants } from '@/components/ui/button'

const props = defineProps<CalendarNextProps & { class?: HTMLAttributes['class'] }>()

const delegatedProps = reactiveOmit(props, 'class')

const forwardedProps = useForwardProps(delegatedProps)
</script>

<template>
  <CalendarNext
    :aria-label="$t('dateTime.nextMonth')"
    data-slot="calendar-next-button"
    :class="cn(
      buttonVariants({ variant: 'outline' }),
      'size-11 bg-transparent p-0 cursor-pointer disabled:opacity-50',
      props.class,
    )"
    v-bind="forwardedProps"
  >
    <slot>
      <ChevronRightIcon class="cn-rtl-flip size-4" />
    </slot>
  </CalendarNext>
</template>
