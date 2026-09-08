<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { AvatarVariants } from '~/components/ui/avatar'
import { AvatarRoot } from 'reka-ui'
import { cn } from '~/lib/utils'
import { avatarVariants } from '~/components/ui/avatar'

const props = defineProps<{
  class?: HTMLAttributes['class']
  size?: AvatarVariants['size']
}>()
</script>

<template>
  <AvatarRoot
    data-slot="avatar"
    :data-size="size ?? 'default'"
    :class="cn(avatarVariants({ size }), props.class)"
  >
    <slot />
  </AvatarRoot>
</template>
