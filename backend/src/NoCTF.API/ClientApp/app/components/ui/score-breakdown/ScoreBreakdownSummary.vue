<script setup lang="ts">
defineProps<{ grossPoints: number; deductionPoints: number; netPoints: number; grossLabel: string; deductionLabel: string; netLabel: string }>()
</script>
<template>
  <dl class="grid grid-cols-3 gap-x-4 gap-y-1 text-sm"><div><dt class="text-xs text-muted-foreground">{{ grossLabel }}</dt><dd class="font-mono tabular-nums">{{ grossPoints }}</dd></div><div><dt class="text-xs text-muted-foreground">{{ deductionLabel }}</dt><dd class="font-mono tabular-nums">{{ deductionPoints }}</dd></div><div><dt class="text-xs text-muted-foreground">{{ netLabel }}</dt><dd class="font-mono font-semibold tabular-nums">{{ netPoints }}</dd></div></dl>
</template>
