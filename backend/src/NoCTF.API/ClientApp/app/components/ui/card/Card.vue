<script setup lang="ts">
import type { PrimitiveProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { Primitive } from 'reka-ui'
import { cn } from '~/lib/utils'
import CardOrnament from './CardOrnament.vue'

const props = withDefaults(defineProps<{
  as?: PrimitiveProps['as']
  class?: HTMLAttributes['class']
  size?: 'default' | 'sm'
  slotName?: string
}>(), {
  as: 'div',
  size: 'default',
  slotName: 'card',
})
</script>

<template>
  <Primitive
    :as="as"
    :data-slot="slotName"
    :data-size="size"
    :class="cn('card-surface bg-card text-card-foreground gap-4 overflow-hidden rounded-xl py-4 text-sm has-data-[slot=card-footer]:pb-0 has-[>img:first-child]:pt-0 data-[size=sm]:gap-3 data-[size=sm]:py-3 data-[size=sm]:has-data-[slot=card-footer]:pb-0 *:[img:first-child]:rounded-t-xl *:[img:last-child]:rounded-b-xl group/card relative flex flex-col', props.class)"
  >
    <slot />
    <CardOrnament />
  </Primitive>
</template>
