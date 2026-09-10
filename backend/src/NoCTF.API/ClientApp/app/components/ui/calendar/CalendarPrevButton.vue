<script lang="ts" setup>
import type { CalendarPrevProps } from 'reka-ui'

import type { HTMLAttributes } from 'vue'
import { ChevronLeftIcon } from '@lucide/vue'
import { reactiveOmit } from '@vueuse/core'
import { CalendarPrev, useForwardProps } from 'reka-ui'
import { cn } from '@/lib/utils'
import { buttonVariants } from '@/components/ui/button'

const props = defineProps<CalendarPrevProps & { class?: HTMLAttributes['class'] }>()

const delegatedProps = reactiveOmit(props, 'class')

const forwardedProps = useForwardProps(delegatedProps)
</script>

<template>
  <CalendarPrev
    :aria-label="$t('dateTime.previousMonth')"
    data-slot="calendar-prev-button"
    :class="cn(
      buttonVariants({ variant: 'outline' }),
      'size-11 bg-transparent p-0 cursor-pointer disabled:opacity-50',
      props.class,
    )"
    v-bind="forwardedProps"
  >
    <slot>
      <ChevronLeftIcon class="cn-rtl-flip size-4" />
    </slot>
  </CalendarPrev>
</template>
