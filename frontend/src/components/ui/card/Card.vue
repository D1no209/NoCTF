<script setup lang="ts">
import type { HTMLAttributes } from "vue"
import { cn } from "@/lib/utils"

const props = defineProps<{
  class?: HTMLAttributes["class"]
}>()
</script>

<template>
  <div
    data-slot="card"
    :class="
      cn(
        'bg-card/95 text-card-foreground flex flex-col gap-6 rounded-xl border border-slate-900/10 py-6 shadow-[0_14px_45px_rgb(15_23_42/0.055)] backdrop-blur-sm',
        props.class,
      )
    "
  >
    <slot />
  </div>
</template>
