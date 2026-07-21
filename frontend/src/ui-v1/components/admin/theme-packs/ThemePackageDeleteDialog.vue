<script setup lang="ts">
import { Button } from '@/ui-v1/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/ui-v1/components/ui/dialog'
import type { ThemePackage } from '@/themes/theme-package'

defineProps<{
  open: boolean
  theme?: ThemePackage
}>()

const emit = defineEmits<{
  'update:open': [open: boolean]
  confirm: []
}>()
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>Remove local theme package</DialogTitle>
        <DialogDescription>
          This removes the package from this browser and cannot be undone.
        </DialogDescription>
      </DialogHeader>
      <p class="text-sm">
        Remove <span class="font-semibold text-foreground">{{ theme?.name }}</span>?
      </p>
      <DialogFooter>
        <Button variant="outline" @click="emit('update:open', false)">Cancel</Button>
        <Button variant="destructive" @click="emit('confirm')">Remove package</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
