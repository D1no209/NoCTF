<script setup lang="ts">
import { Loader2, RotateCcw, RotateCw, Scan } from 'lucide-vue-next'
import { nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { AVATAR_CROP_SIZE, AVATAR_OUTPUT_SIZE, drawAvatarCrop } from './avatarCrop'

const props = defineProps<{
  open: boolean
  file: File | null
  saving: boolean
}>()

const emit = defineEmits<{
  'update:open': [value: boolean]
  'save': [file: File]
}>()

const { t } = useI18n()
const preview = ref<HTMLCanvasElement | null>(null)
const sourceImage = ref<HTMLImageElement | null>(null)
const loadError = ref(false)
let sourceUrl: string | null = null

const crop = reactive({
  zoom: 1,
  positionX: 0,
  positionY: 0,
  rotation: 0,
})

function resetCrop() {
  crop.zoom = 1
  crop.positionX = 0
  crop.positionY = 0
  crop.rotation = 0
}

function releaseSource() {
  if (sourceUrl)
    URL.revokeObjectURL(sourceUrl)
  sourceUrl = null
  sourceImage.value = null
}

async function loadFile(file: File | null) {
  releaseSource()
  resetCrop()
  loadError.value = false
  if (!file)
    return

  sourceUrl = URL.createObjectURL(file)
  const image = new Image()
  image.decoding = 'async'
  image.onload = async () => {
    sourceImage.value = image
    await nextTick()
    renderPreview()
  }
  image.onerror = () => {
    loadError.value = true
  }
  image.src = sourceUrl
}

function renderPreview() {
  if (!preview.value || !sourceImage.value)
    return
  drawAvatarCrop(preview.value, sourceImage.value, crop)
}

function rotate(delta: number) {
  crop.rotation = (crop.rotation + delta + 360) % 360
}

async function createCroppedFile() {
  if (!sourceImage.value || props.saving)
    return

  const canvas = document.createElement('canvas')
  canvas.width = AVATAR_OUTPUT_SIZE
  canvas.height = AVATAR_OUTPUT_SIZE
  drawAvatarCrop(canvas, sourceImage.value, crop)
  const webp = await new Promise<Blob | null>(resolve =>
    canvas.toBlob(resolve, 'image/webp', 0.9),
  )
  const blob
    = webp ?? (await new Promise<Blob | null>(resolve => canvas.toBlob(resolve, 'image/png')))
  if (!blob)
    throw new Error('Avatar crop could not be encoded.')

  emit('save', new File([blob], webp ? 'avatar.webp' : 'avatar.png', { type: blob.type }))
}

watch(() => props.file, loadFile, { immediate: true })
watch(crop, renderPreview, { deep: true, flush: 'post' })
onBeforeUnmount(releaseSource)
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-[620px]">
      <DialogHeader>
        <DialogTitle>{{ t('home.avatarEditorTitle') }}</DialogTitle>
        <DialogDescription>{{ t('home.avatarEditorDescription') }}</DialogDescription>
      </DialogHeader>

      <div class="grid gap-5 py-2 md:grid-cols-[320px_minmax(0,1fr)]">
        <div
          class="relative size-[320px] max-w-full overflow-hidden border-2 border-border bg-muted"
        >
          <canvas
            ref="preview"
            :width="AVATAR_CROP_SIZE"
            :height="AVATAR_CROP_SIZE"
            class="size-full"
            :aria-label="t('home.avatarCropPreview')"
          />
          <div
            class="pointer-events-none absolute inset-4 rounded-full border-2 border-dashed border-white/90 shadow-[0_0_0_999px_rgba(0,0,0,0.28)]"
          />
          <div
            v-if="loadError"
            class="absolute inset-0 grid place-items-center bg-background p-6 text-center text-sm text-destructive"
          >
            {{ t('home.avatarLoadError') }}
          </div>
        </div>

        <div class="space-y-5">
          <div class="space-y-2">
            <Label for="avatar-zoom">{{ t('home.avatarZoom') }}</Label>
            <input
              id="avatar-zoom"
              v-model.number="crop.zoom"
              type="range"
              min="1"
              max="3"
              step="0.01"
              class="w-full accent-foreground"
            >
          </div>
          <div class="space-y-2">
            <Label for="avatar-position-x">{{ t('home.avatarHorizontal') }}</Label>
            <input
              id="avatar-position-x"
              v-model.number="crop.positionX"
              type="range"
              min="-1"
              max="1"
              step="0.01"
              class="w-full accent-foreground"
            >
          </div>
          <div class="space-y-2">
            <Label for="avatar-position-y">{{ t('home.avatarVertical') }}</Label>
            <input
              id="avatar-position-y"
              v-model.number="crop.positionY"
              type="range"
              min="-1"
              max="1"
              step="0.01"
              class="w-full accent-foreground"
            >
          </div>
          <div class="space-y-2">
            <Label>{{ t('home.avatarRotation') }}</Label>
            <div class="grid grid-cols-2 gap-2">
              <Button variant="outline" type="button" @click="rotate(-90)">
                <RotateCcw class="size-4" />
                −90°
              </Button>
              <Button variant="outline" type="button" @click="rotate(90)">
                <RotateCw class="size-4" />
                +90°
              </Button>
            </div>
          </div>
          <Button variant="ghost" size="sm" type="button" @click="resetCrop">
            <Scan class="size-4" />
            {{ t('home.avatarReset') }}
          </Button>
        </div>
      </div>

      <DialogFooter>
        <Button variant="outline" :disabled="saving" @click="emit('update:open', false)">
          {{ t('common.cancel') }}
        </Button>
        <Button :disabled="saving || !sourceImage || loadError" @click="createCroppedFile">
          <Loader2 v-if="saving" class="size-4 animate-spin" />
          {{ t('home.avatarSave') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
