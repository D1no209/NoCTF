<script setup lang="ts">
import type { PropType } from 'vue'
import { Loader2, Upload } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { Button } from '@/ui-v1/components/ui/button'
import { Card } from '@/ui-v1/components/ui/card'
import { Separator } from '@/ui-v1/components/ui/separator'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/ui-v1/components/ui/select'

interface Challenge {
  id: string
  title: string
}

defineProps({
  challenges: {
    type: Array as PropType<Challenge[]>,
    default: () => [],
  },
  patchChallenge: {
    type: String,
    required: true,
  },
  patchFile: {
    type: Object as PropType<File | null>,
    default: null,
  },
  patchLoading: {
    type: Boolean,
    default: false,
  },
  isDragOver: {
    type: Boolean,
    default: false,
  },
})

const emit = defineEmits<{
  'update:patchChallenge': [value: string]
  'update:patchFile': [value: File | null]
  'update:isDragOver': [value: boolean]
  submit: []
}>()

const { t } = useI18n()

function onPatchFileChange(event: Event) {
  const input = event.target as HTMLInputElement
  emit('update:patchFile', input.files?.[0] ?? null)
}
</script>

<template>
  <Card class="px-4 py-4">
    <div class="flex items-center gap-2 text-base font-bold uppercase tracking-[0.12em] text-foreground">
      <Upload class="size-5" />
      {{ t('awd.uploadPatch') }}
    </div>
    <Separator class="my-3" />

    <div class="space-y-4">
      <div class="space-y-2">
        <label class="text-xs font-bold uppercase text-muted-foreground">{{ t('common.challenge') }}</label>
        <Select :model-value="patchChallenge" @update:model-value="emit('update:patchChallenge', String($event))">
          <SelectTrigger>
            <SelectValue :placeholder="t('awd.selectChallenge')" />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="challenge in challenges" :key="challenge.id" :value="challenge.id">
                {{ challenge.title }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </div>

      <div
        class="group relative flex flex-col items-center justify-center gap-3 border-2 border-dashed p-6 transition-colors hover:bg-muted"
        :class="[
          isDragOver ? 'border-primary bg-primary/5' : 'border-muted-foreground/25',
          patchFile ? 'bg-muted/30' : '',
        ]"
        @dragover.prevent="emit('update:isDragOver', true)"
        @dragleave.prevent="emit('update:isDragOver', false)"
        @drop.prevent="emit('update:isDragOver', false); emit('update:patchFile', $event.dataTransfer?.files[0] || null)"
        @click="($refs.patchFileInput as HTMLInputElement)?.click()"
      >
        <div class="flex size-10 items-center justify-center border-2 border-border bg-muted transition-transform group-hover:scale-110">
          <Upload class="size-5 text-muted-foreground" />
        </div>
        <div class="text-center">
          <p class="text-sm font-medium">
            {{ patchFile ? patchFile.name : t('awd.dropFile') }}
          </p>
          <p class="mt-1 text-xs text-muted-foreground">
            {{ t('awd.patchFileHint') }}
          </p>
        </div>
        <input
          ref="patchFileInput"
          type="file"
          accept=".tar.gz,.tgz"
          class="sr-only"
          @change="onPatchFileChange"
        >
      </div>

      <Button
        variant="secondary"
        class="w-full"
        :disabled="patchLoading || !patchFile || !patchChallenge"
        @click="emit('submit')"
      >
        <Loader2 v-if="patchLoading" class="mr-2 size-4 animate-spin" />
        {{ t('awd.submitPatch') }}
      </Button>
    </div>
  </Card>
</template>
