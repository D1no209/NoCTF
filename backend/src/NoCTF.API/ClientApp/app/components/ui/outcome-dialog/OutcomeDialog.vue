<script setup lang="ts">
const props = defineProps<{
  open: boolean; heading: string; description: string; winner: string; avatar?: string | null; score: string;
  reason: string; corrected?: string | null; continueLabel: string; backLabel: string; nextLabel?: string | null; busy?: boolean; returnFocusId?: string
}>()
const emit = defineEmits<{ 'update:open': [value: boolean]; back: []; next: [] }>()
function restoreFocus(event: Event) {
  const target = props.returnFocusId ? document.getElementById(props.returnFocusId) : null
  if (target instanceof HTMLElement) { event.preventDefault(); target.focus({ preventScroll: true }) }
}
</script>
<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-lg" @close-auto-focus="restoreFocus">
      <DialogHeader><DialogTitle>{{ heading }}</DialogTitle><DialogDescription>{{ description }}</DialogDescription></DialogHeader>
      <div class="flex min-w-0 flex-col items-center gap-4 py-4 text-center" role="status">
        <Badge v-if="corrected" variant="secondary">{{ corrected }}</Badge>
        <Avatar class="size-20"><AvatarImage v-if="avatar" :src="avatar" :alt="winner" /><AvatarFallback>{{ winner.slice(0, 2) }}</AvatarFallback></Avatar>
        <p class="max-w-full break-words text-xl font-semibold">{{ winner }}</p>
        <p class="font-mono text-4xl font-bold tabular-nums text-primary">{{ score }}</p>
        <p class="max-w-[65ch] text-sm text-muted-foreground">{{ reason }}</p>
      </div>
      <DialogFooter class="flex-wrap"><Button variant="outline" @click="emit('update:open', false)">{{ continueLabel }}</Button><Button variant="ghost" @click="emit('back')">{{ backLabel }}</Button><Button v-if="nextLabel" :disabled="busy" @click="emit('next')">{{ nextLabel }}</Button></DialogFooter>
    </DialogContent>
  </Dialog>
</template>
