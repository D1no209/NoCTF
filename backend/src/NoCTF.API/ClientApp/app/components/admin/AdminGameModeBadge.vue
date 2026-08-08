<script setup lang="ts">
const props = defineProps<{ mode?: number | string | null }>()

const label = computed(() => {
  switch (props.mode) {
    case 0:
    case 'Ctf':
      return 'CTF'
    case 1:
    case 'Awd':
      return 'AWD'
    case 2:
    case 'Awdp':
      return 'AWDP'
    case 3:
    case 'Koh':
      return 'KoH'
    default:
      return '—'
  }
})
</script>

<template>
  <Badge variant="secondary">{{ label }}</Badge>
</template>
