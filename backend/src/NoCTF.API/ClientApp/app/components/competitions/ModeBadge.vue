<script setup lang="ts">
import { GameMode, gameModeLabel } from '~/utils/labels'

const props = defineProps<{ mode?: number }>()

const label = computed(() => gameModeLabel(props.mode))

const variant = computed(() => {
  switch (props.mode) {
    case GameMode.Ctf:
      return 'default' as const
    case GameMode.Awd:
      return 'secondary' as const
    case GameMode.Awdp:
      return 'outline' as const
    case GameMode.Koh:
      return 'destructive' as const
    default:
      return 'secondary' as const
  }
})
</script>

<template>
  <Badge :variant="variant">{{ label }}</Badge>
</template>
